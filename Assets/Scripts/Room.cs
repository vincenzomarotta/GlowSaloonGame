using UnityEngine;

// Salon shell (floor, walls, sign, plants). The left wall and the entrance are fixed;
// buying expansions pushes the right wall (xMax) and the back wall (zMax) outwards.
public class Room : MonoBehaviour
{
    public const float XMin = -7f;
    public float xMax = 3f, zMax = 12f;

    Transform shell;
    Material floorMat;

    public static Room Create()
    {
        var r = new GameObject("Room").AddComponent<Room>();
        r.Build(false);
        return r;
    }

    // Rebuild with the current shop colors and decorations.
    public void Restyle() => Build(false);

    public void Resize(float x, float z, bool animate)
    {
        xMax = x;
        zMax = z;
        Build(animate);
    }

    void Build(bool animate)
    {
        if (shell) Destroy(shell.gameObject);
        shell = new GameObject("Shell").transform;
        shell.SetParent(transform, false);

        float w = xMax - XMin, cx = (xMax + XMin) / 2;
        var paint = Cosmetics.Equipped(Cosmetics.Walls).a;
        var tiles = Cosmetics.Equipped(Cosmetics.Floors);
        Color back = paint, wall = Color.Lerp(paint, Color.white, 0.65f), trim = Fx.Gold;

        // Floor runs under the walls and the walls sink below it, so no seam shows at the base.
        var floor = Fx.Box(shell, new(cx, -0.05f, zMax / 2), new(w + 0.6f, 0.1f, zMax + 0.6f), Color.white);
        if (floorMat) Destroy(floorMat.mainTexture);
        floorMat ??= new Material(Fx.Mat(Color.white)) { mainTextureScale = Vector2.one * 0.5f };
        floorMat.SetFloat("_Glossiness", 0.45f); // polished
        floorMat.mainTexture = tiles.name.StartsWith("Marmo") ? Tiles(tiles.a, tiles.b) : Planks(tiles.a, tiles.b);
        floor.GetComponent<Renderer>().sharedMaterial = floorMat;

        var walls = new[]
        {
            Fx.Box(shell, new(XMin - 0.15f, 0.475f, zMax / 2), new(0.3f, 1.45f, zMax + 0.3f), wall, true),
            Fx.Box(shell, new(xMax + 0.15f, 0.475f, zMax / 2), new(0.3f, 1.45f, zMax + 0.3f), wall, true),
            Fx.Box(shell, new(cx, 1.475f, zMax + 0.15f), new(w + 0.6f, 3.45f, 0.3f), back, true),
            FrontWall(XMin - 0.3f, -1.2f, wall),
            FrontWall(1.2f, xMax + 0.3f, wall),
        };
        Fx.Box(shell, new(XMin - 0.15f, 1.24f, zMax / 2), new(0.36f, 0.1f, zMax + 0.3f), trim);
        Fx.Box(shell, new(xMax + 0.15f, 1.24f, zMax / 2), new(0.36f, 0.1f, zMax + 0.3f), trim);
        Fx.Box(shell, new((XMin - 1.5f) / 2, 1.24f, -0.15f), new(-1.2f - XMin + 0.3f, 0.1f, 0.36f), trim);
        Fx.Box(shell, new((xMax + 1.5f) / 2, 1.24f, -0.15f), new(xMax + 0.3f - 1.2f, 0.1f, 0.36f), trim);
        Fx.Box(shell, new(cx, 3.24f, zMax + 0.15f), new(w + 0.6f, 0.1f, 0.36f), trim);

        // Sign on the back wall.
        Fx.Box(shell, new(cx, 2.6f, zMax - 0.02f), new(5.6f, 0.85f, 0.12f), Fx.Lacquer);
        Fx.Text(shell, new(cx, 2.6f, zMax - 0.1f), Game.I.SalonTitle, 0.09f, Fx.Gold, Quaternion.identity);

        // Entrance rug; customers have no physics, so only the player is stopped at the door.
        Fx.Box(shell, new(0, 0.012f, 1f), new(2.4f, 0.02f, 1.4f), Fx.Wine);
        var doorBlock = new GameObject("DoorBlock").AddComponent<BoxCollider>();
        doorBlock.transform.SetParent(shell, false);
        doorBlock.transform.localPosition = new(0, 0.6f, -0.2f);
        doorBlock.size = new(2.4f, 1.2f, 0.4f);

        if (Theme.Christmas) ChristmasTree(new(1.95f, 0, 0.9f)); // right of the door (always in view), replaces the corner plant
        else Plant(new(xMax - 0.6f, 0, 0.8f));
        Plant(new(XMin + 0.6f, 0, zMax - 0.6f));
        Plant(new(xMax - 0.6f, 0, zMax - 0.6f));
        BuildDecor(cx);

        if (!animate) return;
        foreach (var wl in walls) Fx.Pop(wl.transform, 0.5f);
        Fx.Burst(new Vector3(xMax, 1.5f, zMax / 2), 30, Fx.Confetti);
        Fx.Burst(new Vector3(cx, 2f, zMax), 30, Fx.Confetti);
    }

