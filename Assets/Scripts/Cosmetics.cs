using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// End-game money sink: outfits, hair, wall and floor colors, decorations. Owned items persist across salons.
public static class Cosmetics
{
    public class Item
    {
        public string id, name;
        public int price;
        public Color a, b;
        public Item(string id, string name, int price, string a, string b = null)
        {
            this.id = id;
            this.name = name;
            this.price = price;
            this.a = Fx.Hex(a);
            this.b = Fx.Hex(b ?? a);
        }
    }

    public static readonly Item[] Outfits =
    {
        new("o0", "Rosa", 0, "#FF5C9A"), new("o1", "Lilla", 800, "#B388FF"), new("o2", "Menta", 800, "#5FD3A0"),
        new("o3", "Pesca", 1500, "#FFA77A"), new("o4", "Cielo", 1500, "#6FBEFF"), new("o5", "Oro", 5000, "#E8B931"),
    };

    public static readonly Item[] Hair =
    {
        new("h0", "Castano", 0, "#6B3E2E"), new("h1", "Biondo", 600, "#E8C07D"), new("h2", "Nero", 600, "#2B2222"),
        new("h3", "Rosa", 1500, "#FF6FB5"), new("h4", "Lilla", 1500, "#B388FF"), new("h5", "Platino", 4000, "#F2F2F2"),
    };

    public static readonly Item[] Walls =
    {
        new("w0", "Lavanda", 0, "#EBDDF8"), new("w1", "Pesca", 1500, "#FFE1D0"), new("w2", "Menta", 1500, "#D8F3E6"),
        new("w3", "Cielo", 2500, "#DCEBFF"), new("w4", "Rosa", 2500, "#FFD6E5"), new("w5", "Crema", 4000, "#FFF4D6"),
    };

    public static readonly Item[] Floors =
    {
        new("f0", "Rosa", 0, "#F9DCE6", "#EFC4D3"), new("f1", "Lilla", 2000, "#EDE3FB", "#DCCBF3"), new("f2", "Menta", 2000, "#E0F5EA", "#C8EBD8"),
        new("f3", "Legno", 3500, "#F1DCC4", "#E2C6A8"), new("f4", "Marmo", 5000, "#F7F7FA", "#E2E2EC"), new("f5", "Cielo", 3500, "#E3EEFF", "#CBDDF7"),
    };

    // Toggleable once bought.
    public static readonly Item[] Decor =
    {
        new("d0", "Lucine", 1200, "#FFE08A"), new("d1", "Palloncini", 2000, "#FF8FB8"),
        new("d2", "Cuori", 3000, "#FF4F8B"), new("d3", "Piantine", 2500, "#7BC67E"),
    };

    // Toggleable like decorations: Hope follows the player around the salon.
    public static readonly Item[] Pets = { new("p0", "Hope", 1500, "#F28C28", "#FFFFFF") };

    static HashSet<string> owned;
    static HashSet<string> Owned => owned ??= new HashSet<string>(PlayerPrefs.GetString("v2.owned", "").Split(',').Where(s => s != ""));

    public static bool Has(Item i) => i.price == 0 || Owned.Contains(i.id);

    public static void Buy(Item i)
    {
        Owned.Add(i.id);
        PlayerPrefs.SetString("v2.owned", string.Join(",", Owned));
    }

    public static Item Equipped(Item[] set) => set.FirstOrDefault(i => i.id == PlayerPrefs.GetString("v2.eq." + set[0].id, set[0].id)) ?? set[0];
    public static void Equip(Item[] set, Item i) => PlayerPrefs.SetString("v2.eq." + set[0].id, i.id);

    public static bool DecorOn(string id) => Owned.Contains(id) && PlayerPrefs.GetInt("v2.deco." + id, 1) == 1;
    public static void SetDecor(Item i, bool on) => PlayerPrefs.SetInt("v2.deco." + i.id, on ? 1 : 0);
}
