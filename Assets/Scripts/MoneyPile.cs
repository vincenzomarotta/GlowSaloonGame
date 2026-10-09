using System.Collections.Generic;
using UnityEngine;

// Stack of bills left by customers at a station.
public class MoneyPile : MonoBehaviour
{
    const int MaxBills = 32;

    readonly List<Transform> bills = new();
    readonly List<int> values = new();
    float timer;

    public int Value { get; private set; }

    public static MoneyPile Create(Transform parent, Vector3 local)
    {
        var go = new GameObject("Money");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        return go.AddComponent<MoneyPile>();
    }

    static Vector3 Slot(int i) => new((i % 2 - 0.5f) * 0.46f, (i / 4) * 0.07f + 0.03f, ((i / 2) % 2 - 0.5f) * 0.28f);

    public void Add(int amount, Vector3 from)
    {
        Value += amount;
        int n = Mathf.Clamp(amount / 4, 1, 6);
        for (int i = 0; i < n; i++)
        {
            int v = amount / n + (i == 0 ? amount % n : 0);
            if (bills.Count >= MaxBills) { values[^1] += v; continue; }
            var b = Fx.Bill(from);
            bills.Add(b);
            values.Add(v);
            Fx.Fly(b, transform, Slot(bills.Count - 1), 0.35f + i * 0.05f, 1.5f);
        }
    }

    public bool Take(out Transform bill, out int value)
    {
        bill = null;
        value = 0;
        if (bills.Count == 0) return false;
        bill = bills[^1];
        value = values[^1];
        bills.RemoveAt(bills.Count - 1);
        values.RemoveAt(values.Count - 1);
        Value -= value;
        return true;
    }

    // Walking close sweeps the bills onto the player's back; they count once banked at a register.
    void Update()
    {
        if (bills.Count == 0) return;
        var player = Game.I.player;
        if (!Fx.Near(player.transform.position, transform.position, 1.0f)) return;
        timer -= Time.deltaTime;
        while (timer <= 0 && Take(out var bill, out var value))
        {
            timer += 0.03f;
            player.wallet.Add(bill, value);
            Fx.Sfx("pop", 0.2f);
        }
    }
}
