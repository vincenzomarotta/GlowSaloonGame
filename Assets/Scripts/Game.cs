using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

public enum Kind { Nails, Hair, Spa, Makeup }

// Bootstraps the whole salon at runtime (no scene content needed): world, unlock tree, customers, save.
public class Game : MonoBehaviour
{
    public const string SalonName = "Nenni's Saloon"; // app name

    // Each new salon (prestige) gets a name, a lawn color and one more x of income.
    static readonly string[] Lawns = { "#A9DFC0", "#F3E3B5", "#C9D7F2", "#F7C9D9" };
    public static string[] ServiceName => Theme.Service;
    public static readonly int[] UpgradeBase = { 300, 600, 1000, 1400 };

    public static Game I;
    public static readonly Quaternion CamRot = Quaternion.Euler(55, 0, 0);
    public static readonly Vector3 Door = new(0, 0, -2.5f);

    // Per-kind tuning, indexed by (int)Kind.
    public static string[] ProductName => Theme.Product;
    public static readonly int[] Price = { 6, 14, 22, 32 };
    public static readonly float[] ServiceTime = { 1.6f, 2.2f, 2.8f, 2.4f };
    public static readonly Color[] Tint = { Fx.Hex("#FF4F8B"), Fx.Hex("#B388FF"), Fx.Hex("#4CC99A"), Fx.Hex("#E8A33D") };

    // Test aid: launch with -autoplay and the player walks to whatever the guide arrow points at.
    public static readonly bool Autoplay = Environment.GetCommandLineArgs().Contains("-autoplay");

    const int Choices = 3;          // unlock tiles offered at the same time
    const float Markup = 0.25f;     // picking one makes every other offered tile 25% (of base) pricier...
    const int MaxMarkups = 4;       // ...up to double its base price

    static readonly float[] RowZ = { 4.5f, 8.1f, 11.7f, 15.3f };
    static readonly float[] RackX = { -1.5f, 1.5f, -4.5f, 4.5f };
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
    float vipTimer = 60;

    class Step
    {
        public string id, label;
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

    static float Side(Kind k) => k is Kind.Nails or Kind.Spa ? -1 : 1;
    static float Row(Kind k, int i) => RowZ[(k is Kind.Spa or Kind.Makeup ? 2 : 0) + i];
    static Vector3 PadPos(Kind k, int i) => new(3.6f * Side(k), 0, Row(k, i));
    Vector3 RackPos(Kind k) => new(RackX[(int)k], 0, room.zMax - 0.9f);
    Vector3 RackPad(Kind k) => RackPos(k) - new Vector3(0, 0, 1.3f);

