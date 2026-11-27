using System;
using System.Globalization;

namespace _DL.PlaySafe
{
    /// <summary>
    /// The pure clip handling behind PlaySafeManager.EstimateSpeakerAgeAsync: can this clip be sent,
    /// and the mono samples the Opus encoder takes. Kept free of UnityEngine so it can be unit-tested.
    /// </summary>
    internal static class PlaySafeSpeakerAgeClip
    {
        // The API refuses longer clips (SPEAKER_AGE_MAX_SECONDS in dl-voice-ai), so don't encode and upload one.
        internal const int MaxSeconds = 15;
        internal const int MinSampleRate = 8000;
        internal const int MaxSampleRate = 48000;
        // Unity's widest speaker mode (7.1). A larger count is a caller mistake, and the copy of the clip grows with it.
        internal const int MaxChannels = 8;

        /// <summary>Returns what is wrong with the clip, or null when it can be sent.</summary>
        internal static string Validate(float[] samples, int sampleRate, int channels)
        {
            if (samples == null || samples.Length == 0)
                return "samples is empty.";

            if (channels < 1 || channels > MaxChannels)
                return $"channels must be from 1 to {MaxChannels} (got {channels}).";

            if (sampleRate < MinSampleRate || sampleRate > MaxSampleRate)
                return $"unsupported sampleRate {sampleRate}Hz. Expected {MinSampleRate} to {MaxSampleRate}.";

            long frames = samples.Length / channels;
            if (frames == 0)
                return $"samples holds less than one frame of {channels} channels.";

            if (frames > (long)MaxSeconds * sampleRate)
                return $"the clip is longer than {MaxSeconds} seconds.";

            return null;
        }

        /// <summary>
        /// Seconds to hold further calls after a failed request, or 0 when the status asks for no pause.
        /// 429 is the product's per-minute limit; 503 comes with the service's own Retry-After.
        /// </summary>
        internal static float RetryDelaySeconds(long statusCode, string retryAfter)
        {
            if (statusCode != 429 && statusCode != 503)
                return 0f;

            if (!float.TryParse(retryAfter, NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds) || !(seconds > 0f))
                return statusCode == 429 ? 60f : 5f;

            return Math.Min(seconds, 300f);
        }

        /// <summary>The rate a clip is encoded at: the lowest one the Opus encoder takes that loses nothing.</summary>
        internal static int EncodeRate(int sampleRate)
        {
            if (sampleRate <= 16000) return 16000;
            if (sampleRate <= 24000) return 24000;
            return 48000;
        }

        /// <summary>Mixes a validated clip down to mono and brings it to <paramref name="encodeRate"/>.</summary>
        internal static float[] ToEncoderInput(float[] samples, int sampleRate, int channels, out int encodeRate)
        {
            encodeRate = EncodeRate(sampleRate);
            return Resample(PlaySafeOpusEncoder.DownmixToMono(samples, channels), sampleRate, encodeRate);
        }

        // Linear interpolation. It only ever raises the rate (see EncodeRate), so it needs no low-pass filter.
        internal static float[] Resample(float[] mono, int fromRate, int toRate)
        {
            if (fromRate == toRate)
                return mono;

            float[] resampled = new float[(int)((long)mono.Length * toRate / fromRate)];
            double step = (double)fromRate / toRate;
            int last = mono.Length - 1;
            for (int i = 0; i < resampled.Length; i++)
            {
                double position = i * step;
                int index = (int)position;
                if (index >= last)
                {
                    resampled[i] = mono[last];
                    continue;
                }

                float fraction = (float)(position - index);
                resampled[i] = mono[index] + (mono[index + 1] - mono[index]) * fraction;
            }
            return resampled;
        }
    }
}
