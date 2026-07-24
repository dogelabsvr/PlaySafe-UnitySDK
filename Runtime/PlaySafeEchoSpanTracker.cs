using System.Collections.Generic;

namespace _DL.PlaySafe
{
    // Main-thread only. Records which reference-ring ranges correspond to which
    // mic-buffer ranges across the pause/resume cycles of one recording, so the
    // analyzer can align discontinuous mic audio with the output mix.
    internal sealed class PlaySafeEchoSpanTracker
    {
        private readonly List<EchoSpan> _spans = new List<EchoSpan>(8);
        private int _openMicStart = -1;
        private long _openRefStart = -1;

        internal void Reset()
        {
            _spans.Clear();
            _openMicStart = -1;
            _openRefStart = -1;
        }

        internal void BeginSpan(int micRawIndex, long refCount)
        {
            if (_openMicStart >= 0) return;
            _openMicStart = micRawIndex;
            _openRefStart = refCount;
        }

        internal void EndSpan(int micRawIndex, long refCount)
        {
            if (_openMicStart < 0) return;
            if (micRawIndex > _openMicStart && refCount > _openRefStart)
                _spans.Add(new EchoSpan(_openMicStart, micRawIndex, _openRefStart, refCount));
            _openMicStart = -1;
            _openRefStart = -1;
        }

        internal EchoSpan[] Snapshot() => _spans.ToArray();
    }
}
