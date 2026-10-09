using UnityEngine;

// The beautician the player controls with the floating joystick.
public class Player : MonoBehaviour
{
    public float speed = 5f;
    public Carrier carrier;
    public Wallet wallet;
    public Walker walker;
    CharacterController cc;
    TextMesh maxLabel;

    public static Player Create(Vector3 pos)
    {
        var w = Fx.Person(pos, Fx.Hex("#FF5C9A"), Fx.Hex("#6B3E2E"), Fx.Hex("#FFDCC8"));
        Fx.Box(w.model, new(0, 0.45f, 0.21f), new(0.36f, 0.45f, 0.1f), Color.white); // apron
        var p = w.gameObject.AddComponent<Player>();
        p.walker = w;
        p.cc = w.gameObject.AddComponent<CharacterController>();
        p.cc.center = new(0, 0.8f, 0);
        p.cc.height = 1.6f;
        p.cc.radius = 0.35f;
        p.carrier = Carrier.Create(w, true);
        p.wallet = Wallet.Create(w);
        p.maxLabel = Fx.Text(w.transform, Vector3.zero, "MAX", 0.05f, Fx.Hex("#FF3B6B"), Game.CamRot);
        if (Theme.Christmas) SantaHat(w.model);
        return p;
    }

    // Tip flops sideways so it clears the products in front and the money tower on her back.
    static void SantaHat(Transform model)
    {
        var hat = new GameObject("Santa Hat").transform;
        hat.SetParent(model, false);
        hat.localPosition = new(0, 1.42f, -0.04f);
        Color red = Fx.Hex("#E8434F"), white = Color.white;
        Fx.Ball(hat, new(0, 0.16f, 0), new Vector3(0.68f, 0.5f, 0.68f), red);
        Fx.Ball(hat, new(0.1f, 0.38f, 0), new Vector3(0.38f, 0.38f, 0.38f), red);
        Fx.Ball(hat, new(0.24f, 0.5f, 0), 0.22f, red);
        Fx.Ball(hat, new(0.38f, 0.5f, 0), 0.18f, white); // pompom
        Fx.Cyl(hat, Vector3.zero, new(0.74f, 0.07f, 0.74f), white); // fur band
    }

    void Update()
    {
        var stick = Game.Autoplay ? Game.I.AutoStick() : Hud.I.Stick;
        var move = new Vector3(stick.x, 0, stick.y);
        cc.Move(move * (speed * Time.deltaTime));
        if (move.sqrMagnitude > 0.01f) walker.Face(move);

        bool full = carrier.Full;
        if (maxLabel.gameObject.activeSelf != full) maxLabel.gameObject.SetActive(full);
        if (full)
        {
            maxLabel.transform.position = carrier.Top + Vector3.up * 0.35f;
            maxLabel.transform.rotation = Game.CamRot;
        }
    }
}
