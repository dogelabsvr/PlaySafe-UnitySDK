using System;

namespace _DL.PlaySafe
{
    internal readonly struct EchoSpan
    {
        internal readonly int MicRawStart;
        internal readonly int MicRawEnd;
        internal readonly long RefStart;
        internal readonly long RefEnd;

        internal EchoSpan(int micRawStart, int micRawEnd, long refStart, long refEnd)
        {
            MicRawStart = micRawStart;
            MicRawEnd = micRawEnd;
            RefStart = refStart;
            RefEnd = refEnd;
        }
    }

    internal readonly struct RefSlice
    {
        internal readonly float[] Ref;      // null => reference unavailable for this span
        internal readonly int RefLen;
        internal readonly int MicStart16k;
        internal readonly int MicLen16k;
        internal readonly int LeadPad;      // reference samples preceding the span's wall-clock start

        internal RefSlice(float[] reference, int refLen, int micStart16k, int micLen16k, int leadPad)
        {
            Ref = reference;
            RefLen = refLen;
            MicStart16k = micStart16k;
            MicLen16k = micLen16k;
            LeadPad = leadPad;
        }
    }

    internal struct EchoAnalysisResult
    {
        internal bool ReferenceUsable;
        internal float EchoProbability;
        internal float EstimatedDelayMs;
        internal float MeanNcc;
        internal float LagStabilityScore;
        internal float PeakConfidence;
        internal float LocalSpeechEvidence;
        internal float ReferenceRms;
        internal float Coverage;
    }

    internal sealed class EchoParams
    {
        internal const int AnalysisRate = 16000;

        internal float FrameActiveRms = 0.003f;
        internal int NccFrameLen = 1600;
        internal int NccHop = 800;
        internal int DelayMinSamples = -1600;
        internal int DelayMaxSamples = 9600;
        internal int GccFftSize = 32768;
        internal int GccWindowLen = 16000;
        internal float MinJointActiveSeconds = 0.5f;
        internal int LagWindowLen = 4000;
        internal int LagSearchSamples = 256;
        internal int LagSearchStep = 2;
        internal float LagWindowMinNcc = 0.2f;
        internal float LagSpreadTolMs = 15f;
        internal float MinOverlapCoverage = 0.6f;
        internal float MinReferenceRms = 0.005f;
        internal float PeakConfLo = 2f;
        internal float PeakConfHi = 8f;
        internal float NccLo = 0.3f;
        internal float NccHi = 0.75f;
        internal float NccContestFloor = 0.35f;
    }
}
