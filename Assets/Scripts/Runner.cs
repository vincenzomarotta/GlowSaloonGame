using System.Collections.Generic;
using UnityEngine;

// Hired stock clerk: carries products from racks to the emptiest station trays.
public class Runner : MonoBehaviour
{
    enum State { Idle, ToRack, Loading, ToStation, Unloading }

    static readonly List<Runner> all = new();

    Walker walker;
    Carrier carrier;
    Station target;
    State state;
    float wait;

    public static void Create(Vector3 pos, bool pop)
    {
        var w = Fx.Person(pos, Fx.Hex("#B9A2E8"), Fx.NaturalHair(), Fx.Skin(), false);
        Fx.Box(w.model, new(0, 1.42f, 0.05f), new(0.5f, 0.12f, 0.5f), Fx.Hex("#FF6FB5")); // cap
        var r = w.gameObject.AddComponent<Runner>();
        r.walker = w;
        r.carrier = Carrier.Create(w, false);
        r.Train();
        Game.I.carriers.Add(r.carrier);
        all.Add(r);
        if (pop) Fx.Pop(w.transform);
    }

    // Each training level (shop, "Personale"): +20% speed and 2 more products.
    void Train()
    {
        int lv = Game.I.ClerkLevel;
        walker.speed = 3.6f * (1 + 0.2f * (lv - 1));
        carrier.capacity = 5 + 2 * (lv - 1);
    }

    public static void Retrain() => all.ForEach(r => r.Train());
    public static int Count => all.Count;

    void OnDestroy() => all.Remove(this);

    // Through the central aisle, but crossing the back of the salon 1.4 m short of the shelves' pads:
    // walking over the middle (brows) shelf's pad loaded the wrong products and left the clerk stuck with them.
    void Route(Vector3 dest)
    {
        var p = transform.position;
        float lane = Game.I.room.zMax - 3.6f, z0 = Mathf.Min(p.z, lane), z1 = Mathf.Min(dest.z, lane);
        walker.Go(new(p.x, 0, z0), new(0, 0, z0), new(0, 0, z1), new(dest.x, 0, z1), dest);
    }

    bool ClaimedByOther(Station s) => all.Exists(r => r != this && r.target == s);

    // Emptiest station that this runner can actually supply.
    Station PickStation(Kind? only)
    {
        Station best = null;
        foreach (var s in Game.I.stations)
        {
            if (only.HasValue ? s.kind != only.Value : !Game.I.racks.ContainsKey(s.kind)) continue;
            if (s.tray.Count >= (only.HasValue ? Station.TrayMax : 6) || ClaimedByOther(s)) continue;
            if (!best || s.tray.Count < best.tray.Count) best = s;
        }
        return best;
    }

    void Update()
    {
        wait += Time.deltaTime;
        switch (state)
        {
            case State.Idle:
                if (wait < 0.5f) return;
                wait = 0;
                // Leftovers first, otherwise restock the emptiest tray.
                Kind? carried = carrier.items.Count > 0 ? carrier.items[0].kind : null;
                target = PickStation(carried);
                if (!target) return;
                if (carried.HasValue) { Route(target.Pad); state = State.ToStation; }
                else { Route(Game.I.racks[target.kind].Pad); state = State.ToRack; }
                break;
            case State.ToRack:
                if (walker.Arrived) { state = State.Loading; wait = 0; }
                break;
            case State.Loading:
                if (carrier.Full || (wait > 4 && carrier.Has(target.kind))) { Route(target.Pad); state = State.ToStation; }
                else if (wait > 6) state = State.Idle; // rack moved or stayed empty
                break;
            case State.ToStation:
                if (walker.Arrived) { state = State.Unloading; wait = 0; }
                break;
            case State.Unloading:
                if (!carrier.Has(target.kind) || wait > 3) { state = State.Idle; target = null; wait = 0; }
                break;
        }
    }
}
