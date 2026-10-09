using System.Linq;
using UnityEngine;

// Salon names and labels. Every new salon (prestige) is a bigger beauty salon with its own name.
public static class Theme
{
    // Cached: only changes when a new salon reloads the scene.
    public static int Prestige { get; private set; }
    // Christmas decorations from Dec 1 to Jan 10 (-xmas forces them on in test builds).
    public static bool Christmas { get; private set; }

    public static void Load()
    {
        Prestige = PlayerPrefs.GetInt("v2.prestige", 0);
        var d = System.DateTime.Today;
        Christmas = d.Month == 12 || (d.Month == 1 && d.Day <= 10) || System.Environment.GetCommandLineArgs().Contains("-xmas");
    }

    static readonly string[] Titles = { "Nenni's Saloon", "Nenni's Beauty Palace", "Nenni's Glam Studio", "Nenni's Royal Salon" };
    public static string Title => Titles[Prestige % Titles.Length];
    public static string NextTitle => Titles[(Prestige + 1) % Titles.Length];

    public static readonly string[] Service = { "Manicure", "Piega", "Massaggio", "Trucco" };
    public static readonly string[] Product = { "Smalti", "Shampoo", "Asciugamani", "Rossetti" };
    public static readonly string[] Seat = { "Manicure", "Parrucchiere", "Massaggio", "Trucco" }; // unlock tile labels
    public static readonly string[] Staff = { "Estetista", "Parrucchiera", "Massaggiatrice", "Truccatrice" };
}
