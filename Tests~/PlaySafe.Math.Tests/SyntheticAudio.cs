using System;

namespace _DL.PlaySafe.Tests
{
    internal static class SyntheticAudio
    {
        // Low-passed noise with a syllabic (4 Hz) envelope and random word gaps:
        // crude but has the broadband + modulation structure NCC/GCC-PHAT need.
        internal static float[] Speech(int len, int seed, float level)
        {
            var rng = new Random(seed);
            var x = new float[len];
            float lp = 0f;
            bool gate = true;
            int gateLeft = 6400;
            double phase = rng.NextDouble() * Math.PI * 2;
            for (int i = 0; i < len; i++)
            {
                if (--gateLeft <= 0)
                {
                    gate = rng.NextDouble() > 0.3;
                    gateLeft = 3200 + rng.Next(6400);
                }
                float white = (float)(rng.NextDouble() * 2 - 1);
                lp = 0.82f * lp + 0.18f * white;
                float env = 0.55f + 0.45f * (float)Math.Sin(2 * Math.PI * 4.0 * i / 16000.0 + phase);
                x[i] = gate ? level * lp * env : 0f;
            }
            return x;
        }

        internal static float[] DelayScaleAdd(float[] src, int delay, float gain, float[] addTo)
        {
            var y = addTo ?? new float[src.Length];
            for (int i = delay; i < src.Length; i++) y[i] += gain * src[i - delay];
            return y;
        }

        internal static void AddNoise(float[] x, int seed, float level)
        {
            var rng = new Random(seed);
            for (int i = 0; i < x.Length; i++) x[i] += level * (float)(rng.NextDouble() * 2 - 1);
        }

        // Builds a single full-coverage RefSlice from a reference signal, mimicking
        // what AnalyzeAsync assembles: leadPad reference samples precede the span.
        internal static _DL.PlaySafe.RefSlice Slice(float[] reference, int micLen, int leadPad)
        {
            var buf = new float[leadPad + reference.Length + 1600];
            Array.Copy(reference, 0, buf, leadPad, reference.Length);
            return new _DL.PlaySafe.RefSlice(buf, buf.Length, 0, micLen, leadPad);
        }
    }
}
