using NUnit.Framework;
using _DL.PlaySafe;

namespace _DL.PlaySafe.Tests
{
    public class PlaySafeEchoSpanTrackerTests
    {
        [Test]
        public void BeginEnd_ProducesSpan()
        {
            var t = new PlaySafeEchoSpanTracker();
            t.Reset();
            t.BeginSpan(0, 1000);
            t.EndSpan(500, 1500);
            var spans = t.Snapshot();
            Assert.AreEqual(1, spans.Length);
            Assert.AreEqual(0, spans[0].MicRawStart);
            Assert.AreEqual(500, spans[0].MicRawEnd);
            Assert.AreEqual(1000, spans[0].RefStart);
            Assert.AreEqual(1500, spans[0].RefEnd);
        }

        [Test]
        public void DoubleBegin_IgnoresSecond()
        {
            var t = new PlaySafeEchoSpanTracker();
            t.BeginSpan(0, 1000);
            t.BeginSpan(100, 2000);
            t.EndSpan(500, 1500);
            var spans = t.Snapshot();
            Assert.AreEqual(1, spans.Length);
            Assert.AreEqual(0, spans[0].MicRawStart);
            Assert.AreEqual(1000, spans[0].RefStart);
        }

        [Test]
        public void EndWithoutBegin_IsNoop()
        {
            var t = new PlaySafeEchoSpanTracker();
            t.EndSpan(500, 1500);
            Assert.AreEqual(0, t.Snapshot().Length);
        }

        [Test]
        public void ZeroLengthSpan_Discarded()
        {
            var t = new PlaySafeEchoSpanTracker();
            t.BeginSpan(100, 1000);
            t.EndSpan(100, 1000);
            Assert.AreEqual(0, t.Snapshot().Length);
        }

        [Test]
        public void Reset_ClearsSpansAndOpenSpan()
        {
            var t = new PlaySafeEchoSpanTracker();
            t.BeginSpan(0, 100);
            t.EndSpan(50, 150);
            t.BeginSpan(60, 200);
            t.Reset();
            t.EndSpan(500, 1500); // must be a noop: Reset closed the open span
            Assert.AreEqual(0, t.Snapshot().Length);
        }

        [Test]
        public void MultipleSpans_AllReported()
        {
            var t = new PlaySafeEchoSpanTracker();
            t.BeginSpan(0, 100);
            t.EndSpan(50, 150);
            t.BeginSpan(60, 300);
            t.EndSpan(90, 330);
            Assert.AreEqual(2, t.Snapshot().Length);
        }
    }
}
