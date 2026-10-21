using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Three daily goals, rerolled each calendar day, rewarded in banked cash.
public static class Missions
{
    public enum Type { Serve, Earn, Vip }

    public class Mission
    {
        public Type type;
        public Kind kind;
        public int goal, progress, reward;
        public bool claimed;
        public bool Done => progress >= goal;

        public string Text => type switch
        {
            Type.Serve => $"Servi {goal} clienti: {Game.ServiceName[(int)kind]}",
            Type.Earn => $"Incassa {Game.Money(goal)} €",
            _ => goal == 1 ? "Soddisfa 1 cliente VIP" : $"Soddisfa {goal} clienti VIP",
        };
    }

    static List<Mission> list;
    static DateTime listDay;
    static bool dirty;
    static string Today => DateTime.Today.ToString("yyyy-MM-dd");

    // Cached for the day: this runs for every banked bill, and PlayerPrefs calls are slow on Android.
    public static List<Mission> All
    {
        get
        {
            if (list != null && listDay == DateTime.Today) return list;
            listDay = DateTime.Today;
            list = PlayerPrefs.GetString("v4.mday") == Today ? Parse(PlayerPrefs.GetString("v4.missions", "")) : null;
            if (list == null || list.Count != 3) Generate();
            return list;
        }
    }

    public static bool AnyToClaim => All.Any(m => m.Done && !m.claimed);

    // Goals scale with what is unlocked and with the salon multiplier.
    static void Generate()
    {
        var game = Game.I;
        var rng = new System.Random(DateTime.Today.DayOfYear + DateTime.Today.Year * 1000);
        var kinds = game.stations.Select(s => s.kind).Distinct().ToList();
        int topPrice = kinds.Max(k => Game.Price[(int)k]) * game.Multiplier;
        list = new List<Mission>();

        var a = kinds[rng.Next(kinds.Count)];
        int serveGoal = 10 + rng.Next(11);
        list.Add(new Mission { type = Type.Serve, kind = a, goal = serveGoal, reward = Round(serveGoal * Game.Price[(int)a] * game.Multiplier / 3) });

        int earnGoal = Round(topPrice * (30 + rng.Next(30)));
        list.Add(new Mission { type = Type.Earn, goal = earnGoal, reward = Round(earnGoal / 18) });

        if (game.VipAvailable)
        {
            int vipGoal = 1 + rng.Next(2);
            list.Add(new Mission { type = Type.Vip, goal = vipGoal, reward = Round(topPrice * 4 * vipGoal) });
        }
        else
        {
            var b = kinds[rng.Next(kinds.Count)];
            int goal = 15 + rng.Next(16);
            list.Add(new Mission { type = Type.Serve, kind = b, goal = goal, reward = Round(goal * Game.Price[(int)b] * game.Multiplier / 3) });
        }
        Save();
    }

    static int Round(int v) => Mathf.Max(10, Mathf.RoundToInt(v / 10f) * 10);

    public static void Served(Kind kind, bool vipDone)
    {
        foreach (var m in All)
            if (!m.Done && ((m.type == Type.Serve && m.kind == kind) || (m.type == Type.Vip && vipDone))) m.progress++;
        Changed();
    }

    public static void Earned(int amount)
    {
        foreach (var m in All)
            if (!m.Done && m.type == Type.Earn) m.progress = Mathf.Min(m.goal, m.progress + amount);
        Changed();
    }

    public static void Claim(Mission m)
    {
        if (!m.Done || m.claimed) return;
        m.claimed = true;
        Save();
        Game.I.AddCash(m.reward, track: false);
        Fx.Sfx("unlock", 0.7f);
        Hud.I.Toast("Missione completata!");
    }

    static void Changed()
    {
        Save();
        Hud.I.RefreshBadges();
    }

    static void Save() => dirty = true;

    // Writes pending progress; called by Game.Save (every couple of seconds while anything changed, and on pause/quit).
    public static void Flush()
    {
        if (!dirty || list == null) return;
        dirty = false;
        PlayerPrefs.SetString("v4.mday", listDay.ToString("yyyy-MM-dd"));
        PlayerPrefs.SetString("v4.missions", string.Join("|", list.Select(m =>
            $"{(int)m.type}:{(int)m.kind}:{m.goal}:{m.progress}:{m.reward}:{(m.claimed ? 1 : 0)}")));
    }

    static List<Mission> Parse(string s)
    {
        try
        {
            return s.Split('|', StringSplitOptions.RemoveEmptyEntries).Select(part =>
            {
                var f = part.Split(':').Select(int.Parse).ToArray();
                return new Mission { type = (Type)f[0], kind = (Kind)f[1], goal = f[2], progress = f[3], reward = f[4], claimed = f[5] == 1 };
            }).ToList();
        }
        catch (FormatException) { return null; }
        catch (IndexOutOfRangeException) { return null; }
    }
}
