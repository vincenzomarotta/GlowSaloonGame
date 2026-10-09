using System.Collections.Generic;
using UnityEngine;

// Bills a character carries on the back until they are banked at a register.
public class Wallet : MonoBehaviour
{
    const int MaxVisible = 30;

    Transform stack;
    readonly List<Transform> bills = new();
    readonly List<int> values = new();

    public int Value { get; private set; }
    public int Count => bills.Count;

    public static Wallet Create(Walker w)
    {
        var wallet = w.gameObject.AddComponent<Wallet>();
        wallet.stack = new GameObject("Wallet").transform;
        wallet.stack.SetParent(w.model, false);
        wallet.stack.localPosition = new(0, 0.72f, -0.42f);
        return wallet;
    }

    static Vector3 Slot(int i) => new(0, i * 0.055f, 0);

    public void Add(Transform bill, int value)
    {
        Value += value;
        if (bills.Count >= MaxVisible)
        {
            // Tower is tall enough: fold the value into the top bill.
            values[^1] += value;
            Fx.Fly(bill, stack, Slot(bills.Count), 0.2f, 1f, () => Destroy(bill.gameObject));
            return;
        }
        bills.Add(bill);
        values.Add(value);
        Fx.Fly(bill, stack, Slot(bills.Count - 1), 0.2f, 1f);
    }

    public bool TakeTop(out Transform bill, out int value)
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
}
