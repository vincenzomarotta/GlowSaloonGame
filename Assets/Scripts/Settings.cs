using UnityEngine;

// Player options, persisted in PlayerPrefs (all on by default).
public static class Settings
{
    public static bool Music
    {
        get => Get("music");
        set { Set("music", value); Game.I.ApplySettings(); }
    }

    public static bool Sounds { get => Get("sfx"); set => Set("sfx", value); }
    public static bool Vibration { get => Get("haptics"); set => Set("haptics", value); }

    static bool Get(string key) => PlayerPrefs.GetInt(key, 1) == 1;

    static void Set(string key, bool on)
    {
        PlayerPrefs.SetInt(key, on ? 1 : 0);
        PlayerPrefs.Save();
    }
}

// Short haptic ticks. Android's Handheld.Vibrate is far too long for feedback, so use VibrationEffect.
public static class Haptics
{
    static float last;
#if UNITY_ANDROID && !UNITY_EDITOR
    static AndroidJavaObject vibrator;
#endif

    public static void Tick(long ms = 15, int amplitude = 70)
    {
        if (!Settings.Vibration || Time.unscaledTime - last < 0.07f) return;
        last = Time.unscaledTime;
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            if (vibrator == null)
            {
                using var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unity.GetStatic<AndroidJavaObject>("currentActivity");
                vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
            }
            using var effects = new AndroidJavaClass("android.os.VibrationEffect");
            using var effect = effects.CallStatic<AndroidJavaObject>("createOneShot", ms, amplitude);
            vibrator.Call("vibrate", effect);
        }
        catch
        {
            Handheld.Vibrate(); // pre-Android 8 fallback; also makes Unity add the VIBRATE permission
        }
#endif
    }
}
