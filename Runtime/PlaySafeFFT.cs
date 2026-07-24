using System;

namespace _DL.PlaySafe
{
    // In-place iterative radix-2 FFT with precomputed tables. Sized once and
    // reused by the echo analyzer on its worker thread; not thread-safe per
    // instance and never touched by the audio or main thread.
    internal sealed class PlaySafeFFT
    {
        private readonly int _n;
        private readonly int[] _bitrev;
        private readonly float[] _cos;
        private readonly float[] _sin;

        internal PlaySafeFFT(int n)
        {
            if (n < 2 || (n & (n - 1)) != 0)
                throw new ArgumentException("FFT size must be a power of two", nameof(n));
            _n = n;
            int bits = 0;
            for (int t = n; t > 1; t >>= 1) bits++;
            _bitrev = new int[n];
            for (int i = 0; i < n; i++)
            {
                int r = 0;
                for (int b = 0; b < bits; b++) r |= ((i >> b) & 1) << (bits - 1 - b);
                _bitrev[i] = r;
            }
            _cos = new float[n / 2];
            _sin = new float[n / 2];
            for (int i = 0; i < n / 2; i++)
            {
                double a = -2.0 * Math.PI * i / n;
                _cos[i] = (float)Math.Cos(a);
                _sin[i] = (float)Math.Sin(a);
            }
        }

        internal void Forward(float[] re, float[] im) => Transform(re, im, false);

        internal void Inverse(float[] re, float[] im)
        {
            Transform(re, im, true);
            float s = 1f / _n;
            for (int i = 0; i < _n; i++) { re[i] *= s; im[i] *= s; }
        }

        private void Transform(float[] re, float[] im, bool inverse)
        {
            for (int i = 0; i < _n; i++)
            {
                int j = _bitrev[i];
                if (j > i)
                {
                    float tr = re[i]; re[i] = re[j]; re[j] = tr;
                    float ti = im[i]; im[i] = im[j]; im[j] = ti;
                }
            }
            for (int len = 2; len <= _n; len <<= 1)
            {
                int half = len >> 1;
                int step = _n / len;
                for (int i = 0; i < _n; i += len)
                {
                    for (int k = 0; k < half; k++)
                    {
                        int tw = k * step;
                        float wr = _cos[tw];
                        float wi = inverse ? -_sin[tw] : _sin[tw];
                        int a = i + k;
                        int b = a + half;
                        float xr = re[b] * wr - im[b] * wi;
                        float xi = re[b] * wi + im[b] * wr;
                        re[b] = re[a] - xr;
                        im[b] = im[a] - xi;
                        re[a] += xr;
                        im[a] += xi;
                    }
                }
            }
        }
    }
}
