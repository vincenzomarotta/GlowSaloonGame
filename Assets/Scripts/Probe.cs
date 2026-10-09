#if DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using UnityEngine;

// Temporary lag probe (development builds only): logs a breakdown of every slow frame to logcat.
public class Probe : MonoBehaviour
{
    static readonly string[] Markers =
    {
        "PlayerLoop", "Update.ScriptRunBehaviourUpdate", "PreLateUpdate.ScriptRunBehaviourLateUpdate",
        "Update.ScriptRunDelayedDynamicFrameRate", "FixedUpdate.PhysicsFixedUpdate", "Physics.Simulate",
        "PostLateUpdate.FinishFrameRendering", "Camera.Render", "Shadows.RenderShadowmap", "Render.OpaqueGeometry",
        "Gfx.WaitForPresentOnGfxThread", "Gfx.WaitForRenderThread", "WaitForTargetFPS", "GC.Collect", "GC.Alloc",
        "Canvas.SendWillRenderCanvases", "Canvas.BuildBatch", "Instantiate", "Destroy", "Loading.ReadObject",
    };

    readonly List<(string name, ProfilerRecorder rec)> recs = new();
    ProfilerRecorder gcMem;
    float worst, timer;
    int frames;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() => DontDestroyOnLoad(new GameObject("Probe").AddComponent<Probe>());

    void OnEnable()
    {
        foreach (var m in Markers) recs.Add((m, ProfilerRecorder.StartNew(ProfilerCategory.Internal, m, 1)));
        gcMem = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
        Debug.Log($"PROBE start {SystemInfo.deviceModel} {SystemInfo.graphicsDeviceType} {Screen.width}x{Screen.height} cpu={SystemInfo.processorType} x{SystemInfo.processorCount}");
    }

    void OnDisable()
    {
        foreach (var (_, r) in recs) r.Dispose();
        gcMem.Dispose();
    }

    void Update()
    {
        float ms = Time.unscaledDeltaTime * 1000;
        worst = Mathf.Max(worst, ms);
        timer += Time.unscaledDeltaTime;
        frames++;
        if (timer >= 1)
        {
            Debug.Log($"PROBE sec fps={frames} worst={worst:0}ms");
            timer = 0;
            frames = 0;
            worst = 0;
        }
        if (ms < 30) return;
        var sb = new StringBuilder($"PROBE slow {ms:0}ms gc={gcMem.LastValue / 1024}KB");
        foreach (var (name, r) in recs)
        {
            double v = r.LastValue / 1e6; // ns -> ms
            if (r.Valid && v >= 1) sb.Append($" | {name}={v:0.0}");
        }
        Debug.Log(sb.ToString());
    }
}
#endif
