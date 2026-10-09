using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

// Primitive-based visuals, tweens and procedural sounds. The game ships with zero art assets.
public static class Fx
{
    static Material baseMat;
    static Font font;
    static readonly Dictionary<Color, Material> mats = new(), mottledMats = new();
    static Texture2D mottle;

    public static Font Font => font ??= Resources.Load<Font>("Fredoka");

    public static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

    // Luxury palette. Gold, marble and lacquer get a polished finish in Mat; everything else stays matte.
    public static readonly Color Gold = Hex("#E2BE55"), Marble = Hex("#F4F1EC"), Lacquer = Hex("#1E1B22"),
        Cream = Hex("#F3EBDD"), Champagne = Hex("#E8D5B5"), Blush = Hex("#D8A7A1"), Velvet = Hex("#2F5D50"),
        Wine = Hex("#7A2236"), Uniform = Hex("#2B2730");
    static readonly Dictionary<Color, (float gloss, float metal)> finishes = new()
    {
        [Gold] = (0.6f, 0.35f), // fully metallic went olive: it mirrors the grey default sky [Marble] = (0.75f, 0), [Lacquer] = (0.8f, 0),
    };

    public static Material Mat(Color c) => Mat(c, mats, null);

    // Rounded boxes (walls, furniture) get a faint mottling so big flat surfaces read as painted, not plastic.
    static Material Mottled(Color c) => Mat(c, mottledMats, mottle ??= Mottle());

    static Material Mat(Color c, Dictionary<Color, Material> cache, Texture tex)
    {
        if (cache.TryGetValue(c, out var m)) return m;
        baseMat ??= Resources.Load<Material>("Base");
        m = new Material(baseMat) { color = c };
        if (finishes.TryGetValue(c, out var f))
        {
            m.SetFloat("_Glossiness", f.gloss);
            m.SetFloat("_Metallic", f.metal);
        }
        if (tex)
        {
            m.mainTexture = tex;
            m.mainTextureScale = Vector2.one * 0.5f;
        }
        return cache[c] = m;
    }

