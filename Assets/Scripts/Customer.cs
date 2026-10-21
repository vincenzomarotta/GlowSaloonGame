using System;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

// Walks in, sits at a reserved station, waits for the service, pays and leaves.
// Special customers (VIP, bride, star) chain services against a deadline and only accept the player's hands.
// Regular customers lose patience while nobody works on them.
public class Customer : MonoBehaviour
{
    public enum Special { None, Vip, Bride, Star }

    const float VipSecondsPerService = 45f;
    static readonly Color Angry = Fx.Hex("#FF6B6B");

    public Special special;
    public Action<bool> onLeave; // friends in a group: true when served, false when she stormed off
    public bool Seated { get; private set; }
    public bool Vip => special != Special.None;
    public bool LastService => !Vip || step == plan.Length - 1;
    public float VipTimeLeft => deadline - Time.time;
    public Kind[] Plan => plan;
    public int Step => step;
    public string Title => special switch { Special.Bride => "Sposa", Special.Star => "Star", _ => "VIP" };
    public string Who => special switch { Special.Bride => "La sposa", Special.Star => "La star", _ => "La VIP" };

    Station station;
    Walker walker;
    Transform bubble;
    Renderer bubbleDisc;
    Kind[] plan;
    int step;
    float patience, maxPatience, deadline;
    bool leaving, waitingForSeat;

    public static Customer Spawn(Station s, Kind[] plan = null, Special special = Special.None, Color? friendOutfit = null)
    {
        // Arrives along the sidewalk from either side.
        var start = new Vector3(Random.value < 0.5f ? -11 : 11, 0, Game.Door.z + Random.Range(-0.4f, 0.4f));
        var outfit = special switch
        {
            Special.Vip => Fx.Hex("#FFD166"),
            Special.Bride => Color.white,
            Special.Star => Fx.Hex("#9B5DE5"),
            _ => friendOutfit ?? Fx.Outfit(),
        };
        var w = Fx.Person(start, outfit, Fx.NaturalHair(), Fx.Skin(), special == Special.None && Random.value < 0.6f);
        w.speed = special != Special.None ? 3.6f : 3.2f;
        var c = w.gameObject.AddComponent<Customer>();
        c.walker = w;
        c.special = special;
        c.plan = plan;
        switch (special)
        {
            case Special.Vip:
                c.deadline = Time.time + VipSecondsPerService * plan.Length + 10;
                Crown(w.model);
                break;
            case Special.Bride:
                c.deadline = Time.time + VipSecondsPerService * plan.Length + 15;
                Veil(w.model);
                break;
            case Special.Star:
                c.deadline = Time.time + 35;
                Sunglasses(w.model);
                break;
        }
        if (friendOutfit.HasValue) Bow(w.model);
        c.maxPatience = c.patience = (28 + 6 * (int)s.kind) * (Game.I.Has("sofa") ? 1.5f : 1);
        c.Reserve(s, new[] { Game.Door }.Concat(s.EntryPath()).ToArray());
        return c;
    }

    static void Crown(Transform model)
    {
        var gold = Fx.Hex("#F5C542");
        Fx.Cyl(model, new(0, 1.55f, -0.02f), new(0.36f, 0.06f, 0.36f), gold);
        for (int i = 0; i < 5; i++)
        {
            float a = i * Mathf.PI * 2 / 5;
            Fx.Ball(model, new(Mathf.Sin(a) * 0.16f, 1.66f, Mathf.Cos(a) * 0.16f - 0.02f), 0.09f, gold);
        }
        Fx.Ball(model, new(0, 1.66f, 0.17f), 0.08f, Fx.Hex("#FF4F8B"));
    }

    // Long white veil down her back and a little silver tiara.
    static void Veil(Transform model)
    {
        Fx.Ball(model, new(0, 1.1f, -0.3f), new Vector3(0.72f, 0.95f, 0.22f), Fx.Hex("#F4F1FA"));
        for (int i = -2; i <= 2; i++) Fx.Ball(model, new(i * 0.09f, 1.5f - Mathf.Abs(i) * 0.025f, 0.12f), 0.07f, Fx.Hex("#DDE3F0"));
        Fx.Ball(model, new(0, 1.53f, 0.14f), 0.08f, Fx.Hex("#9FD8F5"));
    }

    static void Sunglasses(Transform model)
    {
        var black = Fx.Hex("#2B2230");
        foreach (var x in new[] { -0.12f, 0.12f }) Fx.Ball(model, new(x, 1.16f, 0.31f), new Vector3(0.17f, 0.12f, 0.05f), black);
        Fx.Box(model, new(0, 1.18f, 0.32f), new(0.1f, 0.025f, 0.02f), black);
    }

