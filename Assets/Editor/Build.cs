using System.IO;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

// One-click (or batchmode) project setup and Android APK build.
// Batchmode: Unity.exe -batchmode -projectPath . -buildTarget Android -executeMethod Build.Apk -logFile build.log
public static class Build
{
    const string ScenePath = "Assets/Scenes/Main.unity";
    const string ApkPath = "Builds/NennisSaloon.apk";

    [MenuItem("Glow Salon/Build APK")]
    public static void Apk() => Apk(BuildOptions.None);

    // Development build (enables the Probe lag logger): -executeMethod Build.ApkDev
    public static void ApkDev() => Apk(BuildOptions.Development);

    static void Apk(BuildOptions options)
    {
        Setup();
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = ApkPath,
            target = BuildTarget.Android,
            options = options,
        });
        Debug.Log("Build result: " + report.summary.result + " -> " + Path.GetFullPath(ApkPath));
        if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }

    // Desktop build for quick testing on PC (WASD or mouse drag).
    [MenuItem("Glow Salon/Build Windows")]
    public static void Win()
    {
        Setup();
        var report = BuildPipeline.BuildPlayer(new[] { ScenePath }, "Builds/Win/GlowSalon.exe", BuildTarget.StandaloneWindows64, BuildOptions.None);
        if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }

    [MenuItem("Glow Salon/Setup Project")]
    public static void Setup()
    {
        // Runtime materials are cloned from this one; living in Resources keeps the Standard shader in the build.
        Directory.CreateDirectory("Assets/Resources");
        const string matPath = "Assets/Resources/Base.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (!mat)
        {
            mat = new Material(Shader.Find("Standard")) { enableInstancing = true };
            AssetDatabase.CreateAsset(mat, matPath);
        }
        mat.SetFloat("_Glossiness", 0.3f); // soft satin sheen
        EditorUtility.SetDirty(mat);

        if (!File.Exists(ScenePath))
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

        Directory.CreateDirectory("Assets/Icons");
        var full = WriteIcon("Assets/Icons/Icon.png", 512, true, true);
        var back = WriteIcon("Assets/Icons/IconBack.png", 432, true, false);
        var front = WriteIcon("Assets/Icons/IconFront.png", 432, false, true);
        PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { full }, IconKind.Any);
        SetAndroidIcons(AndroidPlatformIconKind.Adaptive, back, front);
        SetAndroidIcons(AndroidPlatformIconKind.Round, full);
        SetAndroidIcons(AndroidPlatformIconKind.Legacy, full);

        var android = NamedBuildTarget.Android;
        PlayerSettings.companyName = "Vince";
        PlayerSettings.productName = Game.SalonName;
        PlayerSettings.bundleVersion = "1.0";
        PlayerSettings.SetApplicationIdentifier(android, "com.vince.nennisaloon");
        PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        // CreatePrimitive adds Sphere/CapsuleColliders that no script names, so stripping removed them and every
        // primitive logged an error (thousands per second on the phone: that was the lag).
        PlayerSettings.stripEngineCode = false;
        // Classic Activity entry and internal install: the most compatible launch path across devices.
        PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
        PlayerSettings.Android.preferredInstallLocation = AndroidPreferredInstallLocation.Auto;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        PlayerSettings.SplashScreen.show = false;
        EditorUserBuildSettings.buildAppBundle = false;
        AssetDatabase.SaveAssets();
    }

    static void SetAndroidIcons(PlatformIconKind kind, params Texture2D[] layers)
    {
        var icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
        foreach (var icon in icons) icon.SetTextures(layers);
        PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, icons);
    }

    // ---------- App icon: nail polish bottle with sparkles on a pink-lilac gradient ----------

    static Texture2D WriteIcon(string path, int n, bool background, bool foreground)
    {
        const int ss = 4; // supersampling per axis for antialiasing
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                Vector3 rgb = Vector3.zero;
                float a = 0;
                for (int sy = 0; sy < ss; sy++)
                    for (int sx = 0; sx < ss; sx++)
                    {
                        var c = IconSample((x + (sx + 0.5f) / ss) / n, (y + (sy + 0.5f) / ss) / n, background, foreground);
                        rgb += new Vector3(c.r, c.g, c.b) * c.a;
                        a += c.a;
                    }
                tex.SetPixel(x, y, a > 0 ? new Color(rgb.x / a, rgb.y / a, rgb.z / a, a / (ss * ss)) : Color.clear);
            }
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Color Hex(string h) => ColorUtility.TryParseHtmlString(h, out var c) ? c : Color.magenta;

    static Color IconSample(float u, float v, bool background, bool foreground)
    {
        var p = new Vector2(u, v);
        var c = Color.clear;
        if (background)
        {
            c = Color.Lerp(Hex("#B79BFF"), Hex("#FF8FC7"), (v + 1 - u) / 2);
            c = Color.Lerp(c, Color.white, 0.25f * Mathf.Clamp01(1 - Vector2.Distance(p, new(0.3f, 0.8f)) / 0.6f));
        }
        if (!foreground) return c;

        Over(ref c, new Color(1, 1, 1, 0.95f), RoundRect(p, new(0.5f, 0.4f), new(0.17f, 0.15f), 0.08f));  // glass
        Over(ref c, Hex("#FF3D7F"), RoundRect(p, new(0.5f, 0.38f), new(0.13f, 0.11f), 0.06f));             // polish
        Over(ref c, new Color(1, 1, 1, 0.6f), RoundRect(p, new(0.42f, 0.42f), new(0.025f, 0.06f), 0.025f)); // shine
        Over(ref c, Color.white, RoundRect(p, new(0.5f, 0.57f), new(0.06f, 0.03f), 0.01f));                 // neck
        Over(ref c, Hex("#4B2E5C"), RoundRect(p, new(0.5f, 0.69f), new(0.07f, 0.1f), 0.035f));              // cap
        Over(ref c, Color.white, Sparkle(p, new(0.72f, 0.7f), 0.09f));
        Over(ref c, Color.white, Sparkle(p, new(0.28f, 0.62f), 0.055f));
        Over(ref c, Color.white, Sparkle(p, new(0.7f, 0.3f), 0.04f));
        return c;
    }

    static bool RoundRect(Vector2 p, Vector2 center, Vector2 half, float r)
    {
        var q = new Vector2(Mathf.Abs(p.x - center.x), Mathf.Abs(p.y - center.y)) - (half - Vector2.one * r);
        return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0) - r <= 0;
    }

    // Four-pointed star (astroid).
    static bool Sparkle(Vector2 p, Vector2 center, float size) =>
        Mathf.Sqrt(Mathf.Abs(p.x - center.x)) + Mathf.Sqrt(Mathf.Abs(p.y - center.y)) <= Mathf.Sqrt(size);

    static void Over(ref Color dst, Color src, bool inside)
    {
        if (!inside) return;
        float a = src.a + dst.a * (1 - src.a);
        var rgb = ((Vector4)src * src.a + (Vector4)dst * dst.a * (1 - src.a)) / a;
        dst = new Color(rgb.x, rgb.y, rgb.z, a);
    }
}
