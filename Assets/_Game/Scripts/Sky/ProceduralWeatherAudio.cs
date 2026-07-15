using UnityEngine;

namespace IslandGame.Sky
{
    /// <summary>
    /// Runtime-synthesized weather audio — the same "generate, don't import"
    /// philosophy as the TextureSynth pipeline, applied to sound: no binary
    /// assets, deterministic per seed, tweak the math and rerun. Clips are
    /// built once at startup (a few hundred ms of math) and reused.
    ///
    ///   RAIN LOOP — low-passed white noise, seamlessly loopable via a
    ///   crossfaded tail. Played on a looping AudioSource whose volume
    ///   follows Precipitation01.
    ///
    ///   THUNDER — an initial white-noise crack into a long brown-noise
    ///   rumble with slow amplitude undulation and an exponential die-off.
    ///   Distance is faked by the caller (delay after the flash + pitch).
    /// </summary>
    public static class ProceduralWeatherAudio
    {
        private const int SampleRate = 44100;

        /// <summary>Seamless 3-second rain bed. Volume, not content, carries intensity.</summary>
        public static AudioClip CreateRainLoop(int seed)
        {
            const float seconds = 3f;
            int sampleCount = (int)(SampleRate * seconds);
            var samples = new float[sampleCount];
            var random = new System.Random(seed);

            // Low-passed white noise ≈ steady rain hiss. Two cascaded
            // one-pole filters take the digital edge off.
            float lp1 = 0f, lp2 = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                float white = (float)(random.NextDouble() * 2.0 - 1.0);
                lp1 += (white - lp1) * 0.22f;
                lp2 += (lp1 - lp2) * 0.30f;
                samples[i] = lp2 * 1.6f;
            }

            // Loop seam: crossfade the first 0.15 s over the last 0.15 s.
            int fade = (int)(SampleRate * 0.15f);
            for (int i = 0; i < fade; i++)
            {
                float t = (float)i / fade;
                int tail = sampleCount - fade + i;
                samples[tail] = samples[tail] * (1f - t) + samples[i] * t;
            }

            var clip = AudioClip.Create("RainLoop (generated)", sampleCount, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>One thunder roll, ~3.5 s. Vary AudioSource.pitch per play so no two strikes sound alike.</summary>
        public static AudioClip CreateThunder(int seed)
        {
            const float seconds = 3.5f;
            int sampleCount = (int)(SampleRate * seconds);
            var samples = new float[sampleCount];
            var random = new System.Random(seed);

            float brown = 0f;
            float peak = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                float time = (float)i / SampleRate;
                float white = (float)(random.NextDouble() * 2.0 - 1.0);

                // Brown noise (integrated white, leaky) = the deep rumble body.
                brown = (brown + white * 0.055f) * 0.995f;

                // Slow undulation so the roll tumbles instead of just fading.
                float undulation = 0.6f + 0.4f * Mathf.Sin(time * 7.3f + Mathf.Sin(time * 2.1f) * 2f);

                // Exponential die-off over the roll's length.
                float envelope = Mathf.Exp(-time * 1.35f) * undulation;

                float sample = brown * 8f * envelope;

                // The first ~80 ms is the crack: raw white noise spike on top.
                if (time < 0.08f)
                    sample += white * (1f - time / 0.08f) * 0.9f;

                samples[i] = sample;
                peak = Mathf.Max(peak, Mathf.Abs(sample));
            }

            // Normalize to a consistent loudness; the source's volume scales it.
            if (peak > 0.0001f)
            {
                float gain = 0.85f / peak;
                for (int i = 0; i < sampleCount; i++)
                    samples[i] *= gain;
            }

            var clip = AudioClip.Create("Thunder (generated)", sampleCount, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
