using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

// Screen overlay: cash counter, side buttons, joystick, toasts, VIP bar and modal panels
// (settings, shop, missions, confirm). There is no EventSystem: this class reads raw input.
public class Hud : MonoBehaviour
{
    public static Hud I;
    public Vector2 Stick { get; private set; }

    static readonly Color Ink = Fx.Hex("#4A3F55");
    static readonly Color Accent = Fx.Hex("#FF4F8B");
    static readonly Color Lilac = Fx.Hex("#9C88D9");
    static readonly Color Soft = Fx.Hex("#EFE6FA");
    static readonly Color Disabled = Fx.Hex("#CFC6DA");
    static readonly Color Mint = Fx.Hex("#5FD3A0");

    Canvas canvas;
    RectTransform root, cashPill, stickBase, stickKnob, settingsButton, shopButton, missionButton, pauseButton, vipPill, rushPill;
    GameObject missionBadge, panel;
    Text cashText, toast, vipText, rushText;
    Image toastBg;
    Vector2 origin;
    bool dragging;
    float punch, toastTime;
    int shopTab;
    Action<RectTransform> panelBuild;
    readonly List<(RectTransform rect, Action tap)> panelTaps = new();
    static Sprite round;

    public static void Create()
    {
        var go = new GameObject("Hud");
        I = go.AddComponent<Hud>();
        I.canvas = go.AddComponent<Canvas>();
        I.canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;
        I.Build();
    }

    // ---------- Generated sprites ----------

