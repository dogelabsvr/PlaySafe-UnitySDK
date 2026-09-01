using System;
using NUnit.Framework;
using _DL.PlaySafe;

namespace _DL.PlaySafe.Tests
{
    // Captures ShouldRecord()'s pre-existing branch behavior 1:1, before PLAY-308 adds an override.
    public class PlaySafeRecordingDecisionTests
    {
        private static Func<bool> CanRecord(bool value) => () => value;

        private static Func<bool> Unreachable() => () => throw new InvalidOperationException(
            "canRecord should not have been evaluated - the decision must short-circuit before it");

        [Test]
        public void EditorDebugRecord_NotRecording_ReturnsTrue()
        {
            bool result = PlaySafeRecordingDecision.ShouldRecord(
                isEditorDebugRecord: true,
                shouldRecordPlayTestNotes: false,
                isRecording: false,
                secondsSinceLastRecording: 0,
                recordingIntermissionSeconds: int.MaxValue,
                canRecord: Unreachable());
            Assert.IsTrue(result);
        }

        [Test]
        public void EditorDebugRecord_AlreadyRecording_FallsThroughToDefaultPath()
        {
            bool result = PlaySafeRecordingDecision.ShouldRecord(
                isEditorDebugRecord: true,
                shouldRecordPlayTestNotes: false,
                isRecording: true,
                secondsSinceLastRecording: 9999,
                recordingIntermissionSeconds: 60,
                canRecord: Unreachable());
            Assert.IsFalse(result);
        }

        [Test]
        public void PlayTestNotes_NotRecording_ReturnsCanRecord()
        {
            bool whenTrue = PlaySafeRecordingDecision.ShouldRecord(
                isEditorDebugRecord: false,
                shouldRecordPlayTestNotes: true,
                isRecording: false,
                secondsSinceLastRecording: 0,
                recordingIntermissionSeconds: int.MaxValue,
                canRecord: CanRecord(true));
            bool whenFalse = PlaySafeRecordingDecision.ShouldRecord(
                isEditorDebugRecord: false,
                shouldRecordPlayTestNotes: true,
                isRecording: false,
                secondsSinceLastRecording: 0,
                recordingIntermissionSeconds: int.MaxValue,
                canRecord: CanRecord(false));
            Assert.IsTrue(whenTrue);
            Assert.IsFalse(whenFalse);
        }

        [Test]
        public void PlayTestNotes_AlreadyRecording_FallsThroughToDefaultPath()
        {
            bool result = PlaySafeRecordingDecision.ShouldRecord(
                isEditorDebugRecord: false,
                shouldRecordPlayTestNotes: true,
                isRecording: true,
                secondsSinceLastRecording: 100,
                recordingIntermissionSeconds: 60,
                canRecord: CanRecord(true));
            Assert.IsTrue(result);
        }

        [Test]
        public void DefaultPath_TimeElapsedAndCanRecord_ReturnsTrue()
        {
            bool result = PlaySafeRecordingDecision.ShouldRecord(
                isEditorDebugRecord: false,
                shouldRecordPlayTestNotes: false,
                isRecording: false,
                secondsSinceLastRecording: 100,
                recordingIntermissionSeconds: 60,
                canRecord: CanRecord(true));
            Assert.IsTrue(result);
        }

        [Test]
        public void DefaultPath_TimeNotElapsed_ReturnsFalse()
        {
            bool result = PlaySafeRecordingDecision.ShouldRecord(
                isEditorDebugRecord: false,
                shouldRecordPlayTestNotes: false,
                isRecording: false,
                secondsSinceLastRecording: 10,
                recordingIntermissionSeconds: 60,
                canRecord: Unreachable());
            Assert.IsFalse(result);
        }

        [Test]
        public void DefaultPath_CanRecordFalse_ReturnsFalse()
        {
            bool result = PlaySafeRecordingDecision.ShouldRecord(
                isEditorDebugRecord: false,
                shouldRecordPlayTestNotes: false,
                isRecording: false,
                secondsSinceLastRecording: 100,
                recordingIntermissionSeconds: 60,
                canRecord: CanRecord(false));
            Assert.IsFalse(result);
        }

        [Test]
        public void DefaultPath_AlreadyRecordingAndNotPlaytestNotes_ReturnsFalse()
        {
            bool result = PlaySafeRecordingDecision.ShouldRecord(
                isEditorDebugRecord: false,
                shouldRecordPlayTestNotes: false,
                isRecording: true,
                secondsSinceLastRecording: 9999,
                recordingIntermissionSeconds: 60,
                canRecord: Unreachable());
            Assert.IsFalse(result);
        }
    }
}
