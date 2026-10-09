using System;
using UnityEngine;

// Floor tile with a price: stand on it and your cash drains into it until the thing gets built.
public class UnlockZone : MonoBehaviour
{
    const float Size = 1.8f;
    const float FillSize = Size - 0.16f;

    public int cost, paid;
    string label;
    Action onDone;
    Transform fill;
    TextMesh priceText;
    float stay, timer;
    int tick;

    public int Remaining => Mathf.Max(0, cost - paid);

    public static UnlockZone Create(string label, int cost, int paid, Vector3 pos, Action onDone)
    {
        var z = new GameObject("Zone " + label).AddComponent<UnlockZone>();
        z.label = label;
        z.cost = cost;
        z.paid = paid;
        z.onDone = onDone;
        var t = z.transform;
        t.position = pos;

        Fx.Box(t, new(0, 0.025f, 0), new(Size + 0.14f, 0.03f, Size + 0.14f), Color.white);
        Fx.Box(t, new(0, 0.04f, 0), new(Size, 0.03f, Size), Fx.Hex("#9C88D9"));
        z.fill = Fx.Box(t, Vector3.zero, Vector3.one, Fx.Hex("#7BE0AD"), round: false).transform;

        var flat = Quaternion.Euler(90, 0, 0);
        // Long labels go on two lines so they stay big and inside the tile.
        int space = label.IndexOf(' ');
        var text = label.Length > 12 && space > 0 ? label.Remove(space, 1).Insert(space, "\n") : label;
        Fx.Text(t, new(0, 0.08f, 0.32f), text, 0.038f, Color.white, flat).lineSpacing = 0.9f;
        z.priceText = Fx.Text(t, new(0, 0.08f, -0.48f), "", 0.065f, Color.white, flat);
        z.Refresh();
        return z;
    }

    // The price went up because another option was picked.
    public void SetCost(int newCost)
    {
        if (newCost == cost) return;
        cost = newCost;
        Refresh();
        Fx.Pop(transform);
        Fx.FloatText(transform.position + Vector3.up * 0.8f, "+25%", Fx.Hex("#FF4F8B"));
    }

    void Refresh()
    {
        float k = Mathf.Clamp01((float)paid / cost);
        fill.gameObject.SetActive(k > 0);
        fill.localScale = new(FillSize, 0.032f, FillSize * k);
        fill.localPosition = new(0, 0.045f, -FillSize / 2 + FillSize * k / 2);
        priceText.text = Game.Money(Remaining) + " €";
    }

    void Update()
    {
        var game = Game.I;
        var d = game.player.transform.position - transform.position;
        bool inside = Mathf.Abs(d.x) < Size / 2 && Mathf.Abs(d.z) < Size / 2;
        stay = inside ? stay + Time.deltaTime : 0;
        if (stay < 0.4f) { timer = 0; return; } // brief pause so walking across a tile does not spend

        timer -= Time.deltaTime;
        while (timer <= 0 && game.cash > 0 && paid < cost)
        {
            timer += 0.03f;
            int chunk = Mathf.Min(Mathf.Max(1, cost / 50), Remaining, game.cash);
            game.AddCash(-chunk);
            paid += chunk;
            if ((tick++ & 1) == 0)
            {
                var b = Fx.Bill(game.player.transform.position + Vector3.up * 1.4f);
                Fx.Fly(b, transform, Vector3.up * 0.1f, 0.25f, 1.5f, () => Destroy(b.gameObject));
                Fx.Sfx("coin", 0.2f);
            }
            Refresh();
        }
        if (paid >= cost) Done();
    }

    void Done()
    {
        Fx.Sfx("unlock", 0.7f);
        Haptics.Tick(40, 160);
        Fx.Burst(transform.position + Vector3.up * 0.5f, 24, Fx.Confetti);
        Hud.I.Toast(label + "!");
        Destroy(gameObject);
        enabled = false;
        onDone();
    }
}
