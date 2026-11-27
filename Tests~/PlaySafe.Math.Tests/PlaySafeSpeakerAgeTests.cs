using System;
using Newtonsoft.Json;
using NUnit.Framework;
using _DL.PlaySafe;

namespace _DL.PlaySafe.Tests
{
    public class PlaySafeSpeakerAgeClipTests
    {
        private static float[] Seconds(double seconds, int sampleRate, int channels = 1) =>
            new float[(int)(seconds * sampleRate) * channels];

        [TestCase(8000)]
        [TestCase(16000)]
        [TestCase(22050)]
        [TestCase(24000)]
        [TestCase(44100)]
        [TestCase(48000)]
        public void RatesFromEightToFortyEightKilohertz_AreAccepted(int sampleRate)
        {
            Assert.IsNull(PlaySafeSpeakerAgeClip.Validate(Seconds(10, sampleRate), sampleRate, 1));
        }

        [TestCase(0)]
        [TestCase(-16000)]
        [TestCase(7999)]
        [TestCase(48001)]
        [TestCase(96000)]
        public void OtherRates_AreRefused(int sampleRate)
        {
            StringAssert.Contains("unsupported sampleRate", PlaySafeSpeakerAgeClip.Validate(new float[16000], sampleRate, 1));
        }

        [Test]
        public void NullOrEmptySamples_AreRefused()
        {
            StringAssert.Contains("empty", PlaySafeSpeakerAgeClip.Validate(null, 16000, 1));
            StringAssert.Contains("empty", PlaySafeSpeakerAgeClip.Validate(Array.Empty<float>(), 16000, 1));
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(9)]
        [TestCase(64)]
        public void ChannelsOutsideOneToEight_AreRefused(int channels)
        {
            StringAssert.Contains("channels must be from 1 to 8", PlaySafeSpeakerAgeClip.Validate(new float[16000], 16000, channels));
        }

        [Test]
        public void EightChannels_AreAccepted()
        {
            Assert.IsNull(PlaySafeSpeakerAgeClip.Validate(Seconds(1, 48000, 8), 48000, 8));
        }

        [Test]
        public void FewerSamplesThanChannels_IsRefused()
        {
            StringAssert.Contains("less than one frame", PlaySafeSpeakerAgeClip.Validate(new float[1], 16000, 2));
        }

        [Test]
        public void ExactlyFifteenSeconds_IsAccepted_OneSampleMoreIsNot()
        {
            Assert.IsNull(PlaySafeSpeakerAgeClip.Validate(new float[15 * 48000], 48000, 1));
            StringAssert.Contains("longer than 15 seconds", PlaySafeSpeakerAgeClip.Validate(new float[15 * 48000 + 1], 48000, 1));
        }

        [Test]
        public void Duration_CountsFramesNotSamples()
        {
            Assert.IsNull(PlaySafeSpeakerAgeClip.Validate(Seconds(15, 48000, 2), 48000, 2));
            StringAssert.Contains("longer than 15 seconds", PlaySafeSpeakerAgeClip.Validate(Seconds(16, 48000, 2), 48000, 2));
        }
    }

    public class PlaySafeSpeakerAgeRetryTests
    {
        [TestCase(200, null)]
        [TestCase(400, "5")]
        [TestCase(404, null)]
        [TestCase(413, null)]
        [TestCase(500, "5")]
        [TestCase(0, null)]
        public void OtherStatuses_AskForNoPause(long status, string retryAfter)
        {
            Assert.AreEqual(0f, PlaySafeSpeakerAgeClip.RetryDelaySeconds(status, retryAfter));
        }