    void AddStation(Kind k, int i, bool pop)
    {
        // Piles pair up between rows 0-1 and 2-3 (client, cash, cash, client); a lone top row keeps its cash behind.
        var s = Station.Create(k, new Vector3(5 * Side(k), 0, Row(k, i)), -90 * Side(k), i % 2 == 0);
        stations.Add(s);
        stationAt[(k, i)] = s;
        s.id = k + "" + i;
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
        Step Add(string id, string label, int cost, Func<Vector3> pos, Action<bool> build, Step[] needs)
        {
            var s = new Step { id = id, label = label, baseCost = cost, pos = pos, build = build, needs = needs };
            steps.Add(s);
            return s;
        }
        Step Seat(string id, string label, int cost, Kind k, int i, params Step[] needs) =>
            Add(id, label, cost, () => PadPos(k, i), pop => AddStation(k, i, pop), needs);
        Step Shelf(string id, Kind k, int cost, params Step[] needs) =>
            Add(id, "Scaffale " + ProductName[(int)k], cost, () => RackPad(k), pop => AddRack(k, pop), needs);
        Step Hire(string id, string label, int cost, Kind k, int i, params Step[] needs) =>
            Add(id, "Assumi " + label, cost, null, pop => stationAt[(k, i)].Hire(pop), needs);
        Step Upgrade(string id, string label, int cost, Action apply, params Step[] needs) =>
            Add(id, label, cost, null, _ => apply(), needs);
        Step Clerk(string id, int cost, params Step[] needs) =>
            Add(id, "Assumi Magazziniera", cost, null, pop => Runner.Create(new Vector3(0, 0, 6.3f), pop), needs);
        Step Grow(string id, int cost, float xMax, float zMax, params Step[] needs) =>
            Add(id, "Ingrandisci salone", cost, null, pop => Expand(xMax, zMax, pop), needs);
        Step Till(string id, int cost, int i, params Step[] needs) =>
            Add(id, "Cassa", cost, () => new Vector3(RegisterX[i], 0, 1.2f), pop => AddRegister(i, pop), needs);
        Step Cashier(string id, int cost, int i, params Step[] needs) =>
            Add(id, "Assumi Cassiera", cost, null, pop => registers[i].Hire(pop), needs);

        var nails1 = Seat("nails1", Theme.Seat[(int)Kind.Nails], 20, Kind.Nails, 1);
        var bag1 = Upgrade("bag1", "Borsa più grande", 60, () => player.carrier.capacity += 2);
        Cashier("cashier0", 150, 0);
        Hire("nailStaff0", Theme.Staff[(int)Kind.Nails], 120, Kind.Nails, 0);
        var wide = Grow("wide", 150, 7, 12);
        var hairShelf = Shelf("hairShelf", Kind.Hair, 60, wide);
        var hair0 = Seat("hair0", Theme.Seat[(int)Kind.Hair], 90, Kind.Hair, 0, hairShelf);
        Upgrade("speed", "Scarpe veloci", 250, () => player.speed += 1.5f, bag1);
        Hire("nailStaff1", Theme.Staff[(int)Kind.Nails], 220, Kind.Nails, 1, nails1);
        var till1 = Till("till1", 250, 1, nails1);
        var hair1 = Seat("hair1", Theme.Seat[(int)Kind.Hair], 200, Kind.Hair, 1, hair0);
        Cashier("cashier1", 600, 1, till1);
        Hire("hairStaff0", Theme.Staff[(int)Kind.Hair], 380, Kind.Hair, 0, hair0);
        var deep = Grow("deep", 600, 7, 16.5f, wide);
        var spaShelf = Shelf("spaShelf", Kind.Spa, 350, deep);
        var spa0 = Seat("spa0", Theme.Seat[(int)Kind.Spa], 450, Kind.Spa, 0, spaShelf);
        var clerk1 = Clerk("clerk1", 900, hairShelf);
        Upgrade("bag2", "Borsa XL", 700, () => player.carrier.capacity += 3, bag1);
        Hire("hairStaff1", Theme.Staff[(int)Kind.Hair], 750, Kind.Hair, 1, hair1);
        var makeupShelf = Shelf("makeupShelf", Kind.Makeup, 800, deep);
        var makeup0 = Seat("makeup0", Theme.Seat[(int)Kind.Makeup], 1000, Kind.Makeup, 0, makeupShelf);
        Hire("spaStaff0", Theme.Staff[(int)Kind.Spa], 1200, Kind.Spa, 0, spa0);
        var till2 = Till("till2", 1200, 2, till1);
        Cashier("cashier2", 1600, 2, till2);
        var full = Grow("full", 1800, 7, 20.5f, deep);
        var spa1 = Seat("spa1", Theme.Seat[(int)Kind.Spa], 1500, Kind.Spa, 1, full, spa0);
        var makeup1 = Seat("makeup1", Theme.Seat[(int)Kind.Makeup], 2000, Kind.Makeup, 1, full, makeup0);
        Hire("makeupStaff0", Theme.Staff[(int)Kind.Makeup], 2200, Kind.Makeup, 0, makeup0);
        Hire("spaStaff1", Theme.Staff[(int)Kind.Spa], 2500, Kind.Spa, 1, spa1);
        Hire("makeupStaff1", Theme.Staff[(int)Kind.Makeup], 2800, Kind.Makeup, 1, makeup1);
        Clerk("clerk2", 3000, clerk1);
    }

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
            var z = UnlockZone.Create(s.label, Cost(s), paid.GetValueOrDefault(s.id), want[s.id], () => Purchased(step));
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
        var z = UnlockZone.Create("Apri " + Theme.NextTitle.Replace("Nenni's ", ""), PrestigeCost, paid.GetValueOrDefault("prestige"), Slots()[0], AskPrestige);
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

    // ---------- Cosmetics ----------

    public void ApplyCosmetics()
    {
        player.walker.SetOutfit(Cosmetics.Equipped(Cosmetics.Outfits).a);
        player.walker.SetHair(Cosmetics.Equipped(Cosmetics.Hair).a);
        room.Restyle();
    }

    // ---------- VIP ----------

    public int VipReward(Kind[] plan) => 5 * plan.Sum(k => Game.Price[(int)k]) * Multiplier;

