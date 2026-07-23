using System;
using NUnit.Framework;
using _DL.PlaySafe;

namespace _DL.PlaySafe.Tests
{
    public class PlaySafeEchoAsyncTests
    {
        private static PlaySafeReferenceRing RingFrom(float[] signal, int capacity)
        {
            var ring = new PlaySafeReferenceRing(capacity);
            for (int i = 0; i < signal.Length; i++)
            {
                int s = (int)(signal[i] * 32767f);
                if (s > short.MaxValue) s = short.MaxValue;
                else if (s < short.MinValue) s = short.MinValue;
                ring.WriteMonoSample((short)s);
                if ((i & 1023) == 0) ring.PublishFrame();
            }
            ring.PublishFrame();
            return ring;
        }

        [Test]
        public void TwoSpansWithGap_EchoDetected()
        {
            var refSignal = SyntheticAudio.Speech(192000, 31, 0.5f); // 12 s
            const int delay = 800;
            var mic = new float[128000]; // 8 s: two 4 s spans separated by a 2 s pause
            for (int k = 0; k < 64000; k++) mic[k] = 0.5f * refSignal[16000 + k - delay];
            for (int k = 0; k < 64000; k++) mic[64000 + k] = 0.5f * refSignal[112000 + k - delay];
            var spans = new[]
            {
                new EchoSpan(0, 64000, 16000, 80000),
                new EchoSpan(64000, 128000, 112000, 176000),
            };
            var ring = RingFrom(refSignal, 1 << 18);
            var r = PlaySafeEchoAnalyzer.AnalyzeAsync(mic, mic.Length, 16000, 1, spans, ring, new EchoParams()).Result;
            Assert.IsTrue(r.ReferenceUsable);
            Assert.GreaterOrEqual(r.EchoProbability, 0.8f, Describe(r));
            Assert.That(r.EstimatedDelayMs, Is.EqualTo(50f).Within(4f), Describe(r));
        }

        [Test]
        public void FortyEightKhzMic_ResampledAndDetected()
        {
            var refSignal = SyntheticAudio.Speech(96000, 41, 0.5f); // 6 s
            const int delay = 800;
            var mic16 = new float[64000]; // 4 s span starting at ref count 16000
            for (int k = 0; k < 64000; k++) mic16[k] = 0.5f * refSignal[16000 + k - delay];
            var mic48 = new float[192000];
            for (int i = 0; i < 64000; i++)
            {
                mic48[i * 3] = mic16[i];
                mic48[i * 3 + 1] = mic16[i];
                mic48[i * 3 + 2] = mic16[i];
            }
            var spans = new[] { new EchoSpan(0, 192000, 16000, 80000) };
            var ring = RingFrom(refSignal, 1 << 17);
            var r = PlaySafeEchoAnalyzer.AnalyzeAsync(mic48, mic48.Length, 48000, 1, spans, ring, new EchoParams()).Result;
            Assert.IsTrue(r.ReferenceUsable, Describe(r));
            Assert.GreaterOrEqual(r.EchoProbability, 0.7f, Describe(r));
        }

        [Test]
        public void SpanOlderThanRingCapacity_Unusable()
        {
            var refSignal = SyntheticAudio.Speech(200000, 51, 0.5f);
            var mic = SyntheticAudio.Speech(64000, 52, 0.4f);
            var spans = new[] { new EchoSpan(0, 64000, 1000, 65000) }; // long overwritten
            var ring = RingFrom(refSignal, 1 << 16); // 65536 capacity, 200000 written
            var r = PlaySafeEchoAnalyzer.AnalyzeAsync(mic, mic.Length, 16000, 1, spans, ring, new EchoParams()).Result;
            Assert.IsFalse(r.ReferenceUsable);
        }

        [Test]
        public void NoSpans_Unusable()
        {
            var mic = SyntheticAudio.Speech(64000, 53, 0.4f);
            var ring = RingFrom(SyntheticAudio.Speech(64000, 54, 0.4f), 1 << 17);
            var r = PlaySafeEchoAnalyzer.AnalyzeAsync(mic, mic.Length, 16000, 1, Array.Empty<EchoSpan>(), ring, new EchoParams()).Result;
            Assert.IsFalse(r.ReferenceUsable);
        }

        private static string Describe(EchoAnalysisResult r)
        {
            return $"[p={r.EchoProbability:F2} ncc={r.MeanNcc:F2} lag={r.LagStabilityScore:F2} " +
                   $"peak={r.PeakConfidence:F2} local={r.LocalSpeechEvidence:F2} delay={r.EstimatedDelayMs:F0}ms " +
                   $"cov={r.Coverage:F2} refRms={r.ReferenceRms:F3}]";
        }
    }
}
