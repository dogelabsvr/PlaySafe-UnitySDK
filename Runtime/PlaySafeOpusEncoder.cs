using System;
using System.IO;
using Concentus;
using Concentus.Enums;
using Concentus.Structs;

namespace _DL.PlaySafe
{
    internal static class PlaySafeOpusEncoder
    {
        private const int Channels = 1;
        private const int FrameSize = 960;      // 20ms @ 48kHz; 60ms @ 16kHz; 40ms @ 24kHz - all valid Opus frame sizes
        private const int Bitrate = 24000;      // 24kbps VOIP
        private const int MaxPacketSize = 4000; // per Opus spec

        internal static (byte[] blob, int packetCount) Encode(float[] samples, int sampleCount, int sampleRate)
        {
            var encoder = new OpusEncoder(sampleRate, Channels, OpusApplication.OPUS_APPLICATION_VOIP);
            encoder.Bitrate = Bitrate;

            var output = new MemoryStream();
            var packetBuf = new byte[MaxPacketSize];
            var frame = new float[FrameSize];
            int packetCount = 0;
            int totalFrames = (sampleCount + FrameSize - 1) / FrameSize;

            for (int i = 0; i < totalFrames; i++)
            {
                int start = i * FrameSize;
                int available = Math.Min(FrameSize, sampleCount - start);
                Array.Clear(frame, 0, FrameSize);
                Array.Copy(samples, start, frame, 0, available);

                int encodedBytes = encoder.Encode(
                    new ReadOnlySpan<float>(frame),
                    FrameSize,
                    new Span<byte>(packetBuf),
                    MaxPacketSize);

                // Length-prefix: uint16 big-endian
                output.WriteByte((byte)(encodedBytes >> 8));
                output.WriteByte((byte)(encodedBytes & 0xFF));
                output.Write(packetBuf, 0, encodedBytes);
                packetCount++;
            }

            return (output.ToArray(), packetCount);
        }
    }
}