        [TestCase(429, null, 60f)]
        [TestCase(429, "", 60f)]
        [TestCase(429, "12", 12f)]
        [TestCase(503, "5", 5f)]
        [TestCase(503, null, 5f)]
        [TestCase(503, "Wed, 21 Oct 2026 07:28:00 GMT", 5f)]
        [TestCase(503, "0", 5f)]
        [TestCase(503, "-3", 5f)]
        [TestCase(503, "NaN", 5f)]
        [TestCase(503, "86400", 300f)]
        public void RateLimitAndUnavailable_PauseForRetryAfterOrADefault(long status, string retryAfter, float expected)
        {
            Assert.AreEqual(expected, PlaySafeSpeakerAgeClip.RetryDelaySeconds(status, retryAfter));
        }
    }

    public class PlaySafeSpeakerAgeResampleTests
    {
        [TestCase(8000, 16000)]
        [TestCase(11025, 16000)]
        [TestCase(16000, 16000)]
        [TestCase(22050, 24000)]
        [TestCase(24000, 24000)]
        [TestCase(32000, 48000)]
        [TestCase(44100, 48000)]
        [TestCase(48000, 48000)]
        public void EncodeRate_IsTheLowestEncoderRateAtOrAboveTheInput(int sampleRate, int expected)
        {
            Assert.AreEqual(expected, PlaySafeSpeakerAgeClip.EncodeRate(sampleRate));
        }

        [Test]
        public void Resample_SameRate_ReturnsTheSameArray()
        {
            float[] samples = { 0.1f, 0.2f };
            Assert.AreSame(samples, PlaySafeSpeakerAgeClip.Resample(samples, 16000, 16000));
        }

        [Test]
        public void Resample_DoublingTheRate_InterpolatesBetweenSamples_AndHoldsTheLast()
        {
            float[] resampled = PlaySafeSpeakerAgeClip.Resample(new[] { 0f, 1f, 0f }, 8000, 16000);
            Assert.AreEqual(new[] { 0f, 0.5f, 1f, 0.5f, 0f, 0f }, resampled);
        }

        [Test]
        public void Resample_KeepsTheDuration_SoFifteenSecondsStaysFifteenSeconds()
        {
            Assert.AreEqual(15 * 48000, PlaySafeSpeakerAgeClip.Resample(new float[15 * 44100], 44100, 48000).Length);
            Assert.AreEqual(15 * 24000, PlaySafeSpeakerAgeClip.Resample(new float[15 * 22050], 22050, 24000).Length);
            Assert.AreEqual(1, PlaySafeSpeakerAgeClip.Resample(new float[1], 44100, 48000).Length);
        }

        [Test]
        public void Resample_KeepsATone()
        {
            float[] tone = new float[44100];
            for (int i = 0; i < tone.Length; i++)
                tone[i] = (float)Math.Sin(2 * Math.PI * 440 * i / 44100);

            float[] resampled = PlaySafeSpeakerAgeClip.Resample(tone, 44100, 48000);

            for (int i = 0; i < resampled.Length - 2; i++)
                Assert.AreEqual(Math.Sin(2 * Math.PI * 440 * i / 48000), resampled[i], 0.002, $"sample {i}");
        }

        [Test]
        public void ToEncoderInput_MixesDownThenResamples()
        {
            // Stereo 8 kHz, two frames: (1, 0) and (0, 0) -> mono 0.5, 0 -> 16 kHz.
            float[] mono = PlaySafeSpeakerAgeClip.ToEncoderInput(new[] { 1f, 0f, 0f, 0f }, 8000, 2, out int encodeRate);

            Assert.AreEqual(16000, encodeRate);
            Assert.AreEqual(new[] { 0.5f, 0.25f, 0f, 0f }, mono);
        }

        [Test]
        public void ToEncoderInput_AtAnEncoderRateInMono_ReturnsTheSameArray()
        {
            float[] samples = new float[480];
            Assert.AreSame(samples, PlaySafeSpeakerAgeClip.ToEncoderInput(samples, 48000, 1, out _));
        }
    }

    public class PlaySafeOpusEncoderTests
    {
        private static float[] Tone(double seconds, int sampleRate)
        {
            var samples = new float[(int)(seconds * sampleRate)];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = 0.3f * (float)Math.Sin(2 * Math.PI * 220 * i / sampleRate);
            return samples;
        }

