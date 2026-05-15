using System;
using System.IO;
using System.Threading.Tasks;
using Concentus;
using Concentus.Enums;
using Concentus.Structs;

namespace _DL.PlaySafe
{
    // Concentus is a pure-managed C# port of libopus. Per the maintainer it runs at ~40-50% of
    // native libopus speed, and per a documented Android case OpusEncoder.Encode for a 10s
    // buffer can take ~2 seconds. That is the freeze: SILK + CELT analysis runs entirely in
    // managed code on whichever thread calls Encode. Three knobs to make this not freeze
    // Unity's main thread:
    //   1. Cache the encoder. The constructor allocates SILK/CELT state + internal resamplers
    //      and was being paid on every recording window.
    //   2. Drop complexity from the default 10 to 5 (Xiph's mobile-VOIP recommendation). Lower
    //      complexity skips iterations of the noise-shaped quantizer and pitch search.
    //   3. Run Encode on a thread-pool worker via EncodeAsync. The Unity coroutine polls
    //      IsCompleted with `yield return null`, so the main thread keeps ticking frames at
    //      full rate even while the encoder is mid-buffer.
    internal static class PlaySafeOpusEncoder
    {
        private const int Channels = 1;
        private const int FrameSize = 960;       // 60 ms @ 16 kHz, 40 ms @ 24 kHz, 20 ms @ 48 kHz
        private const int Bitrate = 24000;
        private const int Complexity = 5;        // Default is 10. Xiph recommends 3-6 on mobile.
        private const int MaxPacketSize = 4000;  // per Opus spec

        private static OpusEncoder _cachedEncoder;
        private static int _cachedEncoderRate;
        private static readonly object _encoderLock = new object();

        // Reused under _encoderLock. Reuse means zero per-encode allocations apart from the
        // final `output.ToArray()` copy that gets handed to UnityWebRequest.
        private static readonly byte[] _packetBuf = new byte[MaxPacketSize];
        private static readonly MemoryStream _outputStream = new MemoryStream(64 * 1024);
        private static readonly float[] _tailFrame = new float[FrameSize];

        private static OpusEncoder GetEncoder(int sampleRate)
        {
            if (_cachedEncoder == null || _cachedEncoderRate != sampleRate)
            {
                _cachedEncoder = new OpusEncoder(sampleRate, Channels, OpusApplication.OPUS_APPLICATION_VOIP);
                _cachedEncoderRate = sampleRate;
            }
            else
            {
                _cachedEncoder.ResetState();
            }
            // libopus's OPUS_RESET_STATE preserves user CTL params (they live before
            // OPUS_ENCODER_RESET_START in the struct), but Concentus is a port — re-applying
            // the settings unconditionally is two property writes and removes the dependency
            // on that detail. Without this, a deviating ResetState would silently fall back
            // to default complexity (10) and undo the main per-frame optimization.
            _cachedEncoder.Bitrate = Bitrate;
            _cachedEncoder.Complexity = Complexity;
            return _cachedEncoder;
        }

        /// <summary>
        /// Synchronous encode. Holds an internal lock so concurrent calls serialize. Safe to call
        /// from any thread (no Unity API access, no P/Invoke, no I/O). Prefer EncodeAsync from
        /// the main thread to avoid blocking the frame.
        /// </summary>
        internal static (byte[] blob, int packetCount) Encode(float[] samples, int sampleCount, int sampleRate)
        {
            lock (_encoderLock)
            {
                var encoder = GetEncoder(sampleRate);

                _outputStream.SetLength(0);
                _outputStream.Position = 0;

                int packetCount = 0;
                int fullFrames = sampleCount / FrameSize;

                // Full frames slice straight out of the source buffer — no per-frame Array.Copy.
                for (int i = 0; i < fullFrames; i++)
                {
                    int encoded = encoder.Encode(
                        new ReadOnlySpan<float>(samples, i * FrameSize, FrameSize),
                        FrameSize,
                        new Span<byte>(_packetBuf),
                        MaxPacketSize);

                    _outputStream.WriteByte((byte)(encoded >> 8));
                    _outputStream.WriteByte((byte)(encoded & 0xFF));
                    _outputStream.Write(_packetBuf, 0, encoded);
                    packetCount++;
                }

                // One tail frame, zero-padded to FrameSize. Only run if there's a remainder.
                int tailLen = sampleCount - fullFrames * FrameSize;
                if (tailLen > 0)
                {
                    Array.Copy(samples, fullFrames * FrameSize, _tailFrame, 0, tailLen);
                    Array.Clear(_tailFrame, tailLen, FrameSize - tailLen);

                    int encoded = encoder.Encode(
                        new ReadOnlySpan<float>(_tailFrame),
                        FrameSize,
                        new Span<byte>(_packetBuf),
                        MaxPacketSize);

                    _outputStream.WriteByte((byte)(encoded >> 8));
                    _outputStream.WriteByte((byte)(encoded & 0xFF));
                    _outputStream.Write(_packetBuf, 0, encoded);
                    packetCount++;
                }

                return (_outputStream.ToArray(), packetCount);
            }
        }

        /// <summary>
        /// Runs <see cref="Encode"/> on a thread-pool worker so the Unity main thread does not
        /// stall. The caller should poll the returned Task from a coroutine via `IsCompleted`.
        /// </summary>
        internal static Task<(byte[] blob, int packetCount)> EncodeAsync(float[] samples, int sampleCount, int sampleRate)
        {
            // Snapshot to a worker-owned array. The recording buffer (audioBufferFromExistingMic)
            // can be repointed by the next StartRecording, and the Photon audio thread may still
            // tail-write the buffer when StopRecording flips _isRecording. Owning a copy avoids
            // both races without requiring locking on the caller.
            float[] owned = new float[sampleCount];
            Array.Copy(samples, 0, owned, 0, sampleCount);

            return Task.Run(() => Encode(owned, sampleCount, sampleRate));
        }
    }
}
