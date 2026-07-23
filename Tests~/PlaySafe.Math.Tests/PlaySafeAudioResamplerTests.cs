using System;
using NUnit.Framework;
using _DL.PlaySafe;

namespace _DL.PlaySafe.Tests
{
    public class PlaySafeAudioResamplerTests
    {
        private static float[] Sine(int len, int rate, float hz, float amp)
        {
            var x = new float[len];
            for (int i = 0; i < len; i++)
                x[i] = amp * (float)Math.Sin(2.0 * Math.PI * hz * i / rate);
            return x;
        }

        private static float Rms(float[] x, int len)
        {
            double e = 0;
            for (int i = 0; i < len; i++) e += (double)x[i] * x[i];
            return (float)Math.Sqrt(e / len);
        }

        [Test]
        public void Decimate48kTo16k_PreservesLengthAndEnergy()
        {
            var src = Sine(48000, 48000, 440f, 0.5f);
            var dst = new float[16000];
            int outLen = PlaySafeAudioResampler.ToMono16k(src, src.Length, 48000, dst);
            Assert.AreEqual(16000, outLen);
            Assert.That(Rms(dst, outLen), Is.EqualTo(Rms(src, src.Length)).Within(0.05f));
        }

        [Test]
        public void Passthrough16k_CopiesExactly()
        {
            var src = Sine(8000, 16000, 300f, 0.4f);
            var dst = new float[8000];
            int outLen = PlaySafeAudioResampler.ToMono16k(src, src.Length, 16000, dst);
            Assert.AreEqual(8000, outLen);
            Assert.AreEqual(src[1234], dst[1234]);
        }

        [Test]
        public void Resample24kTo16k_ProducesTwoThirdsLengthAndKeepsTone()
        {
            var src = Sine(24000, 24000, 440f, 0.5f);
            var dst = new float[18000];
            int outLen = PlaySafeAudioResampler.ToMono16k(src, src.Length, 24000, dst);
            Assert.That(outLen, Is.EqualTo(16000).Within(64));
            Assert.That(Rms(dst, outLen), Is.EqualTo(Rms(src, src.Length)).Within(0.08f));
        }

        [Test]
        public void Decimate_ByThree_AveragesGroups()
        {
            var src = new float[] { 1f, 2f, 3f, 4f, 5f, 6f };
            var dst = new float[2];
            int outLen = PlaySafeAudioResampler.Decimate(src, 6, 3, dst);
            Assert.AreEqual(2, outLen);
            Assert.AreEqual(2f, dst[0]);
            Assert.AreEqual(5f, dst[1]);
        }
    }
}
