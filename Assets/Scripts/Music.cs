using UnityEngine;

// Procedural background loop: 8 bars of C–Am–F–G with arpeggio, bass, pad, shaker,
// and a marimba-like melody on the second pass. Note tails wrap around, so the loop is seamless.
public static class Music
{
    const int Rate = 44100;
    const float Beat = 60f / 96f;

    static readonly int[][] Chords = { new[] { 60, 64, 67, 72 }, new[] { 57, 60, 64, 69 }, new[] { 53, 57, 60, 65 }, new[] { 55, 59, 62, 67 } };
    static readonly int[] Bass = { 48, 45, 41, 43 };
    static readonly int[] Arp = { 0, 1, 2, 1, 3, 2, 1, 2 };

    // (beat in bar, length in beats, midi note) for bars 5-8.
    static readonly float[][][] Melody =
    {
        new[] { new[] { 0f, 1, 76 }, new[] { 1f, 1, 79 }, new[] { 2f, 0.5f, 81 }, new[] { 2.5f, 0.5f, 79 }, new[] { 3f, 1, 76 } },
        new[] { new[] { 0f, 1, 72 }, new[] { 1f, 1, 76 }, new[] { 2f, 0.5f, 74 }, new[] { 2.5f, 0.5f, 72 }, new[] { 3f, 1, 69 } },
        new[] { new[] { 0f, 1, 69 }, new[] { 1f, 1, 72 }, new[] { 2f, 1, 77 }, new[] { 3f, 0.5f, 76 }, new[] { 3.5f, 0.5f, 74 } },
        new[] { new[] { 0f, 1, 74 }, new[] { 1f, 1, 79 }, new[] { 2f, 0.5f, 77 }, new[] { 2.5f, 0.5f, 76 }, new[] { 3f, 1, 74 } },
    };

    static float Freq(float midi) => 440f * Mathf.Pow(2, (midi - 69) / 12f);

    // Pure math, safe to run off the main thread; wrap the result with ToClip on the main thread.
    public static float[] Render()
    {
        var rng = new System.Random(7);
        const int bars = 8;
        int length = (int)(bars * 4 * Beat * Rate);
        var data = new float[length];

        for (int bar = 0; bar < bars; bar++)
        {
            int chord = bar % 4;
            float barStart = bar * 4 * Beat;

            for (int e = 0; e < 8; e++)
            {
                float t = barStart + e * Beat / 2;
                Note(data, t, 0.6f, Freq(Chords[chord][Arp[e]]), 0.13f, 0.22f, 0.35f);
                Shaker(data, t + Beat / 4, e % 2 == 0 ? 0.03f : 0.018f, rng);
            }
            Note(data, barStart, 1.2f, Freq(Bass[chord]), 0.22f, 0.45f, 0.1f);
            Note(data, barStart + 2 * Beat, 1.2f, Freq(Bass[chord]), 0.18f, 0.45f, 0.1f);
            foreach (var n in Chords[chord])
                Pad(data, barStart, 4 * Beat, Freq(n), 0.025f);

            if (bar >= 4)
                foreach (var m in Melody[bar - 4])
                    Note(data, barStart + m[0] * Beat, m[1] * Beat + 0.4f, Freq(m[2]), 0.16f, 0.3f, 0.12f, marimba: true);
        }

        // Gentle master limiter.
        for (int i = 0; i < length; i++) data[i] = (float)System.Math.Tanh(data[i] * 1.2f);

        return data;
    }

    public static AudioClip ToClip(float[] data)
    {
        var clip = AudioClip.Create("Music", data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // Plucked tone: fundamental + octave, exponential decay.
    static void Note(float[] data, float start, float dur, float f, float vol, float decay, float octave, bool marimba = false)
    {
        int s0 = (int)(start * Rate), n = (int)(dur * Rate);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float env = Mathf.Min(1, t * 200) * Mathf.Exp(-t / decay);
            float w = Mathf.Sin(2 * Mathf.PI * f * t) + octave * Mathf.Sin(4 * Mathf.PI * f * t);
            if (marimba) w += 0.25f * Mathf.Sin(8 * Mathf.PI * f * t) * Mathf.Exp(-t / 0.04f);
            data[(s0 + i) % data.Length] += w * env * vol;
        }
    }

    // Soft sustained chord tone with slow attack and release.
    static void Pad(float[] data, float start, float dur, float f, float vol)
    {
        int s0 = (int)(start * Rate), n = (int)(dur * Rate);
        for (int i = 0; i < n; i++)
        {
            float k = i / (float)n;
            float env = Mathf.Sin(Mathf.PI * k);
            float t = i / (float)Rate;
            data[(s0 + i) % data.Length] += (Mathf.Sin(2 * Mathf.PI * f * t) + 0.3f * Mathf.Sin(2 * Mathf.PI * f * 1.003f * t)) * env * vol;
        }
    }

    // Short high-passed noise burst.
    static void Shaker(float[] data, float start, float vol, System.Random rng)
    {
        int s0 = (int)(start * Rate), n = (int)(0.04f * Rate);
        float prev = 0;
        for (int i = 0; i < n; i++)
        {
            float noise = (float)rng.NextDouble() * 2 - 1;
            float hp = noise - prev;
            prev = noise;
            data[(s0 + i) % data.Length] += hp * vol * (1 - i / (float)n);
        }
    }
}
