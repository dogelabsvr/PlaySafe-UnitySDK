using UnityEngine;

namespace _DL.PlaySafe
{
    // Hidden component added at runtime to the AudioListener's GameObject.
    // Records the final output mix (everything the player hears through Unity)
    // into the reference ring at 16 kHz mono. OnAudioFilterRead runs on Unity's
    // audio thread: no allocations, no Unity API calls, and the buffer passed
    // through is never modified, so game audio is untouched.
    internal sealed class PlaySafeOutputTap : MonoBehaviour
    {
        private PlaySafeReferenceRing _ring;
        private int _decimFactor; // 0 => non-integer output rate: tap stays silent (fail-safe)
        private float _acc;
        private int _phase;
        private volatile bool _bound;

        internal void Bind(PlaySafeReferenceRing ring, int outputSampleRate)
        {
            _bound = false;
            _ring = ring;
            _decimFactor = (outputSampleRate > 0 && outputSampleRate % EchoParams.AnalysisRate == 0)
                ? outputSampleRate / EchoParams.AnalysisRate
                : 0;
            _acc = 0f;
            _phase = 0;
            _bound = ring != null && _decimFactor > 0;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (!_bound || channels <= 0) return;
            PlaySafeReferenceRing ring = _ring;
            if (ring == null) return;

            int frames = data.Length / channels;
            float acc = _acc;
            int phase = _phase;
            int factor = _decimFactor;
            for (int f = 0; f < frames; f++)
            {
                int baseIdx = f * channels;
                float mono = 0f;
                for (int c = 0; c < channels; c++) mono += data[baseIdx + c];
                acc += mono / channels;
                if (++phase >= factor)
                {
                    float v = acc / factor;
                    acc = 0f;
                    phase = 0;
                    int s = (int)(v * 32767f);
                    if (s > short.MaxValue) s = short.MaxValue;
                    else if (s < short.MinValue) s = short.MinValue;
                    ring.WriteMonoSample((short)s);
                }
            }
            _acc = acc;
            _phase = phase;
            ring.PublishFrame();
        }
    }
}
