using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

public enum Kind { Nails, Hair, Spa, Makeup, Pedicure, Lashes, Brows }

// Bootstraps the whole salon at runtime (no scene content needed): world, unlock tree, customers, save.
public class Game : MonoBehaviour
{
    public const string SalonName = "Nenni's Saloon"; // app name

    // Each new salon (prestige) gets a name, a lawn color and one more x of income.
    static readonly string[] Lawns = { "#A9DFC0", "#F3E3B5", "#C9D7F2", "#F7C9D9" };
    public static string[] ServiceName => Theme.Service;
    public static readonly int[] UpgradeBase = { 300, 600, 1000, 1400, 1900, 2500, 3200 };

    public static Game I;
    public static readonly Quaternion CamRot = Quaternion.Euler(55, 0, 0);
    public static readonly Vector3 Door = new(0, 0, -2.5f);

    // Per-kind tuning, indexed by (int)Kind.
    public static string[] ProductName => Theme.Product;
    public static readonly int[] Price = { 6, 14, 22, 32, 42, 55, 70 };
    public static readonly float[] ServiceTime = { 1.6f, 2.2f, 2.8f, 2.4f, 2.6f, 3.0f, 2.6f };
    public static readonly Color[] Tint =
    {
        Fx.Hex("#FF4F8B"), Fx.Hex("#B388FF"), Fx.Hex("#4CC99A"), Fx.Hex("#E8A33D"), Fx.Hex("#4FB3E8"), Fx.Hex("#7E57C2"), Fx.Hex("#B5835A"),
    };

    // Test aid: launch with -autoplay and the player walks to whatever the guide arrow points at.
    public static readonly bool Autoplay = Environment.GetCommandLineArgs().Contains("-autoplay");

    const int Choices = 3;          // unlock tiles offered at the same time
    const float Markup = 0.25f;     // picking one makes every other offered tile 25% (of base) pricier...
    const int MaxMarkups = 4;       // ...up to double its base price

    static readonly float[] RowZ = { 4.5f, 8.1f, 11.7f, 15.3f, 18.9f, 22.5f, 26.1f };
    static readonly float[] RackX = { -1.95f, 1.95f, -3.9f, 3.9f, -5.85f, 5.85f, 0 };
    // Seat i of each service: wall side (-1 left, 1 right) and row.
    static readonly (int side, int row)[][] Seats =
    {
        new[] { (-1, 0), (-1, 1) }, new[] { (1, 0), (1, 1) }, // Nails, Hair
        new[] { (-1, 2), (-1, 3) }, new[] { (1, 2), (1, 3) }, // Spa, Makeup
        new[] { (-1, 4), (-1, 5) }, new[] { (1, 4), (1, 5) }, // Pedicure, Lashes
        new[] { (-1, 6), (1, 6) },                             // Brows: the last row, one per side
    };
    static readonly float[] RegisterX = { -2.7f, -4.4f, -6.1f }; // front-left corner, next to the entrance

    public Player player;
    public Room room;
    public readonly List<Carrier> carriers = new();
    public readonly List<Station> stations = new();
    public readonly Dictionary<Kind, Rack> racks = new();
    public readonly List<Register> registers = new();
    public int cash;
    public int Prestige => Theme.Prestige;
    public int Multiplier => 1 + Prestige;
    public string SalonTitle => Theme.Title;
    public bool Impatience => bought.Count >= 6;                       // not during the tutorial
    public bool VipAvailable => stations.Select(s => s.kind).Distinct().Count() >= 2 && bought.Count >= 8;
    public Customer vip;
    public Pet pet;
    float vipTimer = 60, groupTimer = 100, rushTimer = 200, rushLeft;
    int rushEarned;
    bool rushLost;

    class Step
    {
        public string id, label, desc; // desc: what it does, written on the tile
        public int baseCost;
        public Func<Vector3> pos; // null: shown in a free aisle slot
        public Action<bool> build;
        public Step[] needs;
    }

