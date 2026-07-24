using System;
using NUnit.Framework;
using _DL.PlaySafe;

namespace _DL.PlaySafe.Tests
{
    public class PlaySafeFFTTests
    {
        [Test]
        public void RoundTrip_RecoversInput()
        {
            var rng = new Random(42);
            const int n = 1024;
            var re = new float[n];
            var im = new float[n];
            var orig = new float[n];
            for (int i = 0; i < n; i++) { orig[i] = (float)(rng.NextDouble() * 2 - 1); re[i] = orig[i]; }
            var fft = new PlaySafeFFT(n);
            fft.Forward(re, im);
            fft.Inverse(re, im);
            for (int i = 0; i < n; i++)
            {
                Assert.That(re[i], Is.EqualTo(orig[i]).Within(1e-4f));
                Assert.That(im[i], Is.EqualTo(0f).Within(1e-4f));
            }
        }

        [Test]
        public void Forward_MatchesNaiveDft()
        {
            var rng = new Random(7);
            const int n = 64;
            var re = new float[n];
            var im = new float[n];
            for (int i = 0; i < n; i++) re[i] = (float)(rng.NextDouble() * 2 - 1);
            var expectedRe = new double[n];
            var expectedIm = new double[n];
            for (int k = 0; k < n; k++)
                for (int t = 0; t < n; t++)
                {
                    double a = -2.0 * Math.PI * k * t / n;
                    expectedRe[k] += re[t] * Math.Cos(a);
                    expectedIm[k] += re[t] * Math.Sin(a);
                }
            new PlaySafeFFT(n).Forward(re, im);
            for (int k = 0; k < n; k++)
            {
                Assert.That(re[k], Is.EqualTo((float)expectedRe[k]).Within(1e-3f));
                Assert.That(im[k], Is.EqualTo((float)expectedIm[k]).Within(1e-3f));
            }
        }

        [Test]
        public void Constructor_RejectsNonPowerOfTwo()
        {
            Assert.Throws<ArgumentException>(() => new PlaySafeFFT(1000));
        }
    }
}
