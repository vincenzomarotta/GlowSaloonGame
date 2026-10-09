using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// A service spot: customer seat, supply tray, money pile and the pad where the worker stands.
// Local +z points to the aisle; the seat is against the wall.
public class Station : MonoBehaviour
{
    public const int TrayMax = 10;

    public const int MaxLevel = 5;

    public Kind kind;
    public string id;
    public int level = 1;
    public Customer customer;
    public readonly List<Item> tray = new();

    Walker staff;
    MoneyPile pile;
    Transform trayRoot, bar, barFill, alert;
    float progress, dropTimer, sparkleTimer;
    float mirror = 1; // -1 swaps the pile/cart side with the customers' entry side
    Transform stars;

    public Vector3 Pad => transform.TransformPoint(0, 0, 1.4f);
    public bool HasStaff => staff;
    public bool CustomerSeated => customer && customer.Seated;
    public bool NeedsWorker => CustomerSeated && tray.Count > 0 && (!staff || customer.Vip);
    public bool Working { get; private set; }

    // Each level above 1: +30% price and +20% speed. The salon multiplier scales every payment.
    public int ServicePrice => Mathf.RoundToInt(Game.Price[(int)kind] * (1 + 0.3f * (level - 1))) * Game.I.Multiplier;
    float ServiceSeconds => Game.ServiceTime[(int)kind] / (1 + 0.2f * (level - 1));
    public int UpgradeCost => Game.UpgradeBase[(int)kind] << (level - 1);

    // Gold studs on the rug show the level.
    public void SetLevel(int lv, bool pop)
    {
        level = lv;
        if (stars) Destroy(stars.gameObject);
        stars = new GameObject("Stars").transform;
        stars.SetParent(transform, false);
        for (int i = 0; i < level - 1; i++)
            Fx.Ball(stars, new(1.35f * mirror, 0.08f, -1.3f + 0.3f * i), 0.17f, Fx.Gold);
        if (pop)
        {
            Fx.Burst(Pad + Vector3.up, 20, Fx.Hex("#F5C542"), Color.white);
            Fx.Pop(transform);
        }
    }
    public int PileValue => pile.Value;
    public MoneyPile Pile => pile;

    // Hair faces the mirror, massage lies on the bed, the others sit facing the worker.
    bool Lies => kind == Kind.Spa;
    Vector3 SeatLocal => Lies ? new Vector3(-0.75f, 0, -0.85f) : new Vector3(0, 0, -0.95f);
    public Vector3 SeatPos => transform.TransformPoint(SeatLocal);
    public Quaternion SeatRot => transform.rotation * Quaternion.Euler(0, kind switch { Kind.Hair => 180, Kind.Spa => 90, _ => 0 }, 0);
    public float SeatY => Lies ? 0.85f : 0.2f;
    public Quaternion SeatPose => Lies ? Quaternion.Euler(90, 0, 0) : Quaternion.identity;

    // Door -> aisle -> beside the furniture -> seat.
    public Vector3[] EntryPath()
    {
        var side = transform.TransformPoint(1.5f * mirror, 0, 2.4f);
        return new[]
        {
            new Vector3(0, 0, 1f), new Vector3(0, 0, side.z), side,
            transform.TransformPoint(1.5f * mirror, 0, SeatLocal.z), SeatPos,
        };
    }