    readonly Dictionary<(Kind, int), Station> stationAt = new();
    readonly List<Step> steps = new();
    readonly HashSet<string> bought = new();
    readonly Dictionary<string, int> paid = new(), markups = new(), levels = new();
    readonly Dictionary<string, UnlockZone> zones = new();
    Camera cam;
    Transform arrow;
    AudioSource music;
    Task<float[]> musicTask;
    float spawnTimer = 1f, saveTimer;
    bool dirty;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        new GameObject("Game").AddComponent<Game>();
        // Opening a new salon reloads the scene; rebuild everything from the save.
        SceneManager.sceneLoaded += (_, _) => { if (!I) new GameObject("Game").AddComponent<Game>(); };
    }

    public static string Money(int v) => v.ToString("#,0").Replace(',', '.');

    void Awake()
    {
        I = this;
        Theme.Load();
        Application.targetFrameRate = 60;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        QualitySettings.antiAliasing = 4;
        QualitySettings.shadowDistance = 40;
        QualitySettings.shadowResolution = ShadowResolution.VeryHigh;

        BuildWorld();
        Hud.Create();
        player = Player.Create(new Vector3(0, 0, 3));
        ApplyCosmetics();
        carriers.Add(player.carrier);
        BuildSteps();
        Load();
        StartMusic();
        cam.transform.position = CameraTarget();
    }

    // ---------- Layout ----------

    static Vector3 PadPos(Kind k, int i)
    {
        var (side, row) = Seats[(int)k][i];
        return new(3.6f * side, 0, RowZ[row]);
    }
    Vector3 RackPos(Kind k) => new(RackX[(int)k], 0, room.zMax - 0.9f);
    Vector3 RackPad(Kind k) => RackPos(k) - new Vector3(0, 0, 1.3f);

    void AddStation(Kind k, int i, bool pop)
    {
        // Piles pair up between rows 0-1, 2-3, 4-5 (client, cash, cash, client); the lone top row keeps its cash behind.
        var (side, row) = Seats[(int)k][i];
        var s = Station.Create(k, new Vector3(5 * side, 0, RowZ[row]), -90 * side, row % 2 == 0);
        stations.Add(s);
        stationAt[(k, i)] = s;
        s.id = k + "" + i;
        s.training = levels.GetValueOrDefault("t_" + s.id, 1);
        s.SetLevel(levels.GetValueOrDefault(s.id, 1), false);
        if (pop) Fx.Pop(s.transform);
    }

    void AddRack(Kind k, bool pop)
    {
        var r = Rack.Create(k, RackPos(k));
        racks[k] = r;
        if (pop) Fx.Pop(r.transform);
    }

    void AddRegister(int i, bool pop)
    {
        var r = Register.Create(new Vector3(RegisterX[i], 0, 1f));
        registers.Add(r);
        if (pop) Fx.Pop(r.transform);
    }

    // Two columns of tiles along the central aisle, clear of rugs, registers and rack pads.
    // Customers walk between the columns.
    Vector3[] Slots()
    {
        var list = new List<Vector3>();
        for (float z = 3.8f; z + 0.9f <= room.zMax - 3.3f; z += 2.7f)
        {
            list.Add(new Vector3(-1.15f, 0, z));
            list.Add(new Vector3(1.15f, 0, z));
        }
        return list.ToArray();
    }

    void Expand(float xMax, float zMax, bool pop)
    {
        room.Resize(xMax, zMax, pop);
        foreach (var (k, r) in racks) r.MoveTo(RackPos(k), pop);
        if (pop) Hud.I.Toast("Salone più grande!", 2.5f);
    }

    // ---------- Unlock tree ----------
    // Steps are listed in the order they are offered; each needs its prerequisites bought first.

    void BuildSteps()
    {
        Step Add(string id, string label, int cost, Func<Vector3> pos, Action<bool> build, Step[] needs, string desc = null)
        {
            var s = new Step { id = id, label = label, desc = desc, baseCost = cost, pos = pos, build = build, needs = needs };
            steps.Add(s);
            return s;
        }
        Step Seat(string id, int cost, Kind k, int i, params Step[] needs) =>
            Add(id, Theme.Seat[(int)k], cost, () => PadPos(k, i), pop => AddStation(k, i, pop), needs);
        Step Shelf(string id, Kind k, int cost, params Step[] needs) =>
            Add(id, "Scaffale " + ProductName[(int)k], cost, () => RackPad(k), pop => AddRack(k, pop), needs);
        Step Hire(string id, int cost, Kind k, int i, params Step[] needs) =>
            Add(id, "Assumi " + Theme.Staff[(int)k], cost, null, pop => stationAt[(k, i)].Hire(pop), needs, "Lavora al posto tuo");
        Step Upgrade(string id, string label, string desc, int cost, Action apply, params Step[] needs) =>
            Add(id, label, cost, null, _ => apply(), needs, desc);
        Step Clerk(string id, int cost, params Step[] needs) =>
            Add(id, "Assumi Magazziniera", cost, null, pop => Runner.Create(new Vector3(0, 0, 6.3f), pop), needs, "Rifornisce le postazioni");
        Step Grow(string id, int cost, float xMax, float zMax, params Step[] needs) =>
            Add(id, "Ingrandisci salone", cost, null, pop => Expand(xMax, zMax, pop), needs, "Più spazio nel salone");
        Step Till(string id, int cost, int i, params Step[] needs) =>
            Add(id, "Cassa", cost, () => new Vector3(RegisterX[i], 0, 1.2f), pop => AddRegister(i, pop), needs);
        Step Cashier(string id, int cost, int i, params Step[] needs) =>
            Add(id, "Assumi Cassiera", cost, null, pop => registers[i].Hire(pop), needs, "Incassa al posto tuo");
        Step Furnish(string id, string label, string desc, int cost, params Step[] needs) =>
            Add(id, label, cost, null, pop => Furnishing.Build(id, pop), needs, desc);

        var nails1 = Seat("nails1", 20, Kind.Nails, 1);
        var bag1 = Upgrade("bag1", "Borsa più grande", "Porti 2 prodotti in più", 60, () => player.carrier.capacity += 2);
        Cashier("cashier0", 150, 0);
        Hire("nailStaff0", 120, Kind.Nails, 0);
        var wide = Grow("wide", 150, 7, 12);
        var hairShelf = Shelf("hairShelf", Kind.Hair, 60, wide);
        var hair0 = Seat("hair0", 90, Kind.Hair, 0, hairShelf);
        Upgrade("speed", "Scarpe veloci", "Cammini più veloce", 250, () => player.speed += 1.5f, bag1);
        Hire("nailStaff1", 220, Kind.Nails, 1, nails1);
        var till1 = Till("till1", 250, 1, nails1);
        var hair1 = Seat("hair1", 200, Kind.Hair, 1, hair0);
        Furnish("sofa", "Divanetto", "Le clienti aspettano di più", 400, wide);
        Cashier("cashier1", 600, 1, till1);
        Hire("hairStaff0", 380, Kind.Hair, 0, hair0);
        var deep = Grow("deep", 600, 7, 16.5f, wide);
        var spaShelf = Shelf("spaShelf", Kind.Spa, 350, deep);
        var spa0 = Seat("spa0", 450, Kind.Spa, 0, spaShelf);
        var clerk1 = Clerk("clerk1", 900, hairShelf);
        Upgrade("bag2", "Borsa XL", "Porti 3 prodotti in più", 700, () => player.carrier.capacity += 3, bag1);
        Hire("hairStaff1", 750, Kind.Hair, 1, hair1);
        var makeupShelf = Shelf("makeupShelf", Kind.Makeup, 800, deep);
        var makeup0 = Seat("makeup0", 1000, Kind.Makeup, 0, makeupShelf);
        Furnish("coffee", "Macchina del caffè", "Dipendenti più veloci", 1100, wide);
        Hire("spaStaff0", 1200, Kind.Spa, 0, spa0);
        var till2 = Till("till2", 1200, 2, till1);
        Cashier("cashier2", 1600, 2, till2);
        Furnish("sign", "Insegna luminosa", "Arrivano più clienti", 1500);
        var full = Grow("full", 1800, 7, 20.5f, deep);
        var spa1 = Seat("spa1", 1500, Kind.Spa, 1, full, spa0);
        var makeup1 = Seat("makeup1", 2000, Kind.Makeup, 1, full, makeup0);
        Hire("makeupStaff0", 2200, Kind.Makeup, 0, makeup0);
        Furnish("carpet", "Tappeto rosso", "VIP più spesso e più ricche", 2600);
        Hire("spaStaff1", 2500, Kind.Spa, 1, spa1);
        Hire("makeupStaff1", 2800, Kind.Makeup, 1, makeup1);
        var clerk2 = Clerk("clerk2", 3000, clerk1);

        // The new wing: pedicure and lashes, then one more row for brows.
        var wing = Grow("wing", 5000, 7, 27.7f, full);
        var pediShelf = Shelf("pediShelf", Kind.Pedicure, 3500, wing);
        var pedi0 = Seat("pedi0", 4000, Kind.Pedicure, 0, pediShelf);
        var lashShelf = Shelf("lashShelf", Kind.Lashes, 4500, wing);
        var lash0 = Seat("lash0", 5000, Kind.Lashes, 0, lashShelf);
        Hire("pediStaff0", 5500, Kind.Pedicure, 0, pedi0);
        var pedi1 = Seat("pedi1", 6000, Kind.Pedicure, 1, pedi0);
        Hire("lashStaff0", 6500, Kind.Lashes, 0, lash0);
        var lash1 = Seat("lash1", 7000, Kind.Lashes, 1, lash0);
        Clerk("clerk3", 7000, clerk2);
        Hire("pediStaff1", 7500, Kind.Pedicure, 1, pedi1);
        Hire("lashStaff1", 8500, Kind.Lashes, 1, lash1);
        var wing2 = Grow("wing2", 9000, 7, 31.3f, wing);
        var browShelf = Shelf("browShelf", Kind.Brows, 8000, wing2);
        var brow0 = Seat("brow0", 9000, Kind.Brows, 0, browShelf);
        Hire("browStaff0", 10000, Kind.Brows, 0, brow0);
        var brow1 = Seat("brow1", 11000, Kind.Brows, 1, brow0);
        Hire("browStaff1", 13000, Kind.Brows, 1, brow1);
    }

    public bool Has(string id) => bought.Contains(id);

    int Cost(Step s) => Mathf.RoundToInt(s.baseCost * (1 + Markup * markups.GetValueOrDefault(s.id)) / 5f) * 5;

    IEnumerable<Step> Offered() =>
        steps.Where(s => !bought.Contains(s.id) && s.needs.All(n => bought.Contains(n.id))).Take(Choices);

    // Syncs the floor tiles with the offered steps. Buildables sit where they will appear;
    // everything else keeps its aisle slot or takes a free one. Tiles whose spot changed are rebuilt.
    void RefreshZones(bool popNew)
    {
        var offered = Offered().ToList();
        var slots = Slots();
        var want = new Dictionary<string, Vector3>();
        var usedSlots = new HashSet<int>();
        foreach (var s in offered)
        {
            if (s.pos != null) want[s.id] = s.pos();
            else if (zones.TryGetValue(s.id, out var z))
            {
                int i = Array.IndexOf(slots, z.transform.position);
                if (i >= 0 && usedSlots.Add(i)) want[s.id] = slots[i];
            }
        }
        foreach (var s in offered.Where(s => !want.ContainsKey(s.id)))
        {
            int i = Enumerable.Range(0, slots.Length).First(k => !usedSlots.Contains(k));
            usedSlots.Add(i);
            want[s.id] = slots[i];
        }

        foreach (var (id, z) in zones.ToList())
        {
            if (want.TryGetValue(id, out var p) && z.transform.position == p) continue;
            paid[id] = z.paid;
            Destroy(z.gameObject);
            zones.Remove(id);
        }
        foreach (var s in offered)
        {
            if (zones.ContainsKey(s.id)) continue;
            var step = s;
            var z = UnlockZone.Create(s.label, s.desc, Cost(s), paid.GetValueOrDefault(s.id), want[s.id], () => Purchased(step));
            zones[s.id] = z;
            if (popNew) Fx.Pop(z.transform);
        }
    }

    void Purchased(Step s)
    {
        bought.Add(s.id);
        paid.Remove(s.id);
        zones.Remove(s.id);
        foreach (var (id, z) in zones)
        {
            markups[id] = Mathf.Min(MaxMarkups, markups.GetValueOrDefault(id) + 1);
            z.SetCost(Cost(steps.First(x => x.id == id)));
        }
        s.build(true);
        RefreshZones(true);
        RefreshPrestigeZone();
        dirty = true;
        if (bought.Count == steps.Count) Hud.I.Toast("Salone completato!", 4);
    }

    // ---------- New salon (prestige) ----------

    int PrestigeCost => 15000 * Multiplier;

    // After everything is bought, one last tile opens the next salon.
    void RefreshPrestigeZone()
    {
        if (bought.Count < steps.Count || zones.ContainsKey("prestige")) return;
        var z = UnlockZone.Create("Apri " + Theme.NextTitle.Replace("Nenni's ", ""), $"Guadagni x{Multiplier + 1}", PrestigeCost,
            paid.GetValueOrDefault("prestige"), Slots()[0], AskPrestige);
        zones["prestige"] = z;
    }

    void AskPrestige()
    {
        zones.Remove("prestige");
        paid.Remove("prestige");
        Hud.I.Confirm($"Aprire {Theme.NextTitle}?",
            $"Ricominci da un salone nuovo\nma guadagni x{Multiplier + 1} per sempre.\nVestiti e decorazioni restano tuoi.",
            "Apri!", () =>
            {
                PlayerPrefs.SetInt("v2.prestige", Prestige + 1);
                foreach (var key in new[] { "v2.cash", "v2.wallet", "v2.bought", "v2.paid", "v2.markups", "v2.levels" }) PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
                Time.timeScale = 1;
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            },
            () =>
            {
                AddCash(PrestigeCost, track: false); // changed her mind: refund
                RefreshPrestigeZone();
            });
    }

    // ---------- Station levels ----------

    public void Upgrade(Station s)
    {
        if (s.level >= Station.MaxLevel || cash < s.UpgradeCost) return;
        AddCash(-s.UpgradeCost);
        s.SetLevel(s.level + 1, true);
        levels[s.id] = s.level;
        Fx.Sfx("unlock", 0.6f);
        Save();
    }

    // ---------- Staff training ----------

    public const int MaxClerkLevel = 3;
    public int ClerkLevel => levels.GetValueOrDefault("t_clerks", 1);
    public int ClerkTrainCost => 1500 << (ClerkLevel - 1);

    public void Train(Station s)
    {
        if (s.training >= Station.MaxTraining || cash < s.TrainCost) return;
        AddCash(-s.TrainCost);
        s.SetTraining(s.training + 1, true);
        levels["t_" + s.id] = s.training;
        Fx.Sfx("unlock", 0.6f);
        Save();
    }

    public void TrainClerks()
    {
        if (ClerkLevel >= MaxClerkLevel || cash < ClerkTrainCost) return;
        AddCash(-ClerkTrainCost);
        levels["t_clerks"] = ClerkLevel + 1;
        Runner.Retrain();
        Fx.Sfx("unlock", 0.6f);
        Save();
    }

    // ---------- Cosmetics ----------

    public void ApplyCosmetics(bool pop = false)
    {
        player.walker.SetOutfit(Cosmetics.Equipped(Cosmetics.Outfits).a);
        player.walker.SetHair(Cosmetics.Equipped(Cosmetics.Hair).a);
        room.Restyle();
        bool hope = Cosmetics.DecorOn(Cosmetics.Pets[0].id);
        if (hope && !pet) pet = Pet.Create(player.transform.position - player.transform.forward * 1.2f, pop);
        else if (!hope && pet) Destroy(pet.gameObject);
    }

    // ---------- VIP and special customers ----------

    // VIPs pay 5x the price of their services, the bride and the star 8x; the red carpet adds 30%.
    public int VipReward(Customer c)
    {
        int x = c.special == Customer.Special.Vip ? 5 : 8;
        return Mathf.RoundToInt(x * c.Plan.Sum(k => Price[(int)k]) * Multiplier * (Has("carpet") ? 1.3f : 1));
    }

    static readonly Kind[] BridePlan = { Kind.Hair, Kind.Makeup, Kind.Nails };

    void SpawnVip()
    {
        vipTimer -= Time.deltaTime;
        if (vip || vipTimer > 0 || !VipAvailable) return;
        var kinds = stations.Select(s => s.kind).Distinct().ToList();
        float r = Random.value;
        var type = r < 0.3f && BridePlan.All(kinds.Contains) ? Customer.Special.Bride
            : r < 0.55f ? Customer.Special.Star
            : Customer.Special.Vip;
        var plan = type switch
        {
            Customer.Special.Bride => BridePlan,
            Customer.Special.Star => new[] { kinds[Random.Range(0, kinds.Count)] },
            _ => kinds.OrderBy(_ => Random.value).Take(Random.Range(2, 4)).ToArray(),
        };
        var first = stations.FirstOrDefault(s => s.kind == plan[0] && !s.customer);
        if (!first) { vipTimer = 3; return; }
        vip = Customer.Spawn(first, plan, type);
        Hud.I.Toast(type switch
        {
            Customer.Special.Bride => "Arriva una sposa!",
            Customer.Special.Star => "Arriva una star!",
            _ => "Arriva una cliente VIP!",
        }, 2.5f);
        Fx.Sfx("unlock", 0.5f);
    }

    public void VipEnded(Customer c, bool happy)
    {
        vip = null;
        vipTimer = Random.Range(70f, 110f) * (Has("carpet") ? 0.7f : 1);
        Hud.I.Toast(happy ? c.Title + " soddisfatta!" : c.Who + " se n'è andata...", 2.5f);
    }

    // Two or three friends with matching outfits and pink bows: if none of them storms off, a bonus as big as their bill.
    void SpawnGroup()
    {
        groupTimer -= Time.deltaTime;
        if (groupTimer > 0) return;
        groupTimer = Random.Range(90f, 140f);
        if (bought.Count < 10) return;
        var free = stations.Where(s => !s.customer).OrderBy(_ => Random.value).Take(3).ToList();
        if (free.Count < 2) { groupTimer = 5; return; }
        var outfit = Fx.Outfit();
        int left = free.Count, bonus = free.Sum(s => s.ServicePrice);
        bool lost = false;
        foreach (var s in free)
            Customer.Spawn(s, friendOutfit: outfit).onLeave = served =>
            {
                lost |= !served;
                if (--left > 0) return;
                if (lost) { Hud.I.Toast("Un'amica se n'è andata...", 2.5f); return; }
                AddCash(bonus, track: false);
                Fx.Sfx("unlock", 0.6f);
                Hud.I.Toast($"Amiche felici!\n<size=50>Bonus +{Money(bonus)} €</size>", 3);
            };
        Hud.I.Toast("Arriva un gruppo di amiche!", 2.5f);
    }

    // ---------- Rush hour ----------
    // Every few minutes customers pour in for 45 s; losing none of them pays half of what the rush earned.

    public bool Rush => rushLeft > 0;
    int RushPrize => rushEarned / 2;

    void UpdateRush()
    {
        Hud.I.ShowRush(rushLeft, RushPrize, rushLost);
        if (Rush)
        {
            rushLeft -= Time.deltaTime;
            if (Rush) return;
            rushTimer = Random.Range(180f, 260f);
            if (rushLost || RushPrize == 0) { Hud.I.Toast("Ora di punta finita", 2.5f); return; }
            AddCash(RushPrize, track: false);
            Fx.Sfx("unlock", 0.7f);
            Hud.I.Toast($"Ora di punta superata!\n<size=50>Premio +{Money(RushPrize)} €</size>", 3.5f);
            return;
        }
        if (bought.Count < 12) return;
        rushTimer -= Time.deltaTime;
        if (rushTimer > 0) return;
        rushLeft = 45;
        rushEarned = 0;
        rushLost = false;
        Hud.I.Toast("Ora di punta!\n<size=50>Non perdere nessuna cliente</size>", 3);
        Fx.Sfx("unlock", 0.6f);
    }

    public void Paid(int price) { if (Rush) rushEarned += price; }
    public void CustomerLost() { if (Rush) rushLost = true; }

    // ---------- Save ----------

    static string Pack(Dictionary<string, int> d) => string.Join(",", d.Where(kv => kv.Value > 0).Select(kv => kv.Key + ":" + kv.Value));

    static void Unpack(string s, Dictionary<string, int> into)
    {
        foreach (var part in s.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split(':');
            if (kv.Length == 2 && int.TryParse(kv[1], out var v)) into[kv[0]] = v;
        }
    }

    void Load()
    {
        cash = Autoplay && Environment.GetCommandLineArgs().Contains("-rich") ? 100000 : PlayerPrefs.GetInt("v2.cash", 0);
        foreach (var id in PlayerPrefs.GetString("v2.bought", "").Split(',', StringSplitOptions.RemoveEmptyEntries)) bought.Add(id);
        Unpack(PlayerPrefs.GetString("v2.paid", ""), paid);
        Unpack(PlayerPrefs.GetString("v2.markups", ""), markups);
        Unpack(PlayerPrefs.GetString("v2.levels", ""), levels);

        AddRack(Kind.Nails, false);
        AddStation(Kind.Nails, 0, false);
        AddRegister(0, false);
        foreach (var s in steps)
            if (bought.Contains(s.id)) s.build(false);
        RefreshZones(false);
        RefreshPrestigeZone();

        int carried = PlayerPrefs.GetInt("v2.wallet", 0);
        if (carried > 0) player.wallet.Add(Fx.Bill(player.transform.position + Vector3.up), carried);

        Hud.I.SetCash(cash, false);
        Hud.I.RefreshBadges();
        if (bought.Count == 0 && cash == 0) Hud.I.Toast(Prestige > 0 ? "Benvenuta in " + SalonTitle + "!" : "Trascina per muoverti", 3.5f);

        // After an update, show once what is new (a fresh install just starts playing).
        if (PlayerPrefs.GetInt("news", 0) < Updater.Version)
        {
            PlayerPrefs.SetInt("news", Updater.Version);
            if (bought.Count > 0 && !Autoplay) Hud.I.ShowNews();
        }
    }

    void Save()
    {
        foreach (var (id, z) in zones) paid[id] = z.paid;
        PlayerPrefs.SetInt("v2.cash", cash);
        PlayerPrefs.SetInt("v2.wallet", player.wallet.Value);
        PlayerPrefs.SetString("v2.bought", string.Join(",", bought));
        PlayerPrefs.SetString("v2.paid", Pack(paid));
        PlayerPrefs.SetString("v2.markups", Pack(markups));
        PlayerPrefs.SetString("v2.levels", Pack(levels));
        Missions.Flush();
        PlayerPrefs.Save();
        dirty = false;
        saveTimer = 0;
    }

    void OnApplicationPause(bool paused) { if (paused) Save(); }
    void OnApplicationQuit() => Save();

    // Banked money counts toward the "earn" mission unless it is itself a reward or refund.
    public void AddCash(int v, bool track = true)
    {
        cash += v;
        dirty = true;
        Hud.I.SetCash(cash, v > 0);
        if (v > 0 && track) Missions.Earned(v);
    }

    // ---------- Music ----------

    public void ApplySettings() => music.mute = !Settings.Music;

    void StartMusic()
    {
        music = gameObject.AddComponent<AudioSource>();
        music.loop = true;
        music.volume = 0.35f;
        music.mute = !Settings.Music;
        musicTask = Task.Run(Music.Render); // a few seconds of synthesis; keep startup instant

    }

    // ---------- Loop ----------

    void Update()
    {
        SpawnCustomers();
        SpawnVip();
        SpawnGroup();
        UpdateRush();
        Hud.I.ShowVip(vip);
        UpdateArrow();
        if (musicTask is { IsCompleted: true })
        {
            if (musicTask.Status == TaskStatus.RanToCompletion)
            {
                music.clip = Music.ToClip(musicTask.Result);
                music.Play();
            }
            musicTask = null;
        }
        saveTimer += Time.deltaTime;
        if (dirty && saveTimer > 2) Save();
    }

    void LateUpdate()
    {
        cam.fieldOfView = Camera.HorizontalToVerticalFieldOfView(30, cam.aspect);
        cam.transform.position = Vector3.Lerp(cam.transform.position, CameraTarget(), 1 - Mathf.Exp(-6 * Time.deltaTime));
    }

    Vector3 CameraTarget()
    {
        var p = player.transform.position;
        float x0 = Room.XMin + 4.5f, x1 = room.xMax - 4.5f, z1 = Mathf.Max(2f, room.zMax - 5f);
        float x = x0 < x1 ? Mathf.Clamp(p.x, x0, x1) : (x0 + x1) / 2;
        var focus = new Vector3(x, 0, Mathf.Clamp(p.z, 2f, z1) + 1f);
        return focus + CamRot * new Vector3(0, 0, -22);
    }

    void SpawnCustomers()
    {
        spawnTimer -= Time.deltaTime;
        if (spawnTimer > 0) return;
        var free = stations.Where(s => !s.customer).ToList();
        if (free.Count == 0) { spawnTimer = 0.5f; return; }
        Customer.Spawn(free[Random.Range(0, free.Count)]);
        spawnTimer = Random.Range(1.2f, 3f) / Mathf.Sqrt(stations.Count) / (Has("sign") ? 1.5f : 1) / (Rush ? 3 : 1);
    }

    // Bouncing pointer: teaches the loop in the first salon only, gone after the first three unlocks.
    void UpdateArrow()
    {
        var target = Prestige == 0 && bought.Count < 3 ? ArrowTarget() : null;
        bool show = target.HasValue && !Fx.Near(player.transform.position, target.Value, 1.2f);
        if (arrow.gameObject.activeSelf != show) arrow.gameObject.SetActive(show);
        if (!show) return;
        arrow.position = target.Value + Vector3.up * (2.2f + Mathf.Abs(Mathf.Sin(Time.time * 4)) * 0.5f);
        arrow.Rotate(0, 120 * Time.deltaTime, 0);
    }

    public Vector2 AutoStick()
    {
        var t = ArrowTarget();
        if (!t.HasValue) return Vector2.zero;
        var d = t.Value - player.transform.position;
        return d.sqrMagnitude < 0.09f ? Vector2.zero : new Vector2(d.x, d.z).normalized;
    }

    Vector3? ArrowTarget()
    {
        var affordable = zones.Values.Where(z => cash >= z.Remaining).OrderBy(z => z.Remaining).FirstOrDefault();
        if (affordable) return affordable.transform.position;
        if (bought.Count >= 3 && !Autoplay) return null;

        var carrier = player.carrier;
        foreach (var s in stations)
            if (s.CustomerSeated && s.tray.Count == 0 && carrier.Has(s.kind)) return s.Pad;
        foreach (var s in stations)
            if (s.NeedsWorker) return s.Pad;
        if (carrier.items.Count > 0)
        {
            var s = stations.Where(x => carrier.Has(x.kind) && x.tray.Count < Station.TrayMax).OrderBy(x => x.tray.Count).FirstOrDefault();
            if (s) return s.Pad;
        }
        foreach (var s in stations)
            if (s.tray.Count == 0 && !s.HasStaff && racks.TryGetValue(s.kind, out var r)) return r.Pad;
        var pile = stations.OrderByDescending(x => x.PileValue).FirstOrDefault();
        bool pileWaiting = pile && pile.PileValue > 0;
        if (player.wallet.Count > 0 && (!pileWaiting || player.wallet.Value >= 30))
            return registers.OrderBy(r => (r.Pad - player.transform.position).sqrMagnitude).First().Pad;
        if (pileWaiting) return pile.Pile.transform.position;
        return null;
    }

    // ---------- World ----------

    void BuildWorld()
    {
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.6f, 0.56f, 0.66f);
        RenderSettings.ambientEquatorColor = new Color(0.56f, 0.52f, 0.6f);
        RenderSettings.ambientGroundColor = new Color(0.42f, 0.38f, 0.46f);

        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 0.7f;
        sun.color = new Color(1f, 0.95f, 0.9f);
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.45f;
        sun.transform.rotation = Quaternion.Euler(60, -30, 0);

        cam = new GameObject("Camera").AddComponent<Camera>();
        cam.tag = "MainCamera";
        cam.gameObject.AddComponent<AudioListener>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        var lawn = Fx.Hex(Lawns[Prestige % Lawns.Length]);
        cam.backgroundColor = lawn;
        cam.allowMSAA = true;
        cam.transform.rotation = CamRot;
        cam.nearClipPlane = 1;
        cam.farClipPlane = 80;

        var world = new GameObject("World").transform;
        Fx.Box(world, new(0, -0.6f, 8), new(80, 1, 80), lawn);
        Outdoor(world);
        room = Room.Create();

        arrow = new GameObject("Arrow").transform;
        var arrowColor = Fx.Hex("#FF3B6B");
        Fx.Box(arrow, new(0, 0.55f, 0), new(0.22f, 0.6f, 0.22f), arrowColor);
        Fx.Box(arrow, new(0, 0.05f, 0), new(0.5f, 0.5f, 0.22f), arrowColor).transform.localRotation = Quaternion.Euler(0, 0, 45);
    }

    // ---------- Outside: street, sidewalk, greenery ----------

    static void Outdoor(Transform world)
    {
        Color white = Color.white, pink = Fx.Hex("#FF9EBB");

        // Sidewalk with curb, then the road with lane dashes.
        Fx.Box(world, new(0, -0.06f, -2.4f), new(60, 0.12f, 3.6f), Fx.Hex("#E3D8EC"));
        Fx.Box(world, new(0, -0.03f, -4.25f), new(60, 0.12f, 0.22f), white);
        Fx.Box(world, new(0, -0.09f, -6.9f), new(60, 0.1f, 5f), Fx.Hex("#BDB6CF"));
        for (float x = -28; x <= 28; x += 3) Fx.Box(world, new(x, -0.035f, -6.9f), new(1.4f, 0.02f, 0.18f), white);

        Car(world, new(-9, 0, -5.7f), Fx.Hex("#FF9EBB"));
        Car(world, new(6.5f, 0, -8.1f), Fx.Hex("#8ED9C4"));
        Car(world, new(17, 0, -5.7f), Fx.Hex("#B9A2E8"));

        foreach (var x in new[] { 6f, 12f, 18f })
        {
            Lamp(world, new(x, 0, -3.9f));
            Lamp(world, new(-x, 0, -3.9f));
        }
        Bench(world, new(9, 0, -3.6f));
        Bench(world, new(-9, 0, -3.6f));

        // Flower boxes under the front windows.
        foreach (var x in new[] { -4.4f, 4.4f })
        {
            Fx.Box(world, new(x, 0.18f, -0.62f), new(4.2f, 0.36f, 0.5f), white);
            var flowerColors = new[] { pink, Fx.Hex("#FFD166"), Fx.Hex("#B388FF"), Fx.Hex("#FF6FB5") };
            for (int k = 0; k < 12; k++)
                Fx.Ball(world, new(x - 1.9f + k * 0.345f, 0.42f, -0.62f + (k % 2 - 0.5f) * 0.18f), 0.2f, flowerColors[k % flowerColors.Length]);
        }

        // The salon's lot: paved patio (future expansions land on it) framed by a flowering hedge.
        var patio = Fx.Box(world, new(0, -0.07f, 16.1f), new(16.8f, 0.1f, 33.4f), Color.white);
        patio.GetComponent<Renderer>().sharedMaterial = new Material(Fx.Mat(Color.white))
        {
            mainTexture = Room.Tiles(Fx.Hex("#F1E9F4"), Fx.Hex("#E2D6EA")),
            mainTextureScale = Vector2.one / 1.5f,
        };
        var hedge = Fx.Hex("#7BC67E");
        Fx.Box(world, new(-8.8f, 0.45f, 16.2f), new(0.8f, 0.9f, 33.6f), hedge);
        Fx.Box(world, new(8.8f, 0.45f, 16.2f), new(0.8f, 0.9f, 33.6f), hedge);
        Fx.Box(world, new(0, 0.45f, 33.2f), new(18.4f, 0.9f, 0.8f), hedge);
        var blossoms = Theme.Christmas ? new[] { Fx.Hex("#FF4D5E"), Color.white, Color.white } : new[] { pink, Fx.Hex("#FFD166"), Color.white };
        int bloom = 0;
        for (float z = 0.4f; z < 32.8f; z += 1.4f)
        {
            Fx.Ball(world, new(-8.8f, 0.93f, z), 0.18f, blossoms[bloom++ % 3]);
            Fx.Ball(world, new(8.8f, 0.93f, z), 0.18f, blossoms[bloom++ % 3]);
        }
        for (float x = -8; x <= 8; x += 1.4f) Fx.Ball(world, new(x, 0.93f, 33.2f), 0.18f, blossoms[bloom++ % 3]);
        for (float z = 3; z < 32; z += 4.5f)
        {
            Pot(world, new(-7.95f, 0, z));
            Pot(world, new(7.95f, 0, z));
        }

        // Trees in neat rows on the lawn and in sidewalk planters.
        foreach (var z in new[] { 1f, 7f, 13f, 19f, 25f, 31f })
        {
            Tree(world, new(-11.5f, 0, z));
            Tree(world, new(11.5f, 0, z));
        }
        for (float x = -10; x <= 10; x += 5) Tree(world, new(x, 0, 36.3f));
        foreach (var x in new[] { 12f, 18f })
        {
            Fx.Cyl(world, new(x, 0.08f, -1.6f), new(1.3f, 0.08f, 1.3f), Color.white);
            Fx.Cyl(world, new(-x, 0.08f, -1.6f), new(1.3f, 0.08f, 1.3f), Color.white);
            Tree(world, new(x, 0, -1.6f));
            Tree(world, new(-x, 0, -1.6f));
        }

        // Small clusters of wildflowers on the lawn (fixed seed, same meadow every launch).
        var rng = new System.Random(3);
        var wild = new[] { Color.white, Fx.Hex("#FFD166"), pink, Fx.Hex("#B388FF") };
        for (int n = 0; n < 45; n++)
        {
            float cx = (float)rng.NextDouble() * 50 - 25, cz = (float)rng.NextDouble() * 56 - 16;
            var color = wild[n % wild.Length];
            for (int k = 0; k < 3; k++)
            {
                float x = cx + (float)rng.NextDouble() * 0.8f, z = cz + (float)rng.NextDouble() * 0.8f;
                if (!OnLot(x, z)) Fx.Ball(world, new(x, 0.06f, z), 0.17f, color);
            }
        }
    }

    // Lot (salon + patio + hedge), sidewalk and road: no wildflowers there.
    static bool OnLot(float x, float z) => (Mathf.Abs(x) < 9.6f && z > -0.6f && z < 34f) || (z > -10f && z < -0.2f);

    static void Pot(Transform parent, Vector3 p)
    {
        Fx.Cyl(parent, p + new Vector3(0, 0.25f, 0), new(0.5f, 0.25f, 0.5f), Fx.Hex("#FFC7D9"), true);
        Fx.Ball(parent, p + new Vector3(0, 0.75f, 0), 0.7f, Fx.Hex("#8FD694"));
    }

    static void Tree(Transform parent, Vector3 p)
    {
        Fx.Cyl(parent, p + new Vector3(0, 0.6f, 0), new(0.28f, 0.6f, 0.28f), Fx.Hex("#C9A27E"));
        Fx.Ball(parent, p + new Vector3(0, 1.8f, 0), 1.6f, Fx.Hex("#7BC67E"));
        Fx.Ball(parent, p + new Vector3(0.45f, 2.3f, 0.2f), 1.1f, Fx.Hex("#8FD694"));
        Fx.Ball(parent, p + new Vector3(-0.4f, 2.2f, -0.25f), 0.9f, Fx.Hex("#6DBB72"));
        if (!Theme.Christmas) return;
        Fx.Ball(parent, p + new Vector3(0, 2.45f, 0), new Vector3(1.3f, 0.5f, 1.3f), Color.white); // snow caps
        Fx.Ball(parent, p + new Vector3(0.45f, 2.78f, 0.2f), new Vector3(0.85f, 0.32f, 0.85f), Color.white);
    }

    static void Lamp(Transform parent, Vector3 p)
    {
        Fx.Cyl(parent, p + new Vector3(0, 0.06f, 0), new(0.36f, 0.06f, 0.36f), Fx.Hex("#E9E1F0"));
        Fx.Cyl(parent, p + new Vector3(0, 1.4f, 0), new(0.1f, 1.4f, 0.1f), Color.white);
        Fx.Ball(parent, p + new Vector3(0, 2.9f, 0), 0.42f, Fx.Hex("#FFF3B0"));
    }

    static void Bench(Transform parent, Vector3 p)
    {
        var c = Fx.Hex("#FFC7D9");
        Fx.Box(parent, p + new Vector3(0, 0.45f, 0), new(1.6f, 0.12f, 0.5f), c);
        Fx.Box(parent, p + new Vector3(0, 0.78f, 0.24f), new(1.6f, 0.45f, 0.1f), c);
        Fx.Box(parent, p + new Vector3(-0.65f, 0.2f, 0), new(0.1f, 0.4f, 0.4f), Color.white);
        Fx.Box(parent, p + new Vector3(0.65f, 0.2f, 0), new(0.1f, 0.4f, 0.4f), Color.white);
    }

    static void Car(Transform parent, Vector3 p, Color body)
    {
        Fx.Box(parent, p + new Vector3(0, 0.5f, 0), new(2.3f, 0.55f, 1.15f), body);
        Fx.Box(parent, p + new Vector3(-0.1f, 0.98f, 0), new(1.25f, 0.5f, 1.02f), Color.Lerp(body, Color.white, 0.4f));
        Fx.Box(parent, p + new Vector3(-0.1f, 1.0f, 0), new(1.28f, 0.3f, 0.94f), Fx.Hex("#CFEFFF"));
        foreach (var x in new[] { -0.72f, 0.72f })
            foreach (var z in new[] { -0.55f, 0.55f })
                Fx.Cyl(parent, p + new Vector3(x, 0.24f, z), new(0.46f, 0.08f, 0.46f), Fx.Hex("#4A3F55")).transform.localRotation = Quaternion.Euler(90, 0, 0);
        Fx.Ball(parent, p + new Vector3(1.15f, 0.55f, -0.35f), 0.16f, Fx.Hex("#FFF3B0"));
        Fx.Ball(parent, p + new Vector3(1.15f, 0.55f, 0.35f), 0.16f, Fx.Hex("#FFF3B0"));
    }
}
