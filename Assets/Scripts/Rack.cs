using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Shelf that restocks products over time and hands them to anyone standing on its pad.
public class Rack : MonoBehaviour
{
    const int Max = 8;
    const float RestockTime = 1.1f;

    public Kind kind;
    readonly List<Item> stock = new();
    float restockTimer, giveTimer;

    public Vector3 Pad => transform.TransformPoint(0, 0, 1.3f);

    public static Rack Create(Kind kind, Vector3 pos)
    {
        var r = new GameObject(kind + " Rack").AddComponent<Rack>();
        r.kind = kind;
        var t = r.transform;
        t.SetPositionAndRotation(pos, Quaternion.Euler(0, 180, 0)); // back wall, facing the room
        var col = r.gameObject.AddComponent<BoxCollider>();
        col.center = new(0, 1, -0.05f);
        col.size = new(1.8f, 2, 0.6f);

        Color wood = Fx.Hex("#F3E1D0"), tint = Game.Tint[(int)kind];
        Fx.Box(t, new(0, 1, -0.25f), new(1.7f, 2, 0.1f), wood);
        Fx.Box(t, new(-0.85f, 1, 0), new(0.06f, 2, 0.5f), wood);
        Fx.Box(t, new(0.85f, 1, 0), new(0.06f, 2, 0.5f), wood);
        foreach (var y in new[] { 0.05f, 0.65f, 1.25f, 1.97f })
            Fx.Box(t, new(0, y, 0), new(1.7f, 0.06f, 0.5f), Color.white);
        Fx.Box(t, new(0, 2.35f, -0.15f), new(1.5f, 0.45f, 0.1f), tint);
        Fx.Text(t, new(0, 2.35f, -0.04f), Game.ProductName[(int)kind], 0.04f, Color.white, Quaternion.identity);

        Fx.Cyl(t, new(0, 0.01f, 1.3f), new(1.3f, 0.01f, 1.3f), Color.white);
        Fx.Cyl(t, new(0, 0.02f, 1.3f), new(1.05f, 0.01f, 1.05f), tint);
        return r;
    }

    // Follows the back wall when the salon grows.
    public void MoveTo(Vector3 pos, bool animate)
    {
        if (animate) StartCoroutine(Slide(pos));
        else transform.position = pos;
    }

    IEnumerator Slide(Vector3 to)
    {
        var from = transform.position;
        for (float k = 0; k < 1; k += Time.deltaTime / 0.6f)
        {
            transform.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0, 1, k));
            yield return null;
        }
        transform.position = to;
    }

    static Vector3 Slot(int i) => new((i % 4 - 1.5f) * 0.38f, i < 4 ? 0.68f : 1.28f, 0.05f);

    void Update()
    {
        restockTimer += Time.deltaTime;
        if (stock.Count >= Max) restockTimer = 0;
        else if (restockTimer > RestockTime)
        {
            restockTimer = 0;
            var item = Fx.Product(kind, transform.TransformPoint(Slot(stock.Count)));
            item.transform.SetParent(transform, true);
            item.transform.localRotation = Quaternion.identity;
            stock.Add(item);
            Fx.Pop(item.transform);
        }

        giveTimer -= Time.deltaTime;
        if (giveTimer > 0 || stock.Count == 0) return;
        foreach (var c in Game.I.carriers)
        {
            if (c.Full || !Fx.Near(c.transform.position, Pad, 1.1f)) continue;
            var item = stock[^1];
            stock.RemoveAt(stock.Count - 1);
            c.Add(item);
            giveTimer = 0.12f;
            break;
        }
    }
}