        // The blob is what the API reads: each packet behind a 2-byte big-endian length.
        private static int CountPackets(byte[] blob)
        {
            int offset = 0, count = 0;
            while (offset + 2 <= blob.Length)
            {
                int length = (blob[offset] << 8) | blob[offset + 1];
                Assert.Greater(length, 0);
                offset += 2 + length;
                count++;
            }
            Assert.AreEqual(blob.Length, offset, "the blob must end on a packet boundary");
            return count;
        }

        // What the API adds up to enforce its 15 s limit: each packet's duration from its TOC byte (RFC 6716, 3.1).
        private static double SumPacketMilliseconds(byte[] blob)
        {
            double[] silkMs = { 10, 20, 40, 60 }, hybridMs = { 10, 20 }, celtMs = { 2.5, 5, 10, 20 };
            double total = 0;
            for (int offset = 0; offset + 2 <= blob.Length;)
            {
                int length = (blob[offset] << 8) | blob[offset + 1];
                byte toc = blob[offset + 2];
                int config = toc >> 3, code = toc & 0x03;
                double frameMs = config < 12 ? silkMs[config & 0x03] : config < 16 ? hybridMs[config & 0x01] : celtMs[config & 0x03];
                int frames = code == 0 ? 1 : code == 3 ? blob[offset + 3] & 0x3F : 2;
                Assert.AreEqual(0, toc & 0x04, "the encoder is mono");
                total += frameMs * frames;
                offset += 2 + length;
            }
            return total;
        }

        [Test]
        public void DownmixToMono_Mono_ReturnsTheSameArray()
        {
            float[] samples = { 0.1f, 0.2f };
            Assert.AreSame(samples, PlaySafeOpusEncoder.DownmixToMono(samples, 1));
        }

        [Test]
        public void DownmixToMono_Stereo_AveragesEachFrame_AndDropsAPartialFrame()
        {
            float[] mono = PlaySafeOpusEncoder.DownmixToMono(new[] { 1f, 0f, 0.5f, -0.5f, 0.9f }, 2);
            Assert.AreEqual(new[] { 0.5f, 0f }, mono);
        }

        [TestCase(16000, 60)]
        [TestCase(24000, 40)]
        [TestCase(48000, 20)]
        public void Encode_ProducesWholeLengthPrefixedPackets(int sampleRate, int packetMs)
        {
            float[] samples = Tone(2.03, sampleRate);
            var (blob, packetCount) = PlaySafeOpusEncoder.Encode(samples, samples.Length, sampleRate);

            // 2.03 s does not end on a packet boundary, so the tail is padded into one more packet.
            Assert.AreEqual((int)Math.Ceiling(2030.0 / packetMs), packetCount);
            Assert.AreEqual(packetCount, CountPackets(blob));
        }

        [TestCase(16000, 16000, 250)]
        [TestCase(24000, 24000, 375)]
        [TestCase(48000, 48000, 750)]
        [TestCase(44100, 48000, 750)]
        [TestCase(8000, 16000, 250)]
        public void Encode_FifteenSeconds_IsExactlyTheFifteenSecondsTheApiAccepts(int sampleRate, int expectedRate, int expectedPackets)
        {
            float[] mono = PlaySafeSpeakerAgeClip.ToEncoderInput(Tone(15, sampleRate), sampleRate, 1, out int encodeRate);
            var (blob, packetCount) = PlaySafeOpusEncoder.Encode(mono, mono.Length, encodeRate, PlaySafeOpusEncoder.VoiceAnalysis);

            Assert.AreEqual(expectedRate, encodeRate);
            Assert.AreEqual(expectedPackets, packetCount);
            Assert.AreEqual(expectedPackets, CountPackets(blob));
            Assert.AreEqual(15000.0, SumPacketMilliseconds(blob));
            // The API's limit for this upload (SPEAKER_AGE_MAX_OPUS_BYTES in dl-voice-ai).
            Assert.Less(blob.Length, 1024 * 1024);
        }