    void SpawnVip()
    {
        vipTimer -= Time.deltaTime;
        if (vip || vipTimer > 0 || !VipAvailable) return;
        var kinds = stations.Select(s => s.kind).Distinct().OrderBy(_ => Random.value).Take(Random.Range(2, 4)).ToArray();
        var first = stations.FirstOrDefault(s => s.kind == kinds[0] && !s.customer);
        if (!first) { vipTimer = 3; return; }
        vip = Customer.Spawn(first, kinds);
        Hud.I.Toast("Arriva una cliente VIP!", 2.5f);
        Fx.Sfx("unlock", 0.5f);
    }

    public void VipEnded(bool happy)
    {
        vip = null;
        vipTimer = Random.Range(70f, 110f);
        Hud.I.Toast(happy ? "VIP soddisfatta!" : "La VIP se n'è andata...", 2.5f);
    }

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
        spawnTimer = Random.Range(1.2f, 3f) / Mathf.Sqrt(stations.Count);
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
        // Cool ambient plus a warm, lower sun that sum to about 1 on top faces: pastels keep their color
        // instead of blowing out to white, and the sides fall into shade for depth.
        RenderSettings.ambientSkyColor = new Color(0.46f, 0.45f, 0.56f);
        RenderSettings.ambientEquatorColor = new Color(0.42f, 0.4f, 0.48f);
        RenderSettings.ambientGroundColor = new Color(0.3f, 0.27f, 0.33f);

        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 0.65f;
        sun.color = new Color(1f, 0.92f, 0.82f);
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.6f;
        sun.transform.rotation = Quaternion.Euler(50, -30, 0);

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
        var patio = Fx.Box(world, new(0, -0.07f, 10.7f), new(16.8f, 0.1f, 22.6f), Color.white);
        patio.GetComponent<Renderer>().sharedMaterial = new Material(Fx.Mat(Color.white))
        {
            mainTexture = Room.Tiles(Fx.Hex("#F1E9F4"), Fx.Hex("#E2D6EA")),
            mainTextureScale = Vector2.one / 1.5f,
        };
        var hedge = Fx.Hex("#7BC67E");
        Fx.Box(world, new(-8.8f, 0.45f, 10.8f), new(0.8f, 0.9f, 22.8f), hedge);
        Fx.Box(world, new(8.8f, 0.45f, 10.8f), new(0.8f, 0.9f, 22.8f), hedge);
        Fx.Box(world, new(0, 0.45f, 22.4f), new(18.4f, 0.9f, 0.8f), hedge);
        var blossoms = Theme.Christmas ? new[] { Fx.Hex("#FF4D5E"), Color.white, Color.white } : new[] { pink, Fx.Hex("#FFD166"), Color.white };
        int bloom = 0;
        for (float z = 0.4f; z < 22; z += 1.4f)
        {
            Fx.Ball(world, new(-8.8f, 0.93f, z), 0.18f, blossoms[bloom++ % 3]);
            Fx.Ball(world, new(8.8f, 0.93f, z), 0.18f, blossoms[bloom++ % 3]);
        }
        for (float x = -8; x <= 8; x += 1.4f) Fx.Ball(world, new(x, 0.93f, 22.4f), 0.18f, blossoms[bloom++ % 3]);
        for (float z = 3; z < 21; z += 4.5f)
        {
            Pot(world, new(-7.95f, 0, z));
            Pot(world, new(7.95f, 0, z));
        }

        // Trees in neat rows on the lawn and in sidewalk planters.
        foreach (var z in new[] { 1f, 7f, 13f, 19f })
        {
            Tree(world, new(-11.5f, 0, z));
            Tree(world, new(11.5f, 0, z));
        }
        for (float x = -10; x <= 10; x += 5) Tree(world, new(x, 0, 25.5f));
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
            float cx = (float)rng.NextDouble() * 50 - 25, cz = (float)rng.NextDouble() * 44 - 16;
            var color = wild[n % wild.Length];
            for (int k = 0; k < 3; k++)
            {
                float x = cx + (float)rng.NextDouble() * 0.8f, z = cz + (float)rng.NextDouble() * 0.8f;
                if (!OnLot(x, z)) Fx.Ball(world, new(x, 0.06f, z), 0.17f, color);
            }
        }
    }

    // Lot (salon + patio + hedge), sidewalk and road: no wildflowers there.
    static bool OnLot(float x, float z) => (Mathf.Abs(x) < 9.6f && z > -0.6f && z < 23.2f) || (z > -10f && z < -0.2f);

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
