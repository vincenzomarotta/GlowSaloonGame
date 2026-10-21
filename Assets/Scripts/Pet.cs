using UnityEngine;

// Hope, the little orange dog with white spots on the neck and nose (shop, "Animali").
// Trots after the player, sits and wags its tail when she stops. No physics: it walks through furniture.
public class Pet : MonoBehaviour
{
    Transform model, tail;
    readonly Transform[] legs = new Transform[4];
    float phase, wag;

    public static Pet Create(Vector3 pos, bool pop)
    {
        var p = new GameObject("Hope").AddComponent<Pet>();
        p.transform.position = pos;
        p.Build();
        if (pop)
        {
            Fx.Pop(p.transform);
            Fx.Burst(pos + Vector3.up * 0.6f, 20, Fx.Hex("#FF6FB5"), Fx.Hex("#F28C28"), Color.white);
            Fx.FloatText(pos + Vector3.up * 1.2f, "Ciao Hope!", Fx.Hex("#FF4F8B"));
        }
        return p;
    }

    // Pivot at the feet, facing +z, about 0.7 m tall.
    void Build()
    {
        Color orange = Fx.Hex("#F28C28"), dark = Fx.Hex("#D9701A"), white = Color.white, ink = Fx.Hex("#3A2E39");
        model = new GameObject("Model").transform;
        model.SetParent(transform, false);
        model.localScale = Vector3.one * 1.2f;

        Fx.Ball(model, new(0, 0.32f, -0.02f), new Vector3(0.34f, 0.3f, 0.56f), orange);       // body
        Fx.Ball(model, new(0, 0.4f, 0.2f), new Vector3(0.24f, 0.26f, 0.18f), white);          // white neck patch
        Fx.Cyl(model, new(0, 0.47f, 0.2f), new(0.26f, 0.025f, 0.22f), Fx.Hex("#FF6FB5"));     // collar
        Fx.Ball(model, new(0, 0.4f, 0.31f), 0.06f, Fx.Hex("#F5C542"));                       // tag

        var head = new GameObject("Head").transform;
        head.SetParent(model, false);
        head.localPosition = new(0, 0.6f, 0.28f);
        Fx.Ball(head, Vector3.zero, 0.34f, orange);
        Fx.Ball(head, new(0, -0.05f, 0.15f), new Vector3(0.19f, 0.14f, 0.18f), white);       // white muzzle
        Fx.Ball(head, new(0, 0.06f, 0.14f), new Vector3(0.07f, 0.15f, 0.06f), white);        // white blaze up the nose
        Fx.Ball(head, new(0, -0.02f, 0.245f), 0.065f, ink);                                  // nose
        foreach (var x in new[] { -0.08f, 0.08f })
        {
            Fx.Ball(head, new(x, 0.05f, 0.14f), new Vector3(0.05f, 0.065f, 0.04f), ink);
            Fx.Ball(head, new(x + 0.01f, 0.07f, 0.16f), 0.018f, white);
        }
        foreach (var x in new[] { -0.15f, 0.15f })                                          // floppy ears
            Fx.Ball(head, new(x, 0.06f, -0.03f), new Vector3(0.1f, 0.19f, 0.09f), dark).transform.localRotation = Quaternion.Euler(0, 0, x > 0 ? 18 : -18);

        for (int i = 0; i < 4; i++)
        {
            legs[i] = new GameObject("Leg").transform;
            legs[i].SetParent(model, false);
            legs[i].localPosition = new(i % 2 == 0 ? -0.1f : 0.1f, 0.18f, i < 2 ? 0.16f : -0.17f);
            Fx.Cyl(legs[i], new(0, -0.09f, 0), new(0.09f, 0.09f, 0.09f), orange);
        }
        tail = new GameObject("Tail").transform;
        tail.SetParent(model, false);
        tail.localPosition = new(0, 0.42f, -0.28f);
        Fx.Ball(tail, new(0, 0.09f, -0.03f), new Vector3(0.07f, 0.2f, 0.07f), orange).transform.localRotation = Quaternion.Euler(-25, 0, 0);
    }

    void Update()
    {
        var player = Game.I.player;
        var p = player.transform;
        float dt = Time.deltaTime;
        // A step behind and to the side, so it never hides the products in front or the money on the back.
        var target = p.position - p.forward * 1.1f + p.right * 0.7f;
        var to = target - transform.position;
        to.y = 0;
        float dist = to.magnitude;
        if (dist > 10) { transform.position = target; dist = 0; } // left behind (e.g. a new salon): catch up
        bool walking = dist > 0.3f;
        if (walking)
        {
            float speed = Mathf.Min(dist * 4, player.speed + 1.5f);
            transform.position += to / dist * Mathf.Min(speed * dt, dist);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), 10 * dt);
        }
        else
        {
            var look = p.position - transform.position;
            look.y = 0;
            if (look.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look), 4 * dt);
        }

        // Trot: diagonal legs swing together, a little bounce. Idle: sits up, tail wags faster.
        phase = walking ? phase + dt * 16 : 0;
        float swing = Mathf.Sin(phase) * 35;
        for (int i = 0; i < 4; i++) legs[i].localRotation = Quaternion.Euler(i == 0 || i == 3 ? swing : -swing, 0, 0);
        float blend = 1 - Mathf.Exp(-12 * dt);
        model.localPosition = Vector3.Lerp(model.localPosition, new(0, walking ? Mathf.Abs(Mathf.Sin(phase)) * 0.05f : -0.04f, 0), blend);
        model.localRotation = Quaternion.Slerp(model.localRotation, Quaternion.Euler(walking ? 0 : -18, 0, 0), blend);
        wag += dt * (walking ? 10 : 18);
        tail.localRotation = Quaternion.Euler(0, Mathf.Sin(wag) * 35, 0);
    }
}