        // Opus configs (RFC 6716, 3.1): 0-11 are the speech coder, 16-31 the waveform coder, 20-23 of those wideband.
        private static int FirstConfig(byte[] blob) => blob[2] >> 3;

        [TestCase(16000)]
        [TestCase(24000)]
        [TestCase(48000)]
        public void VoiceAnalysisProfile_UsesTheWaveformCoderAtWideband(int sampleRate)
        {
            float[] samples = Tone(2, sampleRate);
            byte[] blob = PlaySafeOpusEncoder.Encode(samples, samples.Length, sampleRate, PlaySafeOpusEncoder.VoiceAnalysis).blob;
            Assert.That(FirstConfig(blob), Is.InRange(20, 23));
        }

        [Test]
        public void TranscriptionProfile_IsTheDefault_AndKeepsTheSpeechCoder()
        {
            float[] samples = Tone(2, 16000);
            byte[] byDefault = PlaySafeOpusEncoder.Encode(samples, samples.Length, 16000).blob;
            byte[] named = PlaySafeOpusEncoder.Encode(samples, samples.Length, 16000, PlaySafeOpusEncoder.Transcription).blob;
            Assert.AreEqual(byDefault, named);
            Assert.Less(FirstConfig(byDefault), 12);
        }

        [Test]
        public void Profiles_DoNotAffectEachOther()
        {
            float[] samples = Tone(2, 16000);
            byte[] before = PlaySafeOpusEncoder.Encode(samples, samples.Length, 16000).blob;
            byte[] analysis = PlaySafeOpusEncoder.Encode(samples, samples.Length, 16000, PlaySafeOpusEncoder.VoiceAnalysis).blob;
            byte[] after = PlaySafeOpusEncoder.Encode(samples, samples.Length, 16000).blob;
            byte[] analysisAgain = PlaySafeOpusEncoder.Encode(samples, samples.Length, 16000, PlaySafeOpusEncoder.VoiceAnalysis).blob;

            Assert.AreEqual(before, after);
            Assert.AreEqual(analysis, analysisAgain);
            Assert.AreNotEqual(before, analysis);
        }
    }

    public class SpeakerAgeResponseTests
    {
        [Test]
        public void Deserializes_AnEstimate()
        {
            var response = JsonConvert.DeserializeObject<SpeakerAgeResponse>(
                "{\"ok\":true,\"message\":\"Speaker age estimated\",\"data\":{\"isChildProbability\":0.9313,\"conclusion\":\"likely_child\",\"reason\":\"ok\"}}");

            Assert.IsTrue(response.Ok);
            Assert.AreEqual(0.9313f, response.Data.IsChildProbability);
            Assert.AreEqual(SpeakerAgeConclusion.LikelyChild, response.Data.Conclusion);
            Assert.AreEqual(SpeakerAgeReason.Ok, response.Data.Reason);
        }

        [Test]
        public void Deserializes_NoEstimate_AsNullProbability()
        {
            var response = JsonConvert.DeserializeObject<SpeakerAgeResponse>(
                "{\"ok\":true,\"message\":\"Speaker age estimated\",\"data\":{\"isChildProbability\":null,\"conclusion\":\"uncertain\",\"reason\":\"not_enough_speech\"}}");

            Assert.IsNull(response.Data.IsChildProbability);
            Assert.AreEqual(SpeakerAgeConclusion.Uncertain, response.Data.Conclusion);
            Assert.AreEqual(SpeakerAgeReason.NotEnoughSpeech, response.Data.Reason);
        }

        [Test]
        public void Deserializes_AnErrorEnvelope_WithoutData()
        {
            var response = JsonConvert.DeserializeObject<SpeakerAgeResponse>("{\"ok\":false,\"message\":\"Too many requests\"}");

            Assert.IsFalse(response.Ok);
            Assert.IsNull(response.Data);
        }
    }
}