    // Optional decorations bought in the shop.
    void BuildDecor(float cx)
    {
        if (Cosmetics.DecorOn("d0") || Theme.Christmas) // string lights along the wall tops
        {
            var warm = Theme.Christmas
                ? new[] { Fx.Hex("#FF4D5E"), Fx.Hex("#5CC46A"), Fx.Hex("#FFD166") }
                : new[] { Fx.Hex("#FFE08A"), Fx.Hex("#FF9EBB"), Fx.Hex("#B9A2E8") };
            int n = 0;
            for (float z = 0.3f; z < zMax; z += 0.7f)
            {
                Fx.Ball(shell, new(XMin - 0.15f, 1.36f, z), 0.16f, warm[n++ % 3]);
                Fx.Ball(shell, new(xMax + 0.15f, 1.36f, z), 0.16f, warm[n++ % 3]);
            }
            for (float x = XMin; x < xMax; x += 0.7f) Fx.Ball(shell, new(x, 3.36f, zMax + 0.15f), 0.16f, warm[n++ % 3]);
        }
        if (Cosmetics.DecorOn("d1")) // balloon bunches by the entrance
            foreach (var x in new[] { -1.6f, 1.6f })
            {
                var colors = new[] { Fx.Hex("#FF8FB8"), Fx.Hex("#B9A2E8"), Fx.Hex("#FFD166") };
                for (int i = 0; i < 3; i++)
                {
                    var top = new Vector3(x + (i - 1) * 0.28f, 2.1f + (i % 2) * 0.3f, 0.35f);
                    Fx.Cyl(shell, new(top.x, (top.y + 0.6f) / 2, top.z), new(0.02f, (top.y - 0.6f) / 2, 0.02f), Color.white); // string
                    Fx.Ball(shell, top, new Vector3(0.42f, 0.5f, 0.42f), colors[i]);
                }
            }
        if (Cosmetics.DecorOn("d2")) // hearts beside the sign
            foreach (var x in new[] { cx - 3.5f, cx + 3.5f })
            {
                var heart = new GameObject("Heart").transform;
                heart.SetParent(shell, false);
                heart.localPosition = new(x, 2.5f, zMax - 0.05f);
                var red = Fx.Hex("#FF4F8B");
                Fx.Ball(heart, new(-0.16f, 0.1f, 0), new Vector3(0.4f, 0.4f, 0.12f), red);
                Fx.Ball(heart, new(0.16f, 0.1f, 0), new Vector3(0.4f, 0.4f, 0.12f), red);
                Fx.Box(heart, new(0, -0.08f, 0), new(0.4f, 0.4f, 0.12f), red).transform.localRotation = Quaternion.Euler(0, 0, 45);
            }
        if (Cosmetics.DecorOn("d3")) // succulents in tiny pots along the front and side wall tops
        {
            var pots = new[] { Fx.Hex("#FFC7D9"), Fx.Hex("#FFFFFF"), Fx.Hex("#B9A2E8") };
            int n = 0;
            void Pot(Vector3 p)
            {
                Fx.Cyl(shell, p + new Vector3(0, 0.1f, 0), new(0.22f, 0.1f, 0.22f), pots[n++ % 3]);
                Fx.Ball(shell, p + new Vector3(0, 0.26f, 0), new Vector3(0.26f, 0.2f, 0.26f), Fx.Hex("#7BC67E"));
                Fx.Ball(shell, p + new Vector3(0.05f, 0.36f, 0), 0.08f, Fx.Hex("#FF9EBB"));
            }
            for (float x = XMin + 0.6f; x < -1.5f; x += 1.6f) Pot(new(x, 1.29f, -0.15f));
            for (float x = 2f; x < xMax; x += 1.6f) Pot(new(x, 1.29f, -0.15f));
            for (float z = 2f; z < zMax - 1; z += 3.2f)
            {
                Pot(new(XMin - 0.15f, 1.29f, z));
                Pot(new(xMax + 0.15f, 1.29f, z));
            }
        }
    }

