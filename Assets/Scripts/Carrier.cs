using System.Collections.Generic;
using UnityEngine;

// The tower of products a character holds in front of them.
public class Carrier : MonoBehaviour
{
    public int capacity = 4;
    public bool isPlayer;
    public Walker walker;
    public Transform stack;
    public readonly List<Item> items = new();
    Vector3 lastPos;

    public bool Full => items.Count >= capacity;
    public Vector3 Top => stack.TransformPoint(0, Height(items.Count), 0);
    public bool Has(Kind k) => items.Exists(x => x.kind == k);

    public static Carrier Create(Walker w, bool isPlayer)
    {
        var c = w.gameObject.AddComponent<Carrier>();
        c.walker = w;
        c.isPlayer = isPlayer;
        c.stack = new GameObject("Stack").transform;
        c.stack.SetParent(w.model, false);
        c.stack.localPosition = new(0, 0.75f, 0.45f);
        c.lastPos = w.transform.position;
        return c;
    }

    float Height(int count)
    {
        float h = 0;
        for (int i = 0; i < count; i++) h += items[i].height;
        return h;
    }

    public void Add(Item item)
    {
        items.Add(item);
        Fx.Fly(item.transform, stack, new Vector3(0, Height(items.Count - 1), 0), 0.25f, 1f);
        if (isPlayer) Fx.Sfx("pop", 0.35f);
    }

    // Removes the topmost item of the given kind and lets the ones above it settle down.
    public Item Take(Kind kind)
    {
        int i = items.FindLastIndex(x => x.kind == kind);
        if (i < 0) return null;
        var item = items[i];
        items.RemoveAt(i);
        for (int j = i; j < items.Count; j++)
            Fx.Fly(items[j].transform, stack, new Vector3(0, Height(j), 0), 0.12f, 0);
        return item;
    }

    // The tower leans against the walking direction.
    void LateUpdate()
    {
        walker.holding = items.Count > 0;
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        var local = transform.InverseTransformDirection((transform.position - lastPos) / dt);
        lastPos = transform.position;
        var target = Quaternion.Euler(Mathf.Clamp(-local.z * 2.5f, -12, 12), 0, 0);
        stack.localRotation = Quaternion.Slerp(stack.localRotation, target, 1 - Mathf.Exp(-8 * dt));
    }
}