    // Random 16x16 values, bilinear and tiling: soft blotches about 12 cm wide on world-unit UVs.
    static Texture2D Mottle()
    {
        const int n = 16;
        var tex = new Texture2D(n, n) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        var rng = new System.Random(7);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float v = 0.95f + 0.05f * (float)rng.NextDouble();
                tex.SetPixel(x, y, new Color(v, v, v));
            }
        tex.Apply();
        return tex;
    }

    public static bool Near(Vector3 a, Vector3 b, float radius)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return dx * dx + dz * dz < radius * radius;
    }

    // ---------- Primitives ----------

    public static GameObject Prim(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Color c, bool solid = false)
    {
        var go = GameObject.CreatePrimitive(type);
        if (!solid) UnityEngine.Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = Mat(c);
        return go;
    }

    // Soft rounded block. Its mesh is built at the requested size, so keep its localScale at 1
    // (pass round: false for boxes whose scale gets animated).
    public static GameObject Box(Transform p, Vector3 pos, Vector3 size, Color c, bool solid = false, bool round = true)
    {
        if (!round) return Prim(PrimitiveType.Cube, p, pos, size, c, solid);
        var go = new GameObject("Box");
        go.transform.SetParent(p, false);
        go.transform.localPosition = pos;
        go.AddComponent<MeshFilter>().sharedMesh = RoundedBox(size);
        go.AddComponent<MeshRenderer>().sharedMaterial = Mottled(c);
        if (solid) go.AddComponent<BoxCollider>().size = size;
        return go;
    }

    static readonly Dictionary<Vector3, Mesh> roundedMeshes = new();

    // Box whose edges are rounded with a per-axis radius, so thin plates still get rounded corners.
    // Each face is a grid dense near the edges; vertices are pushed onto an ellipsoid around the inner box.
    static Mesh RoundedBox(Vector3 size)
    {
        if (roundedMeshes.TryGetValue(size, out var cached)) return cached;
        const int seg = 3;
        const float maxRadius = 0.12f;
        var h = size / 2;
        var r = new Vector3(Radius(h.x), Radius(h.y), Radius(h.z));
        static float Radius(float half) => Mathf.Max(1e-4f, Mathf.Min(maxRadius, half * 0.9f));

        int m = 2 * (seg + 1);
        var coords = new float[3][];
        for (int a = 0; a < 3; a++)
        {
            coords[a] = new float[m];
            for (int k = 0; k <= seg; k++)
            {
                float off = h[a] - r[a] + r[a] * Mathf.Cos(k / (float)seg * Mathf.PI / 2);
                coords[a][k] = -off;
                coords[a][m - 1 - k] = off;
            }
        }

        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        var inner = h - r;
        for (int n = 0; n < 3; n++)
            for (int sign = -1; sign <= 1; sign += 2)
            {
                int u = (n + 1) % 3, v = (n + 2) % 3, start = verts.Count;
                for (int i = 0; i < m; i++)
                    for (int j = 0; j < m; j++)
                    {
                        var p = Vector3.zero;
                        p[n] = sign * h[n];
                        p[u] = coords[u][i];
                        p[v] = coords[v][j];
                        var core = new Vector3(Mathf.Clamp(p.x, -inner.x, inner.x), Mathf.Clamp(p.y, -inner.y, inner.y), Mathf.Clamp(p.z, -inner.z, inner.z));
                        var d = p - core;
                        d = new Vector3(d.x / r.x, d.y / r.y, d.z / r.z).normalized;
                        var pos = core + Vector3.Scale(d, r);
                        verts.Add(pos);
                        uvs.Add(new Vector2(pos[u], pos[v])); // world-unit UVs so textures tile evenly
                        normals.Add(new Vector3(d.x / r.x, d.y / r.y, d.z / r.z).normalized);
                    }
                // (i,j),(i,j+1),(i+1,j+1) faces -n, so flip the winding on the +n side.
                for (int i = 0; i < m - 1; i++)
                    for (int j = 0; j < m - 1; j++)
                    {
                        int a = start + i * m + j, b = a + 1, c = a + m + 1, e = a + m;
                        if (sign < 0) tris.AddRange(new[] { a, b, c, a, c, e });
                        else tris.AddRange(new[] { a, c, b, a, e, c });
                    }
            }

        var mesh = new Mesh { name = "RoundedBox" };
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return roundedMeshes[size] = mesh;
    }
    public static GameObject Ball(Transform p, Vector3 pos, Vector3 scale, Color c) => Prim(PrimitiveType.Sphere, p, pos, scale, c);
    public static GameObject Ball(Transform p, Vector3 pos, float size, Color c) => Prim(PrimitiveType.Sphere, p, pos, Vector3.one * size, c);
    // Unity cylinders are 2 units tall at scale 1, so scale.y is the half height.
    public static GameObject Cyl(Transform p, Vector3 pos, Vector3 scale, Color c, bool solid = false) => Prim(PrimitiveType.Cylinder, p, pos, scale, c, solid);
    public static GameObject Capsule(Transform p, Vector3 pos, Vector3 scale, Color c) => Prim(PrimitiveType.Capsule, p, pos, scale, c);

    public static TextMesh Text(Transform parent, Vector3 localPos, string s, float size, Color c, Quaternion worldRot)
    {
        var go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.rotation = worldRot;
        var t = go.AddComponent<TextMesh>();
        t.font = Font;
        t.text = s;
        t.fontSize = 64;
        t.characterSize = size;
        t.anchor = TextAnchor.MiddleCenter;
        t.alignment = TextAlignment.Center;
        t.color = c;
        go.GetComponent<MeshRenderer>().sharedMaterial = Font.material;
        return t;
    }

    // ---------- Palettes ----------

    static readonly Color[] outfits = { Hex("#F7A1C4"), Hex("#A0C4FF"), Hex("#B9FBC0"), Hex("#FFD6A5"), Hex("#CDB4DB"), Hex("#FFADAD"), Hex("#9BF6FF") };
    static readonly Color[] naturalHair = { Hex("#3B2219"), Hex("#6B4226"), Hex("#A0522D"), Hex("#E8C07D"), Hex("#1C1C1C"), Hex("#B5523B") };
    static readonly Color[] funHair = { Hex("#FF6FB5"), Hex("#B388FF"), Hex("#64D2FF"), Hex("#F5F5F5"), Hex("#FF8A65") };
    static readonly Color[] skins = { Hex("#FFDCC8"), Hex("#F1C27D"), Hex("#E0AC69"), Hex("#C68642"), Hex("#8D5524") };
    public static readonly Color[] Confetti = { Hex("#FF6FB5"), Hex("#FFD166"), Hex("#64D2FF"), Hex("#B388FF"), Hex("#7BE0AD") };

    static Color Pick(Color[] a) => a[Random.Range(0, a.Length)];
    public static Color Outfit() => Pick(outfits);
    public static Color NaturalHair() => Pick(naturalHair);
    public static Color FunHair() => Pick(funHair);
    public static Color Skin() => Pick(skins);

    // ---------- Models ----------

    // Chibi character: pivot at the feet, faces +z. All animation happens on the "Model" child.
    public static Walker Person(Vector3 pos, Color outfit, Color hair, Color skin, bool bun = true)
    {
        var root = new GameObject("Person").transform;
        root.position = pos;
        var model = new GameObject("Model").transform;
        model.SetParent(root, false);

        var body = Capsule(model, new(0, 0.45f, 0), new(0.62f, 0.42f, 0.52f), outfit).GetComponent<Renderer>();
        // Arms hang from a shoulder pivot so they can swing.
        var arms = new Transform[2];
        for (int i = 0; i < 2; i++)
        {
            arms[i] = new GameObject("Arm").transform;
            arms[i].SetParent(model, false);
            arms[i].localPosition = new(i == 0 ? -0.34f : 0.34f, 0.72f, 0);
            Capsule(arms[i], new(0, -0.17f, 0), new(0.15f, 0.2f, 0.15f), skin);
        }
        Ball(model, new(0, 1.15f, 0), 0.62f, skin);
        var hairs = new List<Renderer> { Ball(model, new(0, 1.24f, -0.1f), new Vector3(0.68f, 0.64f, 0.68f), hair).GetComponent<Renderer>() };
        if (bun) hairs.Add(Ball(model, new(0, 1.58f, -0.18f), 0.28f, hair).GetComponent<Renderer>());
        // Big glossy eyes with a highlight, soft blush and a tiny mouth.
        foreach (var x in new[] { -0.12f, 0.12f })
        {
            Ball(model, new(x, 1.15f, 0.29f), new Vector3(0.075f, 0.11f, 0.06f), Hex("#3A2E39"));
            Ball(model, new(x + 0.015f, 1.18f, 0.315f), 0.03f, Color.white);
            Ball(model, new(x * 1.6f, 1.05f, 0.25f), new Vector3(0.11f, 0.05f, 0.04f), Hex("#FFA3BE"));
        }
        Ball(model, new(0, 1.03f, 0.3f), new Vector3(0.07f, 0.03f, 0.03f), Hex("#E0607E"));

        var w = root.gameObject.AddComponent<Walker>();
        w.model = model;
        w.hair = hairs.ToArray();
        w.body = body;
        w.arms = arms;
        return w;
    }

    // Products have their pivot at the bottom so they stack by height.
    public static Item Product(Kind kind, Vector3 pos)
    {
        var go = new GameObject(kind + " Item");
        go.transform.position = pos;
        var t = go.transform;
        var item = go.AddComponent<Item>();
        item.kind = kind;
        switch (kind)
        {
            case Kind.Nails:
                Cyl(t, new(0, 0.1f, 0), new(0.2f, 0.1f, 0.2f), Hex("#FF4F8B"));
                Cyl(t, new(0, 0.26f, 0), new(0.08f, 0.06f, 0.08f), Hex("#2B2B2B"));
                item.height = 0.32f;
                break;
            case Kind.Hair:
                Box(t, new(0, 0.15f, 0), new(0.24f, 0.3f, 0.14f), Hex("#B388FF"));
                Cyl(t, new(0, 0.33f, 0), new(0.1f, 0.03f, 0.1f), Color.white);
                item.height = 0.36f;
                break;
            case Kind.Spa:
                Box(t, new(0, 0.05f, 0), new(0.36f, 0.1f, 0.26f), Hex("#7BE0AD"));
                Box(t, new(0, 0.05f, 0), new(0.37f, 0.104f, 0.06f), Color.white);
                item.height = 0.11f;
                break;
            case Kind.Makeup:
                Cyl(t, new(0, 0.08f, 0), new(0.12f, 0.08f, 0.12f), Hex("#E8B931"));
                Cyl(t, new(0, 0.22f, 0), new(0.09f, 0.07f, 0.09f), Hex("#D7263D"));
                item.height = 0.3f;
                break;
        }
        return item;
    }

    public static Transform Bill(Vector3 pos)
    {
        var t = new GameObject("Bill").transform;
        t.position = pos;
        Box(t, Vector3.zero, new(0.42f, 0.05f, 0.24f), Hex("#5BC27A"));
        Box(t, Vector3.zero, new(0.2f, 0.054f, 0.25f), Hex("#9BE3AE"));
        return t;
    }

    // White thought bubble with the wanted product inside; faces the camera.
    public static Transform Bubble(Transform parent, Kind kind)
    {
        var t = new GameObject("Bubble").transform;
        t.SetParent(parent, false);
        t.localPosition = new(0, 2.3f, 0);
        Cyl(t, Vector3.zero, new(0.75f, 0.02f, 0.75f), Color.white).transform.localRotation = Quaternion.Euler(90, 0, 0);
        var p = Product(kind, Vector3.zero).transform;
        p.SetParent(t, false);
        p.localPosition = new(0, -0.2f, -0.08f);
        p.localScale = Vector3.one * 1.2f;
        return t;
    }

    // ---------- Tweens ----------

    static readonly Dictionary<Transform, int> flights = new();
    static int flightId;

    // Arcs t into parent's local space; tracks moving parents. A newer Fly on the same transform cancels older ones.
    public static void Fly(Transform t, Transform parent, Vector3 to, float dur, float arc, Action done = null)
    {
        int id = ++flightId;
        flights[t] = id;
        Game.I.StartCoroutine(FlyRoutine(t, parent, to, dur, arc, id, done));
    }

    static IEnumerator FlyRoutine(Transform t, Transform parent, Vector3 to, float dur, float arc, int id, Action done)
    {
        t.SetParent(parent, true);
        Vector3 from = t.localPosition, s0 = t.localScale;
        Quaternion r0 = t.localRotation;
        for (float k = 0; k < 1; k += Time.deltaTime / dur)
        {
            if (!t) { flights.Remove(t); yield break; }
            if (!flights.TryGetValue(t, out var cur) || cur != id) yield break;
            t.localPosition = Vector3.Lerp(from, to, k) + Vector3.up * (arc * 4 * k * (1 - k));
            t.localRotation = Quaternion.Slerp(r0, Quaternion.identity, k);
            t.localScale = Vector3.Lerp(s0, Vector3.one, k);
            yield return null;
        }
        if (!t) { flights.Remove(t); yield break; }
        if (!flights.TryGetValue(t, out var last) || last != id) yield break;
        flights.Remove(t);
        t.localPosition = to;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;
        done?.Invoke();
    }

    public static void Pop(Transform t, float dur = 0.35f) => Game.I.StartCoroutine(PopRoutine(t, t.localScale, dur));

    static IEnumerator PopRoutine(Transform t, Vector3 target, float dur)
    {
        for (float k = 0; k < 1; k += Time.deltaTime / dur)
        {
            if (!t) yield break;
            float x = k - 1; // easeOutBack
            t.localScale = target * (1 + 2.70158f * x * x * x + 1.70158f * x * x);
            yield return null;
        }
        if (t) t.localScale = target;
    }

    public static void ShrinkAndDestroy(Transform t) => Game.I.StartCoroutine(ShrinkRoutine(t));

    static IEnumerator ShrinkRoutine(Transform t)
    {
        var s = t.localScale;
        for (float k = 0; k < 1; k += Time.deltaTime / 0.15f)
        {
            if (!t) yield break;
            t.localScale = s * (1 - k);
            yield return null;
        }
        if (t) UnityEngine.Object.Destroy(t.gameObject);
    }

    public static void Burst(Vector3 pos, int n, params Color[] colors) => Game.I.StartCoroutine(BurstRoutine(pos, n, colors));

    static IEnumerator BurstRoutine(Vector3 pos, int n, Color[] colors)
    {
        var parts = new Transform[n];
        var vel = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            parts[i] = Box(null, pos, Vector3.one * 0.14f, colors[i % colors.Length], round: false).transform;
            parts[i].rotation = Random.rotation;
            vel[i] = new Vector3(Random.Range(-2f, 2f), Random.Range(3f, 6f), Random.Range(-2f, 2f));
        }
        const float life = 0.8f;
        for (float k = 0; k < life; k += Time.deltaTime)
        {
            for (int i = 0; i < n; i++)
            {
                vel[i].y -= 12 * Time.deltaTime;
                parts[i].position += vel[i] * Time.deltaTime;
                parts[i].Rotate(400 * Time.deltaTime, 0, 0);
                parts[i].localScale = Vector3.one * 0.14f * (1 - k / life);
            }
            yield return null;
        }
        foreach (var p in parts) UnityEngine.Object.Destroy(p.gameObject);
    }

    // A few tiny glints drifting up: shows that someone is being worked on.
    public static void Sparkle(Vector3 pos, Color c) => Game.I.StartCoroutine(SparkleRoutine(pos, c));

    static IEnumerator SparkleRoutine(Vector3 pos, Color c)
    {
        var parts = new Transform[3];
        for (int i = 0; i < parts.Length; i++)
            parts[i] = Ball(null, pos + Random.insideUnitSphere * 0.35f, 0.09f, i == 0 ? Color.white : c).transform;
        for (float k = 0; k < 1; k += Time.deltaTime / 0.7f)
        {
            foreach (var p in parts)
            {
                p.position += Vector3.up * (0.9f * Time.deltaTime);
                p.localScale = Vector3.one * (0.09f * Mathf.Sin(Mathf.PI * Mathf.Min(1, k * 1.3f + 0.15f)));
            }
            yield return null;
        }
        foreach (var p in parts) UnityEngine.Object.Destroy(p.gameObject);
    }

    public static void FloatText(Vector3 pos, string s, Color c) => Game.I.StartCoroutine(FloatRoutine(Text(null, pos, s, 0.07f, c, Game.CamRot)));

    static IEnumerator FloatRoutine(TextMesh t)
    {
        var start = t.transform.position;
        var c = t.color;
        for (float k = 0; k < 1; k += Time.deltaTime / 0.9f)
        {
            t.transform.position = start + Vector3.up * (1.2f * k);
            t.color = new Color(c.r, c.g, c.b, 1 - k * k);
            yield return null;
        }
        UnityEngine.Object.Destroy(t.gameObject);
    }

    // ---------- Sound ----------

    static AudioSource audio;
    static readonly Dictionary<string, AudioClip> clips = new();
    static readonly Dictionary<string, float> lastPlay = new();

    public static void Sfx(string name, float volume = 0.5f)
    {
        if (!Settings.Sounds) return;
        if (lastPlay.TryGetValue(name, out var last) && Time.unscaledTime - last < 0.05f) return;
        lastPlay[name] = Time.unscaledTime;
        if (!audio) audio = Game.I.gameObject.AddComponent<AudioSource>(); // recreated after a salon reload
        if (!clips.TryGetValue(name, out var clip))
            clips[name] = clip = name switch
            {
                "pop" => Synth(name, 0.07f, 0.6f, 620),
                "coin" => Synth(name, 0.05f, 0, 1320, 1760),
                "done" => Synth(name, 0.09f, 0, 784, 1047, 1319),
                "unlock" => Synth(name, 0.1f, 0, 523, 659, 784, 1047),
                _ => Synth(name, 0.05f, 0, 440),
            };
        audio.PlayOneShot(clip, volume);
    }

    // Sine notes played in sequence, each with a decaying envelope and optional upward pitch sweep.
    static AudioClip Synth(string name, float noteLen, float sweep, params float[] freqs)
    {
        const int rate = 44100;
        int per = (int)(noteLen * rate);
        var data = new float[per * freqs.Length];
        for (int n = 0; n < freqs.Length; n++)
        {
            double phase = 0;
            for (int i = 0; i < per; i++)
            {
                float k = i / (float)per;
                phase += 2 * Math.PI * freqs[n] * (1 + sweep * k) / rate;
                float env = Mathf.Min(1, i / 150f) * (1 - k) * (1 - k);
                data[n * per + i] = (float)Math.Sin(phase) * env * 0.6f;
            }
        }
        var clip = AudioClip.Create(name, data.Length, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