    // Pink bow on top of the head: she came with her friends.
    static void Bow(Transform model)
    {
        var pink = Fx.Hex("#FF6FB5");
        Fx.Ball(model, new(-0.1f, 1.5f, 0.08f), new Vector3(0.16f, 0.12f, 0.08f), pink);
        Fx.Ball(model, new(0.1f, 1.5f, 0.08f), new Vector3(0.16f, 0.12f, 0.08f), pink);
        Fx.Ball(model, new(0, 1.5f, 0.1f), 0.07f, Fx.Hex("#FF4F8B"));
    }

    void Reserve(Station s, Vector3[] path)
    {
        station = s;
        s.customer = this;
        waitingForSeat = false;
        if (bubble) Destroy(bubble.gameObject);
        bubble = Fx.Bubble(transform, s.kind);
        bubbleDisc = bubble.GetComponentInChildren<Renderer>();
        bubble.gameObject.SetActive(false);
        walker.Go(path);
    }

    void Update()
    {
        if (leaving)
        {
            if (walker.Arrived) Destroy(gameObject);
            return;
        }
        if (Vip && Time.time > deadline) { StormOff(); return; }

        if (waitingForSeat)
        {
            var free = Game.I.stations.FirstOrDefault(s => s.kind == plan[step] && !s.customer);
            if (free) Reserve(free, new[] { transform.position }.Concat(free.EntryPath().Skip(1)).ToArray());
            return;
        }
        if (!walker.Arrived) return;

        if (!Seated)
        {
            Seated = true;
            transform.SetPositionAndRotation(station.SeatPos, station.SeatRot);
            walker.Pose(station.SeatY, station.SeatPose);
            return;
        }

        // Patience drains while nobody is working on her (special customers have their own clock).
        if (!Vip && Game.I.Impatience && !station.Working)
        {
            patience -= Time.deltaTime;
            if (bubbleDisc) bubbleDisc.sharedMaterial = Fx.Mat(Color.Lerp(Angry, Color.white, Mathf.Round(patience / maxPatience * 4) / 4));
            if (patience <= 0) StormOff();
        }
    }

    // Seat -> beside the furniture -> aisle (the station's entry path walked backwards, minus the door).
    Vector3[] ExitToAisle() => station.EntryPath().Reverse().Skip(1).Take(3).ToArray();

    void LateUpdate()
    {
        if (bubble && bubble.gameObject.activeSelf) bubble.rotation = Game.CamRot;
    }

    public void ShowBubble(bool on)
    {
        if (!bubble || bubble.gameObject.activeSelf == on) return;
        bubble.gameObject.SetActive(on);
        if (on) Fx.Pop(bubble);
    }

    void StandUp()
    {
        ShowBubble(false);
        Seated = false;
        walker.Pose(0, Quaternion.identity);
        if (station && station.customer == this) station.customer = null;
    }

    void LeaveSalon()
    {
        leaving = true;
        var back = waitingForSeat ? new[] { new Vector3(0, 0, 1f) } : station.EntryPath().Reverse().Skip(1);
        var away = new Vector3(Random.value < 0.5f ? -12 : 12, 0, Game.Door.z);
        walker.Go(back.Concat(new[] { Game.Door, away }).ToArray());
    }

    void Left(bool served)
    {
        onLeave?.Invoke(served);
        onLeave = null;
    }

    // Waited too long: leaves without paying.
    void StormOff()
    {
        StandUp();
        Fx.Burst(transform.position + Vector3.up * 1.6f, 10, Angry, Fx.Hex("#4A3F55"));
        Fx.FloatText(transform.position + Vector3.up * 2.4f, Vip ? Title + " persa!" : "Uffa!", Angry);
        if (Vip) Game.I.VipEnded(this, false);
        Game.I.CustomerLost();
        Left(false);
        special = Special.None;
        plan = null;
        LeaveSalon();
    }

    // Called by the station when a service is completed.
    public void Finish()
    {
        if (station.kind == Kind.Hair) walker.SetHair(Fx.FunHair()); // fresh hair color
        Fx.Burst(transform.position + Vector3.up * 1.2f, 14, Fx.Confetti);
        StandUp();
        if (Vip && step < plan.Length - 1)
        {
            step++;
            var next = Game.I.stations.FirstOrDefault(s => s.kind == plan[step] && !s.customer);
            var exit = ExitToAisle();
            if (next) Reserve(next, exit.Concat(next.EntryPath().Skip(1)).ToArray());
            else
            {
                // Wait in the aisle until a matching seat frees up.
                waitingForSeat = true;
                walker.Go(exit);
            }
            return;
        }
        if (Vip) Game.I.VipEnded(this, true);
        Left(true);
        LeaveSalon();
    }
}