    GameObject FrontWall(float x0, float x1, Color c) =>
        Fx.Box(shell, new((x0 + x1) / 2, 0.475f, -0.15f), new(x1 - x0, 1.45f, 0.3f), c, true);

    void Plant(Vector3 pos)
    {
        Fx.Cyl(shell, pos + new Vector3(0, 0.3f, 0), new(0.6f, 0.3f, 0.6f), Fx.Lacquer, true);
        Fx.Ball(shell, pos + new Vector3(0, 0.95f, 0), 0.9f, Fx.Hex("#7BC67E"));
        Fx.Ball(shell, pos + new Vector3(0.2f, 1.35f, 0.1f), 0.55f, Fx.Hex("#8FD694"));
    }

    // Fluffy tiered fir with baubles and a gold star; solid pot like the plants.
    void ChristmasTree(Vector3 pos)
    {
        Fx.Cyl(shell, pos + new Vector3(0, 0.25f, 0), new(0.6f, 0.25f, 0.6f), Fx.Hex("#E8434F"), true);
        Fx.Cyl(shell, pos + new Vector3(0, 0.52f, 0), new(0.64f, 0.03f, 0.64f), Color.white);
        Color[] greens = { Fx.Hex("#3FA65E"), Fx.Hex("#4DB86A"), Fx.Hex("#5CC478"), Fx.Hex("#6CCF86") };
        Color[] baubles = { Fx.Hex("#FF4D5E"), Fx.Hex("#FFD166"), Fx.Hex("#FF9EBB"), Fx.Hex("#7FC8FF") };
        for (int i = 0; i < 4; i++)
        {
            float y = 0.85f + i * 0.42f, r = 1.35f - i * 0.3f;
            Fx.Ball(shell, pos + new Vector3(0, y, 0), new Vector3(r, 0.62f, r), greens[i]);
            for (int k = 0; k < 5; k++)
            {
                float a = (k * 72 + i * 36) * Mathf.Deg2Rad;
                Fx.Ball(shell, pos + new Vector3(Mathf.Cos(a) * r * 0.45f, y - 0.12f, Mathf.Sin(a) * r * 0.45f), 0.14f, baubles[(k + i) % 4]);
            }
        }
        var gold = Fx.Hex("#FFC93C");
        Fx.Box(shell, pos + new Vector3(0, 2.52f, 0), new(0.3f, 0.3f, 0.08f), gold).transform.localRotation = Quaternion.Euler(0, 0, 45);
        Fx.Ball(shell, pos + new Vector3(0, 2.52f, 0), 0.2f, Color.white);
    }

    // Parquet: 8 staggered planks per texture (25 cm each at the floor's UV scale), shaded between a and b.
    static Texture2D Planks(Color a, Color b)
    {
        const int n = 64, h = n / 8;
        var tex = new Texture2D(n, n) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
        var rng = new System.Random(5);
        var seam = Color.Lerp(b, Color.black, 0.35f);
        for (int row = 0; row < 8; row++)
        {
            int cut = (row * 23) % n; // staggered plank ends
            var shade = Color.Lerp(a, b, (float)rng.NextDouble());
            for (int y = row * h; y < row * h + h; y++)
                for (int x = 0; x < n; x++)
                {
                    float grain = 0.96f + 0.04f * Mathf.Sin(x * 0.7f + y * 2.3f + row);
                    tex.SetPixel(x, y, y == row * h || x == cut ? seam : shade * grain);
                }
        }
        tex.Apply();
        return tex;
    }

    // One tile with a thin grout line; the floor mesh has world-unit UVs.
    public static Texture2D Tiles(Color fill, Color grout)
    {
        const int n = 64;
        var tex = new Texture2D(n, n) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                tex.SetPixel(x, y, x < 2 || y < 2 ? grout : fill);
        tex.Apply();
        return tex;
    }
}
