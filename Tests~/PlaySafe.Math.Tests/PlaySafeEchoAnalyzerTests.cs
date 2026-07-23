using System;
using NUnit.Framework;
using _DL.PlaySafe;

namespace _DL.PlaySafe.Tests
{
    public class PlaySafeEchoAnalyzerTests
    {
        private const int Len = 160000; // 10 s @ 16 kHz

        private static EchoAnalysisResult Run(float[] mic, float[] reference, EchoParams p = null)
        {
            p = p ?? new EchoParams();
            var slice = SyntheticAudio.Slice(reference, Len, p.DelayMaxSamples);
            return PlaySafeEchoAnalyzer.AnalyzeCore(mic, Len, new[] { slice }, p);
        }

        [Test]
        public void PureEcho_HighProbability_AndDelayRecovered()
        {
            var reference = SyntheticAudio.Speech(Len, 1, 0.5f);
            var mic = SyntheticAudio.DelayScaleAdd(reference, 800, 0.5f, new float[Len]);
            SyntheticAudio.AddNoise(mic, 2, 0.005f);
            var r = Run(mic, reference);
            Assert.IsTrue(r.ReferenceUsable);
            Assert.GreaterOrEqual(r.EchoProbability, 0.8f, Describe(r));
            Assert.That(r.EstimatedDelayMs, Is.EqualTo(50f).Within(4f), Describe(r));
            Assert.LessOrEqual(r.LocalSpeechEvidence, 0.1f, Describe(r));
        }

        [Test]
        public void IndependentSpeech_LowProbability()
        {
            var reference = SyntheticAudio.Speech(Len, 3, 0.5f);
            var mic = SyntheticAudio.Speech(Len, 99, 0.4f);
            var r = Run(mic, reference);
            Assert.IsTrue(r.ReferenceUsable);
            Assert.LessOrEqual(r.EchoProbability, 0.2f, Describe(r));
        }

        [Test]
        public void DoubleTalk_MidOrLow_WithLocalSpeechEvidence()
        {
            var reference = SyntheticAudio.Speech(Len, 5, 0.5f);
            var mic = SyntheticAudio.Speech(Len, 77, 0.35f);
            mic = SyntheticAudio.DelayScaleAdd(reference, 800, 0.4f, mic);
            var r = Run(mic, reference);
            Assert.IsTrue(r.ReferenceUsable);
            Assert.LessOrEqual(r.EchoProbability, 0.6f, Describe(r));
            Assert.GreaterOrEqual(r.LocalSpeechEvidence, 0.02f, Describe(r));
        }

        [Test]
        public void SilentReference_Unusable()
        {
            var mic = SyntheticAudio.Speech(Len, 8, 0.4f);
            var reference = new float[Len];
            var r = Run(mic, reference);
            Assert.IsFalse(r.ReferenceUsable);
        }

        [Test]
        public void NoSlices_Unusable()
        {
            var mic = SyntheticAudio.Speech(Len, 8, 0.4f);
            var r = PlaySafeEchoAnalyzer.AnalyzeCore(mic, Len, Array.Empty<RefSlice>(), new EchoParams());
            Assert.IsFalse(r.ReferenceUsable);
        }

        [TestCase(0.05f)]
        [TestCase(0.5f)]
        [TestCase(4f)]
        public void GainInvariance_EchoStillDetected(float gain)
        {
            var reference = SyntheticAudio.Speech(Len, 11, 0.5f);
            var mic = SyntheticAudio.DelayScaleAdd(reference, 1600, gain, new float[Len]);
            SyntheticAudio.AddNoise(mic, 12, 0.002f * gain);
            var r = Run(mic, reference);
            Assert.GreaterOrEqual(r.EchoProbability, 0.75f, "gain " + gain + " " + Describe(r));
        }

        [Test]
        public void SoftClippedEcho_StillDetected()
        {
            var reference = SyntheticAudio.Speech(Len, 13, 0.6f);
            var mic = new float[Len];
            for (int i = 1600; i < Len; i++)
                mic[i] = (float)Math.Tanh(3.0 * reference[i - 1600]) * 0.4f;
            var r = Run(mic, reference);
            Assert.GreaterOrEqual(r.EchoProbability, 0.6f, Describe(r));
        }

        [Test]
        public void Performance_TenSecondClip_UnderBudget()
        {
            var reference = SyntheticAudio.Speech(Len, 21, 0.5f);
            var mic = SyntheticAudio.DelayScaleAdd(reference, 800, 0.5f, new float[Len]);
            var p = new EchoParams();
            var slice = SyntheticAudio.Slice(reference, Len, p.DelayMaxSamples);
            PlaySafeEchoAnalyzer.AnalyzeCore(mic, Len, new[] { slice }, p); // warm scratch
            var sw = System.Diagnostics.Stopwatch.StartNew();
            PlaySafeEchoAnalyzer.AnalyzeCore(mic, Len, new[] { slice }, p);
            sw.Stop();
            Assert.Less(sw.ElapsedMilliseconds, 1500, "analysis too slow: " + sw.ElapsedMilliseconds + "ms");
        }

        private static string Describe(EchoAnalysisResult r)
        {
            return $"[p={r.EchoProbability:F2} ncc={r.MeanNcc:F2} lag={r.LagStabilityScore:F2} " +
                   $"peak={r.PeakConfidence:F2} local={r.LocalSpeechEvidence:F2} delay={r.EstimatedDelayMs:F0}ms " +
                   $"cov={r.Coverage:F2} refRms={r.ReferenceRms:F3}]";
        }
    }
}
