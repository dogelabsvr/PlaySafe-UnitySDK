using System;
using System.Threading;
using NUnit.Framework;
using _DL.PlaySafe;

namespace _DL.PlaySafe.Tests
{
    public class PlaySafeReferenceRingTests
    {
        private static PlaySafeReferenceRing Filled(int cap, int count)
        {
            var ring = new PlaySafeReferenceRing(cap);
            for (int i = 0; i < count; i++) ring.WriteMonoSample((short)(i & 0x7FFF));
            ring.PublishFrame();
            return ring;
        }

        [Test]
        public void CopyRange_ReturnsWrittenData()
        {
            var ring = Filled(1 << 12, 3000);
            var dest = new short[100];
            Assert.IsTrue(ring.TryCopyRange(500, 100, dest, 0));
            for (int i = 0; i < 100; i++) Assert.AreEqual((short)((500 + i) & 0x7FFF), dest[i]);
        }

        [Test]
        public void CopyRange_AcrossWrap_IsContinuous()
        {
            const int cap = 1 << 12;
            var ring = Filled(cap, cap + 2000);
            var dest = new short[300];
            long start = cap + 2000 - 300;
            Assert.IsTrue(ring.TryCopyRange(start, 300, dest, 0));
            for (int i = 0; i < 300; i++) Assert.AreEqual((short)((start + i) & 0x7FFF), dest[i]);
        }

        [Test]
        public void CopyRange_Overwritten_Fails()
        {
            const int cap = 1 << 12;
            var ring = Filled(cap, cap * 3);
            var dest = new short[10];
            Assert.IsFalse(ring.TryCopyRange(5, 10, dest, 0));
        }

        [Test]
        public void CopyRange_NotYetWritten_Fails()
        {
            var ring = Filled(1 << 12, 100);
            var dest = new short[50];
            Assert.IsFalse(ring.TryCopyRange(80, 50, dest, 0));
        }

        [Test]
        public void Constructor_RejectsNonPowerOfTwo()
        {
            Assert.Throws<ArgumentException>(() => new PlaySafeReferenceRing(1000));
        }

        [Test]
        public void ConcurrentProducer_RecentReadsStayConsistent()
        {
            const int cap = 1 << 14;
            var ring = new PlaySafeReferenceRing(cap);
            const long total = 2_000_000;
            var producer = new Thread(() =>
            {
                long written = 0;
                while (written < total)
                {
                    for (int i = 0; i < 512 && written < total; i++, written++)
                        ring.WriteMonoSample((short)(written & 0x7FFF));
                    ring.PublishFrame();
                }
            });
            producer.Start();
            var dest = new short[256];
            int successes = 0;
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while ((producer.IsAlive || successes < 50) && DateTime.UtcNow < deadline)
            {
                long w = ring.WriteCount;
                if (w < 4096) continue;
                long start = w - 1024;
                if (ring.TryCopyRange(start, 256, dest, 0))
                {
                    successes++;
                    for (int i = 0; i < 256; i++)
                        Assert.AreEqual((short)((start + i) & 0x7FFF), dest[i], "corrupt at " + i);
                }
            }
            producer.Join();
            Assert.GreaterOrEqual(successes, 50);
        }
    }
}
