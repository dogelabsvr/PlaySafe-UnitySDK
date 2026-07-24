using System;
using System.Threading;

namespace _DL.PlaySafe
{
    // Single-producer ring of 16 kHz mono reference audio. The producer is
    // Unity's audio thread (PlaySafeOutputTap); consumers copy ranges by
    // absolute sample count on worker threads. Reads are stateless and
    // re-validated after copying, so a producer lapping mid-copy is detected
    // rather than returning corrupt audio.
    internal sealed class PlaySafeReferenceRing
    {
        private readonly short[] _buf;
        private readonly int _mask;
        private long _cursor;       // producer-local position
        private long _writeCount;   // published position

        internal int Capacity { get; }

        internal PlaySafeReferenceRing(int capacityPow2)
        {
            if (capacityPow2 < 2 || (capacityPow2 & (capacityPow2 - 1)) != 0)
                throw new ArgumentException("Capacity must be a power of two", nameof(capacityPow2));
            Capacity = capacityPow2;
            _buf = new short[capacityPow2];
            _mask = capacityPow2 - 1;
        }

        internal void WriteMonoSample(short s)
        {
            _buf[(int)(_cursor & _mask)] = s;
            _cursor++;
        }

        internal void PublishFrame() => Volatile.Write(ref _writeCount, _cursor);

        internal long WriteCount => Volatile.Read(ref _writeCount);

        internal bool TryCopyRange(long startCount, int len, short[] dest, int destOffset)
        {
            if (len <= 0 || startCount < 0) return false;
            long w = Volatile.Read(ref _writeCount);
            if (startCount + len > w) return false;
            if (startCount < w - Capacity) return false;

            int s = (int)(startCount & _mask);
            int first = Math.Min(len, Capacity - s);
            Array.Copy(_buf, s, dest, destOffset, first);
            if (first < len) Array.Copy(_buf, 0, dest, destOffset + first, len - first);

            long wAfter = Volatile.Read(ref _writeCount);
            return startCount >= wAfter - Capacity;
        }
    }
}