    // pileBack: money pile on the side away from the entrance (world +z), else towards it.
    public static Station Create(Kind kind, Vector3 pos, float yaw, bool pileBack)
    {
        var s = new GameObject(kind + " Station").AddComponent<Station>();
        s.kind = kind;
        var t = s.transform;
        t.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        s.mirror = t.TransformDirection(Vector3.left).z > 0 == pileBack ? 1 : -1;
        float m = s.mirror;
        var col = s.gameObject.AddComponent<BoxCollider>();
        col.center = new(0, 0.5f, -0.75f);
        col.size = new(2.4f, 1f, 1.5f);

        // Color-coded rug marks the area of each service.
        Fx.Box(t, new(0, 0.012f, 0.25f), new(3f, 0.02f, 3.4f), Color.Lerp(Game.Tint[(int)kind], Color.white, 0.62f));
        s.BuildFurniture();

        // Worker pad.
        Fx.Cyl(t, new(0, 0.03f, 1.4f), new(1.3f, 0.01f, 1.3f), Fx.Gold);
        Fx.Cyl(t, new(0, 0.04f, 1.4f), new(1.05f, 0.01f, 1.05f), Fx.Cream);

        // Supply cart.
        Fx.Cyl(t, new(-0.95f * m, 0.36f, 0.55f), new(0.08f, 0.36f, 0.08f), Fx.Gold);
        Fx.Box(t, new(-0.95f * m, 0.72f, 0.55f), new(0.7f, 0.06f, 0.6f), Fx.Lacquer, solid: true);
        s.trayRoot = new GameObject("Tray").transform;
        s.trayRoot.SetParent(t, false);
        s.trayRoot.localPosition = new(-0.95f * m, 0.75f, 0.55f);

        // "!" over the cart while a customer waits for products that aren't there.
        s.alert = Fx.Text(t, new(-0.95f * m, 1.6f, 0.55f), "!", 0.14f, Fx.Hex("#FF3B6B"), Game.CamRot).transform;
        s.alert.gameObject.SetActive(false);

        // Beside the customer's seat, away from their entry side and ~2.5 m from the pad,
        // so working the station doesn't sweep it: the player walks over to pick the money up.
        // 1.55 keeps two facing piles apart in the 0.6 m gap between neighbouring rugs.
        s.pile = MoneyPile.Create(t, new(-1.55f * m, 0, -0.5f));

        s.bar = new GameObject("Bar").transform;
        s.bar.SetParent(t, false);
        s.bar.position = s.SeatPos + Vector3.up * 2.4f;
        s.bar.rotation = Game.CamRot;
        Fx.Box(s.bar, Vector3.zero, new(1.1f, 0.2f, 0.02f), Fx.Hex("#4A3F55"));
        s.barFill = Fx.Box(s.bar, new(0, 0, -0.02f), new(1f, 0.13f, 0.02f), Fx.Hex("#7BE0AD"), round: false).transform;
        s.bar.gameObject.SetActive(false);
        return s;
    }

    void BuildFurniture()
    {
        var t = transform;
        Color white = Fx.Marble, pink = Fx.Blush, lilac = Fx.Velvet, gold = Fx.Gold, grey = Fx.Gold;
        switch (kind)
        {
            case Kind.Nails:
                Fx.Box(t, new(0, 0.32f, -0.95f), new(0.65f, 0.12f, 0.6f), pink);
                Fx.Box(t, new(0, 0.7f, -1.25f), new(0.65f, 0.7f, 0.12f), pink);
                Fx.Box(t, new(0, 0.36f, 0.05f), new(0.25f, 0.72f, 0.25f), white);
                Fx.Box(t, new(0, 0.74f, 0.05f), new(1.3f, 0.07f, 0.65f), white);
                Fx.Cyl(t, new(0.45f, 0.95f, 0.05f), new(0.04f, 0.2f, 0.04f), grey);
                Fx.Ball(t, new(0.45f, 1.18f, 0.05f), 0.2f, Fx.Hex("#FFF3B0"));
                break;
            case Kind.Hair:
                Fx.Cyl(t, new(0, 0.18f, -0.85f), new(0.35f, 0.18f, 0.35f), grey);
                Fx.Box(t, new(0, 0.42f, -0.85f), new(0.75f, 0.14f, 0.7f), lilac);
                Fx.Box(t, new(0, 0.85f, -0.48f), new(0.75f, 0.85f, 0.14f), lilac);
                Fx.Box(t, new(0, 1.5f, -1.65f), new(1.1f, 1.4f, 0.08f), gold);
                Fx.Box(t, new(0, 1.5f, -1.6f), new(0.92f, 1.22f, 0.08f), Fx.Hex("#CFEFFF"));
                break;
            case Kind.Spa:
                Fx.Box(t, new(0, 0.3f, -0.85f), new(2f, 0.6f, 0.85f), Fx.Champagne);
                Fx.Box(t, new(0, 0.62f, -0.85f), new(2f, 0.08f, 0.85f), Fx.Cream);
                Fx.Box(t, new(0.6f, 0.7f, -0.85f), new(0.45f, 0.1f, 0.6f), Color.white);
                Fx.Cyl(t, new(-0.9f, 1.1f, -1.6f), new(0.3f, 0.02f, 0.3f), Fx.Hex("#FFE3A3"));
                break;
            case Kind.Makeup:
                Fx.Box(t, new(0, 0.32f, -0.95f), new(0.65f, 0.12f, 0.6f), Fx.Wine);
                Fx.Box(t, new(0, 0.7f, -1.25f), new(0.65f, 0.7f, 0.12f), Fx.Wine);
                Fx.Box(t, new(0, 1.5f, -1.65f), new(1.2f, 1.1f, 0.08f), gold);
                Fx.Box(t, new(0, 1.5f, -1.6f), new(1.0f, 0.9f, 0.08f), Fx.Hex("#CFEFFF"));
                for (int i = 0; i < 4; i++)
                {
                    float y = 1.1f + i * 0.27f;
                    Fx.Ball(t, new(-0.62f, y, -1.58f), 0.12f, Fx.Hex("#FFF3B0"));
                    Fx.Ball(t, new(0.62f, y, -1.58f), 0.12f, Fx.Hex("#FFF3B0"));
                }
                break;
        }
    }

