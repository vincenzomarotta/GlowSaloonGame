using UnityEngine;

// Cash desk: the player banks carried bills on its pad. A hired cashier rings up part of
// every payment directly (see Station.Complete).
public class Register : MonoBehaviour
{
    Transform machine;
    Walker cashier;
    float timer;

    public Vector3 Pad => transform.TransformPoint(0, 0, 1.2f);

    public static Register Create(Vector3 pos)
    {
        var r = new GameObject("Register").AddComponent<Register>();
        var t = r.transform;
        t.position = pos;

        Fx.Box(t, new(0, 0.45f, 0), new(1.5f, 0.9f, 0.7f), Fx.Marble, solid: true);
        Fx.Box(t, new(0, 0.93f, 0), new(1.6f, 0.07f, 0.8f), Fx.Gold);
        r.machine = Fx.Box(t, new(0.3f, 1.12f, 0), new(0.55f, 0.3f, 0.42f), Fx.Lacquer).transform;
        Fx.Box(r.machine, new(0, 0.2f, -0.1f), new(0.42f, 0.16f, 0.06f), Fx.Hex("#A8E6CF"));
        Fx.Box(t, new(-0.35f, 1.0f, 0.05f), new(0.42f, 0.06f, 0.26f), Fx.Hex("#5BC27A"));

        Fx.Cyl(t, new(0, 0.01f, 1.2f), new(1.3f, 0.01f, 1.3f), Fx.Gold);
        Fx.Cyl(t, new(0, 0.02f, 1.2f), new(1.05f, 0.01f, 1.05f), Fx.Cream);
        return r;
    }

    public void Hire(bool pop)
    {
        cashier = Fx.Person(transform.TransformPoint(0, 0, -0.65f), Fx.Uniform, Fx.NaturalHair(), Fx.Skin());
        Fx.Box(cashier.model, new(0, 0.45f, 0.21f), new(0.36f, 0.45f, 0.1f), Color.white); // apron
        if (pop)
        {
            Fx.Pop(cashier.transform);
            Fx.Burst(cashier.transform.position + Vector3.up, 16, Fx.Confetti);
        }
    }

    void Update()
    {
        timer -= Time.deltaTime;
        if (timer > 0) return;
        var player = Game.I.player;
        if (player.wallet.Count > 0 && Fx.Near(player.transform.position, Pad, 1.0f) && player.wallet.TakeTop(out var bill, out var value))
        {
            Bank(bill, value, 0.25f);
            Haptics.Tick();
            timer = 0.03f;
        }
    }

    public bool HasCashier => cashier;

    // The cashier's share of a payment flies straight from the station into the till.
    public void Receive(int amount, Vector3 from)
    {
        Bank(Fx.Bill(from), amount, 0.6f);
        cashier.Work(0.6f);
    }

    void Bank(Transform bill, int value, float dur) =>
        Fx.Fly(bill, machine, Vector3.up * 0.25f, dur, 2f, () =>
        {
            Game.I.AddCash(value);
            Fx.Sfx("coin", 0.3f);
            Destroy(bill.gameObject);
        });
}