    // 64px antialiased circle; 9-sliced it gives pill shapes.
    static Sprite Round()
    {
        if (round) return round;
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new(x + 0.5f, y + 0.5f), new(n / 2f, n / 2f));
                tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(n / 2f - d)));
            }
        tex.Apply();
        return round = Sprite.Create(tex, new Rect(0, 0, n, n), new(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(31, 31, 31, 31));
    }

    // Soft purple edge darkening for a more polished look.
    static Sprite Vignette()
    {
        const int n = 128;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var tint = Fx.Hex("#6B4C8A");
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new(x, y), new(n / 2f, n / 2f)) / (n / 2f);
                tex.SetPixel(x, y, new Color(tint.r, tint.g, tint.b, Mathf.SmoothStep(0, 0.32f, (d - 0.6f) / 0.6f)));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new(0.5f, 0.5f));
    }

    // Round white button with a baked soft shadow; glyph(p) gives the icon coverage at p (pixels from center).
    static Sprite IconButton(Func<Vector2, float> glyph)
    {
        const int n = 128;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var c = new Vector2(n / 2f, n / 2f + 3);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                var col = new Color(0.3f, 0.2f, 0.4f, 0.22f * Mathf.Clamp01((62 - Vector2.Distance(p, c - new Vector2(0, 6))) / 12f));
                Blend(ref col, Color.white, Mathf.Clamp01(54 - Vector2.Distance(p, c)));
                Blend(ref col, Lilac, Mathf.Clamp01(glyph(p - c)));
                tex.SetPixel(x, y, col);
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new(0.5f, 0.5f));
    }

    static float Gear(Vector2 p)
    {
        float r = p.magnitude;
        float outer = 25 + 7 * Mathf.SmoothStep(0, 1, (Mathf.Cos(Mathf.Atan2(p.y, p.x) * 8) + 0.3f) * 2);
        return Mathf.Clamp01(outer - r) * Mathf.Clamp01(r - 10);
    }

    // Antialiased rounded rectangle coverage.
    static float Box(Vector2 p, Vector2 center, Vector2 half, float r)
    {
        var q = new Vector2(Mathf.Abs(p.x - center.x), Mathf.Abs(p.y - center.y)) - (half - Vector2.one * r);
        return Mathf.Clamp01(0.5f - (Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0) - r));
    }

    static float Bag(Vector2 p)
    {
        float body = Box(p, new(0, -6), new(24, 22), 6);
        float ring = Mathf.Clamp01(5 - Mathf.Abs(Vector2.Distance(p, new(0, 16)) - 12)) * (p.y > 14 ? 1 : 0);
        return Mathf.Max(body, ring);
    }

    static float List(Vector2 p)
    {
        float v = 0;
        foreach (var y in new[] { 16f, 0f, -16f })
        {
            v = Mathf.Max(v, Box(p, new(8, y), new(17, 4), 4));
            v = Mathf.Max(v, Mathf.Clamp01(5.5f - Vector2.Distance(p, new(-20, y))));
        }
        return v;
    }

    static float PauseBars(Vector2 p) => Mathf.Max(Box(p, new(-11, 0), new(6.5f, 20), 3), Box(p, new(11, 0), new(6.5f, 20), 3));

    static void Blend(ref Color dst, Color src, float a)
    {
        float outA = a + dst.a * (1 - a);
        if (outA <= 0) return;
        var rgb = ((Vector4)src * a + (Vector4)dst * dst.a * (1 - a)) / outA;
        dst = new Color(rgb.x, rgb.y, rgb.z, outA);
    }

    // ---------- Widgets ----------

    static Image Img(Transform parent, Color c, Vector2 size, Vector2 pos)
    {
        var go = new GameObject("Img", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = Round();
        img.type = Image.Type.Sliced;
        img.color = c;
        img.raycastTarget = false;
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        return img;
    }

    static Text Txt(Transform parent, int size, Color c, Vector2 pos, string text = "")
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = Fx.Font;
        t.fontSize = size;
        t.color = c;
        t.text = text;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        ((RectTransform)go.transform).anchoredPosition = pos;
        return t;
    }

    static Text LeftTxt(Transform parent, int size, Color c, Vector2 pos, float width, string text)
    {
        var t = Txt(parent, size, c, pos, text);
        t.alignment = TextAnchor.MiddleLeft;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.rectTransform.sizeDelta = new(width, 100);
        return t;
    }

    static Image FullScreen(Transform parent, Color c)
    {
        var img = Img(parent, c, Vector2.zero, Vector2.zero);
        img.sprite = null; // plain rectangle covering the screen
        img.type = Image.Type.Simple;
        img.rectTransform.anchorMin = Vector2.zero;
        img.rectTransform.anchorMax = Vector2.one;
        return img;
    }

    RectTransform SideButton(Func<Vector2, float> glyph)
    {
        var b = Img(root, Color.white, new(130, 130), Vector2.zero).rectTransform;
        b.anchorMin = b.anchorMax = b.pivot = new(1, 1);
        var img = b.GetComponent<Image>();
        img.sprite = IconButton(glyph);
        img.type = Image.Type.Simple;
        return b;
    }

    // Tappable pill inside a panel.
    RectTransform Button(RectTransform parent, string label, Color bg, Vector2 size, Vector2 pos, Action tap, int font = 50, Color? text = null)
    {
        var b = Img(parent, bg, size, pos).rectTransform;
        Txt(b, font, text ?? Color.white, Vector2.zero, label);
        if (tap != null) panelTaps.Add((b, tap));
        return b;
    }

    void Build()
    {
        root = (RectTransform)transform;
        FullScreen(root, Color.white).sprite = Vignette();

        cashPill = Img(root, Color.white, new(400, 120), Vector2.zero).rectTransform;
        cashPill.anchorMin = cashPill.anchorMax = new(0.5f, 1);
        cashPill.pivot = new(0.5f, 1);
        Img(cashPill, new Color(0, 0, 0, 0.12f), new(400, 120), new(0, -8)).transform.SetAsFirstSibling();
        var bill = Img(cashPill, Fx.Hex("#5BC27A"), new(90, 56), new(-125, 0));
        Img(bill.transform, Fx.Hex("#9BE3AE"), new(36, 56), Vector2.zero);
        cashText = Txt(cashPill, 64, Ink, new(40, 0));

        vipPill = Img(root, Fx.Hex("#FFD166"), new(620, 86), Vector2.zero).rectTransform;
        vipPill.anchorMin = vipPill.anchorMax = new(0.5f, 1);
        vipPill.pivot = new(0.5f, 1);
        vipText = Txt(vipPill, 42, Ink, Vector2.zero);
        vipPill.gameObject.SetActive(false);

        rushPill = Img(root, Accent, new(720, 80), Vector2.zero).rectTransform;
        rushPill.anchorMin = rushPill.anchorMax = new(0.5f, 1);
        rushPill.pivot = new(0.5f, 1);
        rushText = Txt(rushPill, 40, Color.white, Vector2.zero);
        rushPill.gameObject.SetActive(false);

        settingsButton = SideButton(Gear);
        shopButton = SideButton(Bag);
        missionButton = SideButton(List);
        pauseButton = SideButton(PauseBars);
        missionBadge = Img(missionButton, Accent, new(34, 34), new(-22, -22)).gameObject;
        var badge = (RectTransform)missionBadge.transform;
        badge.anchorMin = badge.anchorMax = new(1, 1);

        // Over a panel's white card the white toast would vanish: a pink pill goes behind it there.
        toastBg = Img(root, Accent, Vector2.zero, new(0, 420));
        toastBg.gameObject.SetActive(false);
        toast = Txt(root, 76, Color.white, new(0, 420));
        var shadow = toast.gameObject.AddComponent<Shadow>();
        shadow.effectColor = Accent;
        shadow.effectDistance = new(0, -6);
        toast.color = Color.clear;

        stickBase = Img(root, new Color(1, 1, 1, 0.35f), new(300, 300), Vector2.zero).rectTransform;
        stickBase.GetComponent<Image>().type = Image.Type.Simple;
        stickKnob = Img(stickBase, new Color(1, 1, 1, 0.9f), new(130, 130), Vector2.zero).rectTransform;
        stickKnob.GetComponent<Image>().type = Image.Type.Simple;
        stickBase.gameObject.SetActive(false);
    }

    void Start()
    {
        // Test aids for screenshots: -autoplay together with -settings / -shop [-tab N] / -missions / -pause / -news.
        if (!Game.Autoplay) return;
        var args = Environment.GetCommandLineArgs();
        int tab = Array.IndexOf(args, "-tab");
        if (tab >= 0 && tab + 1 < args.Length) int.TryParse(args[tab + 1], out shopTab);
        if (args.Contains("-settings")) Open(BuildSettings);
        if (args.Contains("-shop")) Open(BuildShop);
        if (args.Contains("-missions")) Open(BuildMissions);
        if (args.Contains("-pause")) Open(BuildPause);
        if (args.Contains("-news")) ShowNews();
    }

    // ---------- Panels ----------

    void Open(Action<RectTransform> build)
    {
        panelBuild = build;
        Time.timeScale = 0;
        dragging = false;
        Stick = Vector2.zero;
        Rebuild();
    }

    void Rebuild()
    {
        if (panel) Destroy(panel);
        panelTaps.Clear();
        panel = FullScreen(root, new Color(0.18f, 0.1f, 0.25f, 0.55f)).gameObject;
        panelBuild((RectTransform)panel.transform);
        toastBg.transform.SetAsLastSibling();
        toast.transform.SetAsLastSibling();
    }

    void Close()
    {
        Destroy(panel);
        panel = null;
        Time.timeScale = 1;
        AudioListener.pause = false;
    }

    // Every panel stops the game clock; the pause one also silences music and sounds.
    void BuildPause(RectTransform dim)
    {
        AudioListener.pause = true;
        var card = Card(dim, new(760, 560), "Pausa");
        Txt(card, 46, Ink, new(0, 50), "Il salone ti aspetta!");
        Button(card, "Riprendi", Accent, new(380, 115), new(0, -180), Close, 54);
    }

    RectTransform Card(RectTransform dim, Vector2 size, string title)
    {
        var card = Img(dim, Color.white, size, Vector2.zero).rectTransform;
        var t = Txt(card, 70, Ink, new(0, size.y / 2 - 90), title);
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        t.rectTransform.sizeDelta = new(size.x - 80, 130);
        t.resizeTextForBestFit = true; // long salon names shrink instead of spilling out of the card
        t.resizeTextMinSize = 36;
        t.resizeTextMaxSize = 70;
        return card;
    }

    void CloseButton(RectTransform card) => Button(card, "Chiudi", Accent, new(380, 115), new(0, -card.sizeDelta.y / 2 + 100), Close, 54);

    void BuildSettings(RectTransform dim)
    {
        var card = Card(dim, new(840, 1180), "Impostazioni");
        Toggle(card, "Musica", 310, () => Settings.Music, v => Settings.Music = v);
        Toggle(card, "Suoni", 170, () => Settings.Sounds, v => Settings.Sounds = v);
        Toggle(card, "Vibrazione", 30, () => Settings.Vibration, v => { Settings.Vibration = v; Haptics.Tick(30, 120); });
        Button(card, "Cerca aggiornamenti", Soft, new(600, 100), new(0, -100), Updater.Check, 46, Ink);
        Button(card, $"Versione {Updater.Version}  ·  Novità", Color.clear, new(600, 70), new(0, -180), ShowNews, 36, Lilac);
        Img(card, Soft, new(700, 6), new(0, -240));
        var love = Txt(card, 60, Accent, new(0, -350), "Ti amo nennina,\nbuon anniversario <3");
        love.lineSpacing = 1.1f;
        CloseButton(card);
    }

    // Label on the left, pill switch on the right; the whole row is tappable.
    void Toggle(RectTransform card, string label, float y, Func<bool> get, Action<bool> set)
    {
        var row = Img(card, Color.clear, new(700, 110), new(0, y)).rectTransform;
        LeftTxt(row, 58, Ink, new(-110, 0), 460, label);
        bool on = get();
        var track = Img(row, on ? Accent : Fx.Hex("#D9D2E3"), new(150, 76), new(260, 0));
        Img(track.transform, Color.white, new(60, 60), new(on ? 37 : -37, 0));
        panelTaps.Add((row, () => { set(!get()); Fx.Sfx("pop", 0.4f); Rebuild(); }));
    }

    static readonly string[] ShopTabs = { "Vestiti", "Capelli", "Pareti", "Pavimento", "Decori", "Postazioni", "Personale", "Animali" };

    void BuildShop(RectTransform dim)
    {
        var game = Game.I;
        var card = Card(dim, new(940, 1560), "Negozio");
        Txt(card, 46, Lilac, new(0, 610), "Hai " + Game.Money(game.cash) + " €");
        for (int i = 0; i < ShopTabs.Length; i++)
        {
            int tab = i;
            bool sel = tab == shopTab;
            Button(card, ShopTabs[i], sel ? Accent : Soft, new(275, 80), new(-290 + (i % 3) * 290, 505 - (i / 3) * 95),
                () => { shopTab = tab; Rebuild(); }, 42, sel ? Color.white : Ink);
        }

        if (shopTab < 4)
        {
            var set = new[] { Cosmetics.Outfits, Cosmetics.Hair, Cosmetics.Walls, Cosmetics.Floors }[shopTab];
            for (int i = 0; i < set.Length; i++) Swatch(card, set, set[i], new(-285 + (i % 3) * 285, 60 - (i / 3) * 420));
        }
        else if (shopTab == 4)
        {
            for (int i = 0; i < Cosmetics.Decor.Length; i++) DecorRow(card, Cosmetics.Decor[i], 170 - i * 150);
        }
        else if (shopTab == 5)
        {
            // One row per service, one button per seat: level and the price of the next one.
            var kinds = game.stations.GroupBy(s => s.kind).OrderBy(g => g.Key).ToList();
            for (int i = 0; i < kinds.Count; i++)
                LevelRow(card, Game.ServiceName[(int)kinds[i].Key], 170 - i * 104, kinds[i].OrderBy(s => s.id)
                    .Select(s => (s.level, Station.MaxLevel, s.UpgradeCost, (Action)(() => Game.I.Upgrade(s)))).ToList());
        }
        else if (shopTab == 6)
        {
            // Training: each level makes the worker 25% faster (stock clerks: faster and carrying more).
            var kinds = game.stations.Where(s => s.HasStaff).GroupBy(s => s.kind).OrderBy(g => g.Key).ToList();
            int row = 0;
            foreach (var g in kinds)
                LevelRow(card, Theme.Staff[(int)g.Key], 170 - row++ * 104, g.OrderBy(s => s.id)
                    .Select(s => (s.training, Station.MaxTraining, s.TrainCost, (Action)(() => Game.I.Train(s)))).ToList());
            if (Runner.Count > 0)
                LevelRow(card, "Magazziniere", 170 - row++ * 104,
                    new() { (game.ClerkLevel, Game.MaxClerkLevel, game.ClerkTrainCost, (Action)game.TrainClerks) });
            if (row == 0) Txt(card, 44, Lilac, new(0, 60), "Assumi qualcuno\nper poterlo formare!").lineSpacing = 1.1f;
        }
        else
        {
            // The dog's name is a surprise: it shows only once adopted.
            var dog = Cosmetics.Pets[0];
            DecorRow(card, dog, 170, "Ti segue ovunque nel salone", Cosmetics.Has(dog) ? dog.name : "Cagnolino");
        }
        CloseButton(card);
    }

    void Swatch(RectTransform card, Cosmetics.Item[] set, Cosmetics.Item item, Vector2 pos)
    {
        var cell = Img(card, Color.clear, new(270, 400), pos).rectTransform;
        Img(cell, Soft, new(176, 176), new(0, 80));
        Img(cell, item.a, new(160, 160), new(0, 80));
        if (item.b != item.a) Img(cell, item.b, new(80, 80), new(0, 80));
        Txt(cell, 44, Ink, new(0, -45), item.name);

        bool owned = Cosmetics.Has(item), inUse = Cosmetics.Equipped(set) == item;
        string label = inUse ? "In uso" : owned ? "Usa" : Game.Money(item.price) + " €";
        var bg = inUse ? Mint : owned ? Lilac : Game.I.cash >= item.price ? Accent : Disabled;
        Button(cell, label, bg, new(240, 84), new(0, -140), null, 42);
        panelTaps.Add((cell, () =>
        {
            if (!owned && !Pay(item.price)) return;
            if (!owned) Cosmetics.Buy(item);
            Cosmetics.Equip(set, item);
            Game.I.ApplyCosmetics(true);
            Rebuild();
        }));
    }

    void DecorRow(RectTransform card, Cosmetics.Item item, float y, string sub = null, string name = null)
    {
        var row = Img(card, Soft, new(820, 124), new(0, y)).rectTransform;
        Img(row, item.a, new(70, 70), new(-345, 0));
        if (item.b != item.a) Img(row, item.b, new(30, 30), new(-345, 0));
        LeftTxt(row, 50, Ink, new(-60, sub != null ? 18 : 0), 480, name ?? item.name);
        if (sub != null) LeftTxt(row, 32, Lilac, new(-60, -30), 480, sub);
        bool owned = Cosmetics.Has(item), on = Cosmetics.DecorOn(item.id);
        string label = !owned ? Game.Money(item.price) + " €" : on ? "Attivo" : "Spento";
        var bg = !owned ? (Game.I.cash >= item.price ? Accent : Disabled) : on ? Mint : Lilac;
        Button(row, label, bg, new(250, 84), new(265, 0), () =>
        {
            if (!owned && !Pay(item.price)) return;
            if (!owned) Cosmetics.Buy(item);
            Cosmetics.SetDecor(item, !owned || !on);
            Game.I.ApplyCosmetics(true);
            Rebuild();
        }, 42);
    }

    // Name on the left, then one button per seat (or worker) with its level and the price of the next level.
    void LevelRow(RectTransform card, string label, float y, List<(int lv, int max, int cost, Action buy)> slots)
    {
        var row = Img(card, Soft, new(820, 94), new(0, y)).rectTransform;
        LeftTxt(row, 42, Ink, new(-205, 0), 360, label);
        for (int i = 0; i < slots.Count; i++)
        {
            var (lv, max, cost, buy) = slots[i];
            var pos = new Vector2(95 + i * 210, 0);
            if (lv >= max) { Button(row, $"Lv {lv}\nMAX", Mint, new(200, 80), pos, null, 30); continue; }
            Button(row, $"Lv {lv}\n{Game.Money(cost)} €", Game.I.cash >= cost ? Accent : Disabled, new(200, 80), pos, () =>
            {
                if (Game.I.cash < cost) { Toast("Servono più soldi!"); return; }
                buy();
                Rebuild();
            }, 30);
        }
    }

    static bool Pay(int price)
    {
        if (Game.I.cash < price)
        {
            I.Toast("Servono più soldi!");
            return false;
        }
        Game.I.AddCash(-price);
        Fx.Sfx("unlock", 0.6f);
        return true;
    }

    void BuildMissions(RectTransform dim)
    {
        var card = Card(dim, new(900, 1150), "Missioni del giorno");
        var list = Missions.All;
        for (int i = 0; i < list.Count; i++)
        {
            var m = list[i];
            var row = Img(card, Soft, new(820, 210), new(0, 270 - i * 240)).rectTransform;
            LeftTxt(row, 44, Ink, new(-120, 30), 520, m.Text);
            LeftTxt(row, 40, Lilac, new(-120, -45), 520, m.type == Missions.Type.Earn
                ? $"{Game.Money(m.progress)} / {Game.Money(m.goal)} €"
                : $"{m.progress} / {m.goal}");
            string reward = "+" + Game.Money(m.reward) + " €";
            if (m.claimed) Button(row, "Fatta!", Mint, new(250, 90), new(265, 0), null, 42);
            else if (m.Done) Button(row, reward, Accent, new(250, 90), new(265, 0), () => { Missions.Claim(m); RefreshBadges(); Rebuild(); }, 42);
            else Button(row, reward, Disabled, new(250, 90), new(265, 0), null, 42);
        }
        Txt(card, 40, Lilac, new(0, -330), "Nuove missioni ogni giorno!");
        CloseButton(card);
    }

    // Shown once after an update (Game.Load) and from Settings. Rewrite it for every release that adds something.
    const string NewsTitle = "Novità della versione 6";
    const string News =
        "• <b>Nuovo</b>: altre 2 magazziniere da assumere nell'ala nuova. Sposa e Star ora pagano un po' meno.\n" +
        "• <b>3 servizi nuovi</b>: Pedicure, Ciglia e Sopracciglia, con scaffali e dipendenti. Si sbloccano ingrandendo ancora il salone.\n" +
        "• <b>Arredi con bonus</b>: Divanetto (le clienti aspettano di più), Macchina del caffè (dipendenti più veloci), Insegna luminosa (più clienti), Tappeto rosso (VIP più spesso e più ricche). Ogni casella ora dice cosa fa.\n" +
        "• <b>Postazioni più belle</b> a ogni livello: bordo dorato, fiori, lampada e tappeto d'oro.\n" +
        "• <b>Clienti speciali</b>, solo con le tue mani come le VIP: la Sposa (piega, trucco e unghie) e la Star (un servizio, di corsa).\n" +
        "• <b>Gruppi di amiche</b> col fiocco rosa: se le servi tutte senza perderne nessuna, ricevi un bonus.\n" +
        "• <b>Ora di punta</b>: per 45 secondi arrivano tante clienti. Se non ne perdi nessuna vinci metà dell'incasso.\n" +
        "• <b>Negozio › Personale</b>: forma dipendenti e magazziniere, lavorano più veloci.\n" +
        "• <b>Negozio › Animali</b>: adotta un cagnolino che ti segue ovunque.\n" +
        "• I messaggi nei menu ora si leggono bene.";

    public void ShowNews() => Open(dim =>
    {
        var card = Card(dim, new(940, 1640), NewsTitle);
        var t = LeftTxt(card, 38, Ink, new(0, 15), 840, News);
        t.lineSpacing = 1.05f;
        Button(card, "Evviva!", Accent, new(380, 115), new(0, -720), Close, 54);
    });

    public void Confirm(string title, string body, string yes, Action onYes, Action onNo)
    {
        Open(dim =>
        {
            var card = Card(dim, new(860, 760), title);
            Txt(card, 46, Ink, new(0, 60), body).lineSpacing = 1.15f;
            Button(card, "No", Disabled, new(300, 115), new(-170, -260), () => { Close(); onNo(); }, 52);
            Button(card, yes, Accent, new(300, 115), new(170, -260), () => { Close(); onYes(); }, 52);
        });
    }

    // ---------- HUD state ----------

    public void SetCash(int v, bool gained)
    {
        cashText.text = Game.Money(v);
        if (gained) punch = 0.12f;
    }

    public void Toast(string s, float seconds = 1.8f)
    {
        toast.text = s;
        toastTime = seconds;
    }

    public void RefreshBadges() => missionBadge.SetActive(Missions.AnyToClaim);

    public void ShowVip(Customer vip)
    {
        bool on = vip;
        if (vipPill.gameObject.activeSelf != on) vipPill.gameObject.SetActive(on);
        if (!on) return;
        int left = Mathf.Max(0, Mathf.CeilToInt(vip.VipTimeLeft));
        vipText.text = $"{vip.Title} {vip.Step + 1}/{vip.Plan.Length}: {Game.ServiceName[(int)vip.Plan[vip.Step]]}   {left / 60}:{left % 60:00}";
        vipText.color = left < 15 ? Accent : Ink;
    }

    public void ShowRush(float secondsLeft, int prize, bool lost)
    {
        bool on = secondsLeft > 0;
        if (rushPill.gameObject.activeSelf != on) rushPill.gameObject.SetActive(on);
        if (!on) return;
        int left = Mathf.CeilToInt(secondsLeft);
        rushText.text = lost ? $"Ora di punta  0:{left:00}  ·  premio perso" : $"Ora di punta  0:{left:00}  ·  premio +{Game.Money(prize)} €";
    }

    void Update()
    {
        ReadInput();

        // Keep top widgets below notches.
        float top = (Screen.height - Screen.safeArea.yMax) / canvas.scaleFactor;
        cashPill.anchoredPosition = new(0, -top - 40);
        vipPill.anchoredPosition = new(0, -top - 180);
        rushPill.anchoredPosition = new(0, -top - (vipPill.gameObject.activeSelf ? 280 : 180));
        settingsButton.anchoredPosition = new(-30, -top - 35);
        shopButton.anchoredPosition = new(-30, -top - 175);
        missionButton.anchoredPosition = new(-30, -top - 315);
        pauseButton.anchoredPosition = new(-30, -top - 455);
        punch = Mathf.MoveTowards(punch, 0, Time.unscaledDeltaTime * 0.6f);
        cashPill.localScale = Vector3.one * (1 + punch);

        toastTime -= Time.unscaledDeltaTime;
        float alpha = Mathf.Clamp01(toastTime * 3);
        toast.color = new Color(1, 1, 1, alpha);
        toast.rectTransform.localScale = Vector3.one * (1 + Mathf.Clamp01(toastTime - 1.5f));
        bool bg = panel && alpha > 0 && toast.text != "";
        if (toastBg.gameObject.activeSelf != bg) toastBg.gameObject.SetActive(bg);
        if (bg)
        {
            toastBg.color = new Color(Accent.r, Accent.g, Accent.b, alpha * 0.95f);
            toastBg.rectTransform.sizeDelta = new(toast.preferredWidth + 80, toast.preferredHeight + 36);
            toastBg.rectTransform.localScale = toast.rectTransform.localScale;
        }
    }

    // Floating joystick: touch anywhere, drag to move; the base follows the finger past its radius.
    void ReadInput()
    {
        Vector2 pos;
        bool down, held;
        if (Input.touchCount > 0)
        {
            var t = Input.GetTouch(0);
            pos = t.position;
            down = t.phase == TouchPhase.Began;
            held = t.phase != TouchPhase.Ended && t.phase != TouchPhase.Canceled;
        }
        else
        {
            pos = Input.mousePosition;
            down = Input.GetMouseButtonDown(0);
            held = Input.GetMouseButton(0);
        }

        if (panel)
        {
            if (down)
                foreach (var (rect, tap) in panelTaps.ToList())
                    if (RectTransformUtility.RectangleContainsScreenPoint(rect, pos, null)) { tap(); break; }
            return;
        }
        if (down)
        {
            bool Hit(RectTransform r) => RectTransformUtility.RectangleContainsScreenPoint(r, pos, null);
            if (Hit(settingsButton)) { Open(BuildSettings); return; }
            if (Hit(shopButton)) { Open(BuildShop); return; }
            if (Hit(missionButton)) { Open(BuildMissions); return; }
            if (Hit(pauseButton)) { Open(BuildPause); return; }
        }

        if (down) { origin = pos; dragging = true; }
        if (!held) dragging = false;

        float radius = Mathf.Min(Screen.width, Screen.height) * 0.13f;
        if (dragging)
        {
            var d = (pos - origin) / radius;
            if (d.magnitude > 1) { d.Normalize(); origin = pos - d * radius; }
            Stick = d.magnitude < 0.1f ? Vector2.zero : d;
        }
        else
        {
            // Keys only, so gamepad drift can't move the player.
            float x = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
            float y = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1 : 0) - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1 : 0);
            Stick = Vector2.ClampMagnitude(new Vector2(x, y), 1);
        }

        if (stickBase.gameObject.activeSelf != dragging) stickBase.gameObject.SetActive(dragging);
        if (dragging)
        {
            float size = radius * 2 / canvas.scaleFactor;
            stickBase.sizeDelta = new(size, size);
            stickKnob.sizeDelta = new(size * 0.45f, size * 0.45f);
            stickBase.position = origin;
            stickKnob.position = origin + Stick * radius;
        }
    }
}
