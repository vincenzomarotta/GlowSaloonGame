using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

// In-game updates from GitHub releases: the latest release is tagged "v<Build.Version>" and carries the APK.
// The request is anonymous, so the repo (or a releases-only copy of it) must be public.
public static class Updater
{
    const string Repo = "vincenzomarotta/GlowSaloonGame";
    static bool busy;

    [Serializable] class Release { public string tag_name; public string html_url; public Asset[] assets; }
    [Serializable] class Asset { public string name; public string browser_download_url; }

    public static int Version => int.TryParse(Application.version, out var v) ? v : 0;

    public static void Check()
    {
        if (!busy) Hud.I.StartCoroutine(CheckRoutine());
    }

    static IEnumerator CheckRoutine()
    {
        busy = true;
        Hud.I.Toast("Controllo...", 10);
        using var req = UnityWebRequest.Get($"https://api.github.com/repos/{Repo}/releases/latest");
        req.SetRequestHeader("User-Agent", "NennisSaloon");
        yield return req.SendWebRequest();
        busy = false;
        if (req.responseCode == 404) { Hud.I.Toast("Nessun aggiornamento trovato"); yield break; }
        if (req.result != UnityWebRequest.Result.Success) { Hud.I.Toast("Nessuna connessione"); yield break; }

        var release = JsonUtility.FromJson<Release>(req.downloadHandler.text);
        int.TryParse(release.tag_name?.TrimStart('v', 'V'), out int latest);
        if (latest <= Version) { Hud.I.Toast("Il gioco è aggiornato!"); yield break; }

        var apk = Array.Find(release.assets ?? new Asset[0], a => a.name.EndsWith(".apk"));
        Hud.I.Toast("", 0);
        Hud.I.Confirm("Aggiornamento", $"È disponibile la versione {latest}.\nScaricarla ora?", "Sì", () =>
        {
            if (Application.platform == RuntimePlatform.Android && apk != null) Hud.I.StartCoroutine(Download(apk.browser_download_url));
            else Application.OpenURL(release.html_url);
        }, () => { });
    }

    static IEnumerator Download(string url)
    {
        busy = true;
        using var req = new UnityWebRequest(url) { downloadHandler = new DownloadHandlerFile(ApkPath) { removeFileOnAbort = true } };
        var op = req.SendWebRequest();
        while (!op.isDone)
        {
            Hud.I.Toast($"Download {req.downloadProgress * 100:0}%", 1);
            yield return null;
        }
        busy = false;
        if (req.result != UnityWebRequest.Result.Success) { Hud.I.Toast("Download non riuscito"); yield break; }
        Hud.I.Toast("", 0);
        Install();
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    static AndroidJavaObject Activity()
    {
        using var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        return unity.GetStatic<AndroidJavaObject>("currentActivity");
    }

    // App cache dir: the only folder UpdateProvider (Assets/Plugins/Android) serves.
    static string ApkPath
    {
        get
        {
            using var activity = Activity();
            using var dir = activity.Call<AndroidJavaObject>("getCacheDir");
            return Path.Combine(dir.Call<string>("getAbsolutePath"), "update.apk");
        }
    }

    // Hands the APK to the system installer; Android asks once to allow installs from this app.
    static void Install()
    {
        using var activity = Activity();
        using var uris = new AndroidJavaClass("android.net.Uri");
        using var uri = uris.CallStatic<AndroidJavaObject>("parse", $"content://{Application.identifier}.update/update.apk");
        using var intent = new AndroidJavaObject("android.content.Intent", "android.intent.action.VIEW");
        intent.Call<AndroidJavaObject>("setDataAndType", uri, "application/vnd.android.package-archive").Dispose();
        intent.Call<AndroidJavaObject>("addFlags", 0x10000001).Dispose(); // NEW_TASK | GRANT_READ_URI_PERMISSION
        activity.Call("startActivity", intent);
    }
#else
    static string ApkPath => Path.Combine(Application.temporaryCachePath, "update.apk");
    static void Install() { }
#endif
}
