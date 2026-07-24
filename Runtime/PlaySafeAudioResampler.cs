using System;
using Concentus;

namespace _DL.PlaySafe
{
    // Converts mono float audio of arbitrary rate into the 16 kHz analysis
    // domain. Integer ratios (48 kHz Quest output, 48 kHz Photon mics) use a
    // box-average decimator; the box filter's aliasing is acceptable because
    // the result is only correlated, never played back or transcribed.
    // Non-integer ratios (24 kHz Photon default) use the managed SpeexResampler
    // already shipped inside Concentus.dll.
    internal static class PlaySafeAudioResampler
    {
        [ThreadStatic] private static IResampler _resampler;
        [ThreadStatic] private static int _resamplerInRate;

        internal static int Decimate(float[] src, int srcLen, int factor, float[] dst)
        {
            int outLen = srcLen / factor;
            for (int o = 0, i = 0; o < outLen; o++, i += factor)
            {
                float acc = 0f;
                for (int k = 0; k < factor; k++) acc += src[i + k];
                dst[o] = acc / factor;
            }
            return outLen;
        }

        internal static int ToMono16k(float[] src, int srcLen, int srcRate, float[] dst)
        {
            if (srcRate == EchoParams.AnalysisRate)
            {
                Array.Copy(src, dst, srcLen);
                return srcLen;
            }
            if (srcRate % EchoParams.AnalysisRate == 0)
                return Decimate(src, srcLen, srcRate / EchoParams.AnalysisRate, dst);

            if (_resampler == null || _resamplerInRate != srcRate)
            {
                _resampler = ResamplerFactory.CreateResampler(1, srcRate, EchoParams.AnalysisRate, 3, null);
                _resamplerInRate = srcRate;
            }
            _resampler.ResetMem();

            int produced = 0;
            int consumed = 0;
            while (consumed < srcLen && produced < dst.Length)
            {
                int inLen = srcLen - consumed;
                int outLen = dst.Length - produced;
                _resampler.Process(0,
                    src.AsSpan(consumed, inLen), ref inLen,
                    dst.AsSpan(produced, outLen), ref outLen);
                if (inLen <= 0 && outLen <= 0) break;
                consumed += inLen;
                produced += outLen;
            }
            return produced;
        }
    }
}