    public void Hire(bool pop)
    {
        staff = Fx.Person(Pad, Fx.Uniform, Fx.NaturalHair(), Fx.Skin());
        staff.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(SeatPos - Pad, Vector3.up));
        Fx.Box(staff.model, new(0, 0.45f, 0.21f), new(0.36f, 0.45f, 0.1f), Color.white); // apron
        if (pop)
        {
            Fx.Pop(staff.transform);
            Fx.Burst(Pad + Vector3.up, 16, Fx.Confetti);
        }
    }

    void Update()
    {
        dropTimer -= Time.deltaTime;
        bool playerHere = false;
        foreach (var c in Game.I.carriers)
        {
            if (!Fx.Near(c.transform.position, Pad, 1.1f)) continue;
            if (c.isPlayer) playerHere = true;
            if (dropTimer <= 0 && tray.Count < TrayMax && c.Take(kind) is { } item)
            {
                AddToTray(item);
                dropTimer = 0.08f;
            }
        }

        if (!customer) progress = 0;
        // VIPs only accept the player's hands.
        bool staffMay = staff && !(customer && customer.Vip);
        Working = CustomerSeated && tray.Count > 0 && (playerHere || staffMay);
        if (Working)
        {
            if (staffMay) staff.Work();
            else Game.I.player.walker.Work();
            progress += Time.deltaTime / ServiceSeconds;
            sparkleTimer -= Time.deltaTime;
            if (sparkleTimer <= 0)
            {
                sparkleTimer = 0.35f;
                Fx.Sparkle(customer.transform.position + Vector3.up * (SeatY + 0.9f), Game.Tint[(int)kind]);
            }
            if (progress >= 1) Complete();
        }

        bool needStock = CustomerSeated && tray.Count == 0;
        if (alert.gameObject.activeSelf != needStock) alert.gameObject.SetActive(needStock);
        if (needStock) alert.localPosition = new(-0.95f * mirror, 1.6f + Mathf.Abs(Mathf.Sin(Time.time * 5)) * 0.25f, 0.55f);

        bool showBar = CustomerSeated && progress > 0;
        if (bar.gameObject.activeSelf != showBar) bar.gameObject.SetActive(showBar);
        barFill.localScale = new(progress, 0.13f, 0.02f);
        barFill.localPosition = new((progress - 1) / 2, 0, -0.02f);
        if (customer) customer.ShowBubble(CustomerSeated && progress <= 0);
    }

    void AddToTray(Item item)
    {
        int i = tray.Count;
        tray.Add(item);
        var slot = new Vector3((i % 2 - 0.5f) * 0.3f, (i / 4) * item.height, ((i / 2) % 2 - 0.5f) * 0.26f);
        Fx.Fly(item.transform, trayRoot, slot, 0.25f, 1f);
        Fx.Sfx("pop", 0.35f);
    }

    void Complete()
    {
        progress = 0;
        var used = tray[^1];
        tray.RemoveAt(tray.Count - 1);
        Fx.ShrinkAndDestroy(used.transform);

        // A VIP pays everything at the end of her last service.
        int price = !customer.Vip ? ServicePrice : customer.LastService ? Game.I.VipReward(customer.Plan) : 0;
        Missions.Served(kind, customer.Vip && customer.LastService);
        if (price > 0)
        {
            // Each hired cashier rings up 25% of the payment herself (max 75%); the rest lands on the floor.
            var tills = Game.I.registers.Where(r => r.HasCashier).ToList();
            int rung = price * tills.Count / 4;
            if (rung > 0) tills[Random.Range(0, tills.Count)].Receive(rung, customer.transform.position + Vector3.up);
            pile.Add(price - rung, customer.transform.position + Vector3.up);
            Fx.FloatText(SeatPos + Vector3.up * 2.6f, "+" + Game.Money(price) + " €", Fx.Hex("#3FA65E"));
        }
        Fx.Sfx("done");
        var served = customer;
        customer = null;
        served.Finish();
    }
}
