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
        var w = Fx.Person(pos, Fx.Champagne, Fx.NaturalHair(), Fx.Skin(), false);
        Fx.Box(w.model, new(0, 1.42f, 0.05f), new(0.5f, 0.12f, 0.5f), Fx.Wine); // cap
        w.speed = 3.6f;
        var r = w.gameObject.AddComponent<Runner>();
        r.walker = w;
        r.carrier = Carrier.Create(w, false);
        r.carrier.capacity = 5;
        Game.I.carriers.Add(r.carrier);
        all.Add(r);
        if (pop) Fx.Pop(w.transform);
    }

    void OnDestroy() => all.Remove(this);

    void Route(Vector3 dest) => walker.GoViaAisle(dest);

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
