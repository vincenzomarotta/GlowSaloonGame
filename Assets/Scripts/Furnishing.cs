using UnityEngine;

// Bonus furniture bought on unlock tiles. It all stands by the entrance, which never moves when the salon grows.
// The bonuses are read where they apply (Game.Has): sofa = patience, coffee = staff speed, sign = more customers,
// carpet = VIPs more often and richer.
public static class Furnishing
{
    public static void Build(string id, bool pop)
    {
        var t = new GameObject("Furnishing " + id).transform;
        Color white = Color.white, pink = Fx.Hex("#FF8FB8"), gold = Fx.Hex("#F5C542"), warm = Fx.Hex("#FFF3B0");
        switch (id)
        {
            case "sofa": // front right corner, facing the room, with a little round table
                t.position = new(4.4f, 0, 0.75f);
                Fx.Box(t, new(0, 0.22f, 0), new(2f, 0.36f, 0.8f), pink, solid: true);
                Fx.Box(t, new(-0.48f, 0.45f, 0.05f), new(0.92f, 0.14f, 0.62f), Fx.Hex("#FFC7D9"));
                Fx.Box(t, new(0.48f, 0.45f, 0.05f), new(0.92f, 0.14f, 0.62f), Fx.Hex("#FFC7D9"));
                Fx.Box(t, new(0, 0.72f, -0.32f), new(2f, 0.62f, 0.18f), pink);
                Fx.Box(t, new(-1f, 0.5f, 0), new(0.18f, 0.5f, 0.8f), pink);
                Fx.Box(t, new(1f, 0.5f, 0), new(0.18f, 0.5f, 0.8f), pink);
                Fx.Ball(t, new(-0.6f, 0.7f, -0.16f), new Vector3(0.34f, 0.34f, 0.14f), Fx.Hex("#B9A2E8"));
                Fx.Cyl(t, new(0, 0.1f, 1.05f), new(0.08f, 0.1f, 0.08f), Fx.Hex("#C9C9D6"));
                Fx.Cyl(t, new(0, 0.22f, 1.05f), new(0.7f, 0.03f, 0.5f), white);
                Fx.Box(t, new(0.1f, 0.26f, 1.05f), new(0.26f, 0.03f, 0.2f), Fx.Hex("#9FD8F5")); // magazine
                break;
            case "coffee": // counter beside the sofa
                t.position = new(6.3f, 0, 2.25f);
                Fx.Box(t, new(0, 0.45f, 0), new(0.8f, 0.9f, 0.65f), white, solid: true);
                Fx.Box(t, new(0, 0.93f, 0), new(0.86f, 0.06f, 0.7f), pink);
                Fx.Box(t, new(0.08f, 1.2f, -0.08f), new(0.42f, 0.5f, 0.36f), Fx.Hex("#B9A2E8"));
                Fx.Box(t, new(0.08f, 1.3f, 0.11f), new(0.3f, 0.12f, 0.04f), Fx.Hex("#4A3F55"));
                Fx.Cyl(t, new(0.08f, 1.02f, 0.12f), new(0.12f, 0.05f, 0.12f), white);
                Fx.Cyl(t, new(-0.28f, 1.02f, 0.16f), new(0.12f, 0.05f, 0.12f), white);
                Fx.Cyl(t, new(-0.28f, 1.06f, 0.16f), new(0.09f, 0.01f, 0.09f), Fx.Hex("#8A5A3C"));
                break;
            case "sign": // lit "APERTO" board on the sidewalk, right of the door
                t.position = new(1.85f, 0, -1.25f);
                Fx.Cyl(t, new(0, 0.04f, 0), new(0.45f, 0.04f, 0.45f), Fx.Hex("#E9E1F0"));
                Fx.Cyl(t, new(0, 0.75f, 0), new(0.08f, 0.75f, 0.08f), white);
                Fx.Box(t, new(0, 1.75f, 0), new(1.5f, 0.6f, 0.12f), Fx.Hex("#FF4F8B"));
                Fx.Text(t, new(0, 1.75f, -0.08f), "APERTO", 0.05f, white, Quaternion.identity);
                for (float x = -0.65f; x <= 0.66f; x += 0.26f)
                {
                    Fx.Ball(t, new(x, 2.1f, -0.02f), 0.1f, warm);
                    Fx.Ball(t, new(x, 1.4f, -0.02f), 0.1f, warm);
                }
                break;
            case "carpet": // red carpet from the curb to the door, with two gold posts
                t.position = new(0, 0, -1.9f);
                Fx.Box(t, new(0, 0.012f, 0), new(1.5f, 0.02f, 3f), Fx.Hex("#E8434F"));
                Fx.Box(t, new(-0.72f, 0.024f, 0), new(0.06f, 0.012f, 3f), gold);
                Fx.Box(t, new(0.72f, 0.024f, 0), new(0.06f, 0.012f, 3f), gold);
                foreach (var x in new[] { -1.05f, 1.05f })
                {
                    Fx.Cyl(t, new(x, 0.45f, 1.2f), new(0.08f, 0.45f, 0.08f), gold);
                    Fx.Ball(t, new(x, 0.95f, 1.2f), 0.17f, gold);
                }
                break;
        }
        if (!pop) return;
        Fx.Pop(t);
        Fx.Burst(t.position + Vector3.up, 24, Fx.Confetti);
    }
}
