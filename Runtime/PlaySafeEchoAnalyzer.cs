using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace _DL.PlaySafe
{
    // Decides whether a recorded mic clip is an acoustic echo of what the game
    // just played through the headset. Pure detection, no cancellation: output
    // is a one-directional probability (high = echo evidence; low/unusable = no
    // information). AnalyzeCore is deterministic and Unity-free for testability.
    internal static class PlaySafeEchoAnalyzer
    {
        // Analysis scratch is reused between clips; clips arrive at least
        // RecordingDuration apart so the lock is effectively uncontended.
        private static readonly object ScratchLock = new object();
        private static PlaySafeFFT _fft;
        private static int _fftSize;
        private static float[] _reA, _imA, _reB, _imB;

        internal static Task<EchoAnalysisResult> AnalyzeAsync(
            float[] micMono, int micLen, int micRate, int channelCountRaw,
            EchoSpan[] spans, PlaySafeReferenceRing ring, EchoParams p)
        {
            // Snapshot on the caller's (main) thread, mirroring PlaySafeOpusEncoder.EncodeAsync.
            var owned = new float[micLen];
            Array.Copy(micMono, 0, owned, 0, micLen);
            return Task.Run(() => Analyze(owned, micLen, micRate, channelCountRaw, spans, ring, p));
        }

        private static EchoAnalysisResult Analyze(
            float[] micMono, int micLen, int micRate, int channelCountRaw,
            EchoSpan[] spans, PlaySafeReferenceRing ring, EchoParams p)
        {
            if (micRate <= 0 || channelCountRaw <= 0 || ring == null || spans == null || spans.Length == 0)
                return default;

            float[] mic16k;
            int micLen16k;
            if (micRate == EchoParams.AnalysisRate)
            {
                mic16k = micMono;
                micLen16k = micLen;
            }
            else
            {
                mic16k = new float[(int)((long)micLen * EchoParams.AnalysisRate / micRate) + 16];
                micLen16k = PlaySafeAudioResampler.ToMono16k(micMono, micLen, micRate, mic16k);
            }

            int tailPad = -p.DelayMinSamples;
            var slices = new RefSlice[spans.Length];
            for (int i = 0; i < spans.Length; i++)
            {
                EchoSpan span = spans[i];
                int micStart16k = (int)((long)(span.MicRawStart / channelCountRaw) * EchoParams.AnalysisRate / micRate);
                int micEnd16k = (int)((long)(span.MicRawEnd / channelCountRaw) * EchoParams.AnalysisRate / micRate);
                if (micEnd16k > micLen16k) micEnd16k = micLen16k;
                int micSpanLen = micEnd16k - micStart16k;
                int refLen = (int)(span.RefEnd - span.RefStart);
                if (micSpanLen <= 0 || refLen <= 0)
                {
                    slices[i] = new RefSlice(null, 0, micStart16k, Math.Max(0, micSpanLen), 0);
                    continue;
                }
                int leadPad = (int)Math.Min(p.DelayMaxSamples, span.RefStart);
                int totalLen = leadPad + refLen + tailPad;
                var shorts = new short[totalLen];
                if (!ring.TryCopyRange(span.RefStart - leadPad, totalLen, shorts, 0))
                {
                    slices[i] = new RefSlice(null, 0, micStart16k, micSpanLen, 0);
                    continue;
                }
                var reference = new float[totalLen];
                for (int s = 0; s < totalLen; s++) reference[s] = shorts[s] * (1f / 32768f);
                slices[i] = new RefSlice(reference, totalLen, micStart16k, micSpanLen, leadPad);
            }

            return AnalyzeCore(mic16k, micLen16k, slices, p);
        }

        internal static EchoAnalysisResult AnalyzeCore(float[] mic16k, int micLen16k, RefSlice[] slices, EchoParams p)
        {
            var result = default(EchoAnalysisResult);
            if (mic16k == null || micLen16k <= 0 || slices == null || slices.Length == 0)
                return result;

            long covered = 0;
            double refEnergy = 0;
            long refSamples = 0;
            foreach (RefSlice s in slices)
            {
                if (s.Ref == null) continue;
                covered += s.MicLen16k;
                for (int i = 0; i < s.RefLen; i++) refEnergy += (double)s.Ref[i] * s.Ref[i];
                refSamples += s.RefLen;
            }
            result.Coverage = (float)covered / micLen16k;
            result.ReferenceRms = refSamples > 0 ? (float)Math.Sqrt(refEnergy / refSamples) : 0f;
            if (result.Coverage < p.MinOverlapCoverage || result.ReferenceRms < p.MinReferenceRms)
                return result;

            result.ReferenceUsable = true;
            lock (ScratchLock)
            {
                int tau = EstimateDelay(mic16k, slices, p, out float peakConf);
                result.PeakConfidence = peakConf;
                result.EstimatedDelayMs = tau * 1000f / EchoParams.AnalysisRate;

                ScoreFrames(mic16k, micLen16k, slices, p, tau,
                    out float meanNccPos, out float micOnlyRatio, out float jointSeconds);
                result.MeanNcc = meanNccPos;
                result.LocalSpeechEvidence = micOnlyRatio;

                float lagStability = LagStability(mic16k, micLen16k, slices, p, tau);
                result.LagStabilityScore = lagStability;

                float jointCov = Clamp01(jointSeconds / p.MinJointActiveSeconds);
                float nccScore = Clamp01((meanNccPos - p.NccLo) / (p.NccHi - p.NccLo));
                result.EchoProbability = Clamp01(nccScore * lagStability * peakConf * jointCov * (1f - micOnlyRatio));
            }
            return result;
        }

        // GCC-PHAT over the highest joint-energy window: whitened cross-power
        // spectrum sharpens the correlation peak so one clip suffices for a
        // per-clip delay estimate (Quest capture offsets vary device-to-device).
        private static int EstimateDelay(float[] mic, RefSlice[] slices, EchoParams p, out float peakConf)
        {
            peakConf = 0f;
            int n = p.GccFftSize;
            EnsureScratch(n);

            int bestSlice = -1;
            int bestW0 = 0;
            int bestW = 0;
            double bestScore = 0;
            foreach (int candidateW in new[] { p.GccWindowLen, p.GccWindowLen / 2, p.GccWindowLen / 4 })
            {
                for (int si = 0; si < slices.Length; si++)
                {
                    RefSlice s = slices[si];
                    if (s.Ref == null || s.MicLen16k < candidateW) continue;
                    int mRange = s.LeadPad - p.DelayMinSamples;
                    if (candidateW + mRange > n) continue;
                    for (int w0 = 0; w0 + candidateW <= s.MicLen16k; w0 += candidateW / 2)
                    {
                        double micE = 0;
                        for (int k = 0; k < candidateW; k++)
                        {
                            float v = mic[s.MicStart16k + w0 + k];
                            micE += (double)v * v;
                        }
                        int refStart = w0;
                        int refLen = Math.Min(candidateW + mRange, s.RefLen - refStart);
                        if (refLen <= 0) continue;
                        double refE = 0;
                        for (int k = 0; k < refLen; k++)
                        {
                            float v = s.Ref[refStart + k];
                            refE += (double)v * v;
                        }
                        double score = micE * refE;
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestSlice = si;
                            bestW0 = w0;
                            bestW = candidateW;
                        }
                    }
                }
                if (bestSlice >= 0) break;
            }
            if (bestSlice < 0) return 0;

            RefSlice bs = slices[bestSlice];
            int lagRange = bs.LeadPad - p.DelayMinSamples;
            int mLo = Math.Max(0, bs.LeadPad - p.DelayMaxSamples);

            Array.Clear(_reA, 0, n);
            Array.Clear(_imA, 0, n);
            Array.Clear(_reB, 0, n);
            Array.Clear(_imB, 0, n);
            for (int k = 0; k < bestW; k++) _reA[k] = mic[bs.MicStart16k + bestW0 + k];
            int refCopyLen = Math.Min(bestW + lagRange, bs.RefLen - bestW0);
            for (int k = 0; k < refCopyLen; k++) _reB[k] = bs.Ref[bestW0 + k];

            _fft.Forward(_reA, _imA);
            _fft.Forward(_reB, _imB);
            for (int i = 0; i < n; i++)
            {
                // conj(MIC) * REF, then PHAT whitening (phase-only correlation)
                float re = _reA[i] * _reB[i] + _imA[i] * _imB[i];
                float im = _reA[i] * _imB[i] - _imA[i] * _reB[i];
                float mag = (float)Math.Sqrt(re * re + im * im) + 1e-12f;
                _reA[i] = re / mag;
                _imA[i] = im / mag;
            }
            _fft.Inverse(_reA, _imA);

            int mPeak = mLo;
            float peak = 0f;
            double sumAbs = 0;
            int count = 0;
            for (int m = mLo; m <= lagRange && m < n; m++)
            {
                float v = Math.Abs(_reA[m]);
                sumAbs += v;
                count++;
                if (v > peak)
                {
                    peak = v;
                    mPeak = m;
                }
            }
            if (count == 0) return 0;
            float mean = (float)(sumAbs / count) + 1e-12f;
            float ratio = peak / mean;
            peakConf = Clamp01((ratio - p.PeakConfLo) / (p.PeakConfHi - p.PeakConfLo));
            return bs.LeadPad - mPeak;
        }

        private static void ScoreFrames(
            float[] mic, int micLen16k, RefSlice[] slices, EchoParams p, int tau,
            out float meanNccPos, out float micOnlyRatio, out float jointSeconds)
        {
            int frameLen = p.NccFrameLen;
            double nccPosSum = 0;
            int jointFrames = 0;
            int micActiveFrames = 0;
            int micOnlyFrames = 0;
            int contestedFrames = 0;

            foreach (RefSlice s in slices)
            {
                if (s.Ref == null) continue;
                for (int local = 0; local + frameLen <= s.MicLen16k; local += p.NccHop)
                {
                    int t = s.MicStart16k + local;
                    if (t + frameLen > micLen16k) break;
                    int refIdx = local + s.LeadPad - tau;
                    if (refIdx < 0 || refIdx + frameLen > s.RefLen) continue;

                    float micRms = RmsOf(mic, t, frameLen);
                    if (micRms < p.FrameActiveRms) continue;
                    micActiveFrames++;

                    float refRms = RmsOf(s.Ref, refIdx, frameLen);
                    if (refRms < p.FrameActiveRms)
                    {
                        micOnlyFrames++;
                        continue;
                    }

                    double dot = 0;
                    for (int k = 0; k < frameLen; k++)
                        dot += (double)mic[t + k] * s.Ref[refIdx + k];
                    double denom = (double)micRms * refRms * frameLen + 1e-12;
                    float ncc = (float)(dot / denom);
                    if (ncc > 0f) nccPosSum += ncc;
                    if (ncc < p.NccContestFloor) contestedFrames++;
                    jointFrames++;
                }
            }

            meanNccPos = jointFrames > 0 ? (float)(nccPosSum / jointFrames) : 0f;
            // Local speech shows up two ways: the mic is active while the
            // reference is silent (micOnly), or it collapses the correlation of
            // otherwise-joint frames (contested). Either blocks a drop.
            float micOnly = micActiveFrames > 0 ? (float)micOnlyFrames / micActiveFrames : 0f;
            float contested = jointFrames > 0 ? (float)contestedFrames / jointFrames : 0f;
            micOnlyRatio = Math.Max(micOnly, contested);
            jointSeconds = jointFrames * (float)p.NccHop / EchoParams.AnalysisRate;
        }

        // True echo keeps one lag for the whole clip; coincidental correlation
        // wanders. Per-window argmax lags vote via their median absolute deviation.
        private static float LagStability(float[] mic, int micLen16k, RefSlice[] slices, EchoParams p, int tau)
        {
            var lags = new List<int>(16);
            int w = p.LagWindowLen;

            foreach (RefSlice s in slices)
            {
                if (s.Ref == null) continue;
                for (int local = 0; local + w <= s.MicLen16k; local += w)
                {
                    int t = s.MicStart16k + local;
                    if (t + w > micLen16k) break;
                    float micRms = RmsOf(mic, t, w);
                    if (micRms < p.FrameActiveRms) continue;

                    float bestNcc = 0f;
                    int bestD = 0;
                    for (int d = -p.LagSearchSamples; d <= p.LagSearchSamples; d += p.LagSearchStep)
                    {
                        int refIdx = local + s.LeadPad - (tau + d);
                        if (refIdx < 0 || refIdx + w > s.RefLen) continue;
                        double dot = 0, refE = 0;
                        for (int k = 0; k < w; k++)
                        {
                            float rv = s.Ref[refIdx + k];
                            dot += (double)mic[t + k] * rv;
                            refE += (double)rv * rv;
                        }
                        if (refE < 1e-9) continue;
                        float ncc = (float)(dot / (Math.Sqrt(refE) * micRms * Math.Sqrt(w) + 1e-12));
                        if (ncc > bestNcc)
                        {
                            bestNcc = ncc;
                            bestD = d;
                        }
                    }
                    if (bestNcc >= p.LagWindowMinNcc) lags.Add(bestD);
                }
            }

            if (lags.Count == 0) return 0f;
            if (lags.Count < 3) return 0.5f;
            lags.Sort();
            int median = lags[lags.Count / 2];
            var devs = new List<int>(lags.Count);
            foreach (int lag in lags) devs.Add(Math.Abs(lag - median));
            devs.Sort();
            float madMs = devs[devs.Count / 2] * 1000f / EchoParams.AnalysisRate;
            return Clamp01(1f - madMs / p.LagSpreadTolMs);
        }

        private static void EnsureScratch(int n)
        {
            if (_fftSize == n && _fft != null) return;
            _fft = new PlaySafeFFT(n);
            _fftSize = n;
            _reA = new float[n];
            _imA = new float[n];
            _reB = new float[n];
            _imB = new float[n];
        }

        private static float RmsOf(float[] x, int start, int len)
        {
            double e = 0;
            for (int i = 0; i < len; i++)
            {
                float v = x[start + i];
                e += (double)v * v;
            }
            return (float)Math.Sqrt(e / len);
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
