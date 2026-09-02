using System;
using NUnit.Framework;
using _DL.PlaySafe;

namespace _DL.PlaySafe.Tests
{
    // Captures ShouldRecord()'s branch behavior 1:1, plus the PLAY-308 alwaysModerate override.
    public class PlaySafeRecordingDecisionTests
    {
        private static Func<bool> CanRecord(bool value) => () => value;

        private static Func<bool> Unreachable() => () => throw new InvalidOperationException(
            "canRecord should not have been evaluated - the decision must short-circuit before it");

        [Test]
        public void EditorDebugRecord_NotRecording_ReturnsTrue()
        {
            bool result = PlaySafeRecordingDecision.ShouldRecord(
                alwaysModerate: false,
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
                alwaysModerate: false,
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
                alwaysModerate: false,
                isEditorDebugRecord: false,
                shouldRecordPlayTestNotes: true,
                isRecording: false,
                secondsSinceLastRecording: 0,
                recordingIntermissionSeconds: int.MaxValue,
                canRecord: CanRecord(true));
            bool whenFalse = PlaySafeRecordingDecision.ShouldRecord(
                alwaysModerate: false,
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
                alwaysModerate: false,
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
                alwaysModerate: false,
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
                alwaysModerate: false,
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
                alwaysModerate: false,
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
                alwaysModerate: false,
                isEditorDebugRecord: false,
                shouldRecordPlayTestNotes: false,
                isRecording: true,
                secondsSinceLastRecording: 9999,
                recordingIntermissionSeconds: 60,
                canRecord: Unreachable());
            Assert.IsFalse(result);
        }

        [Test]
        public void AlwaysModerate_ForcesRecording_EvenAtZeroSamplingRate()
        {
            // recordingIntermissionSeconds: int.MaxValue is the samplingRate == 0 derivation.
            bool result = PlaySafeRecordingDecision.ShouldRecord(
                alwaysModerate: true,
                isEditorDebugRecord: false,
                shouldRecordPlayTestNotes: false,
                isRecording: false,
                secondsSinceLastRecording: 0,
                recordingIntermissionSeconds: int.MaxValue,
                canRecord: CanRecord(true));
            Assert.IsTrue(result);
        }

        [Test]
        public void AlwaysModerate_StillGatedByCanRecord()
        {
            bool result = PlaySafeRecordingDecision.ShouldRecord(
                alwaysModerate: true,
                isEditorDebugRecord: false,
                shouldRecordPlayTestNotes: false,
                isRecording: false,
                secondsSinceLastRecording: 0,
                recordingIntermissionSeconds: int.MaxValue,
                canRecord: CanRecord(false));
            Assert.IsFalse(result);
        }

        [Test]
        public void AlwaysModerate_CanRecordTrue_ReturnsTrue()
        {
            bool result = PlaySafeRecordingDecision.ShouldRecord(
                alwaysModerate: true,
                isEditorDebugRecord: false,
                shouldRecordPlayTestNotes: false,
                isRecording: false,
                secondsSinceLastRecording: 0,
                recordingIntermissionSeconds: int.MaxValue,
                canRecord: CanRecord(true));
            Assert.IsTrue(result);
        }

        [Test]
        public void AlwaysModerateOff_ReproducesDefaultPathExactly()
        {
            bool notElapsed = PlaySafeRecordingDecision.ShouldRecord(
                alwaysModerate: false,
                isEditorDebugRecord: false,
                shouldRecordPlayTestNotes: false,
                isRecording: false,
                secondsSinceLastRecording: 10,
                recordingIntermissionSeconds: 60,
                canRecord: Unreachable());
            bool elapsed = PlaySafeRecordingDecision.ShouldRecord(
                alwaysModerate: false,
                isEditorDebugRecord: false,
                shouldRecordPlayTestNotes: false,
                isRecording: false,
                secondsSinceLastRecording: 100,
                recordingIntermissionSeconds: 60,
                canRecord: CanRecord(true));
            Assert.IsFalse(notElapsed);
            Assert.IsTrue(elapsed);
        }

        [Test]
        public void AlwaysModerate_FlippingBetweenEvaluations_TakesEffectImmediately_NoCachedDecision()
        {
            // Same inputs except alwaysModerate - proves nothing is latched between calls.
            bool first = PlaySafeRecordingDecision.ShouldRecord(
                alwaysModerate: false,
                isEditorDebugRecord: false,
                shouldRecordPlayTestNotes: false,
                isRecording: false,
                secondsSinceLastRecording: 0,
                recordingIntermissionSeconds: int.MaxValue,
                canRecord: CanRecord(true));
            bool second = PlaySafeRecordingDecision.ShouldRecord(
                alwaysModerate: true,
                isEditorDebugRecord: false,
                shouldRecordPlayTestNotes: false,
                isRecording: false,
                secondsSinceLastRecording: 0,
                recordingIntermissionSeconds: int.MaxValue,
                canRecord: CanRecord(true));
            bool third = PlaySafeRecordingDecision.ShouldRecord(
                alwaysModerate: false,
                isEditorDebugRecord: false,
                shouldRecordPlayTestNotes: false,
                isRecording: false,
                secondsSinceLastRecording: 0,
                recordingIntermissionSeconds: int.MaxValue,
                canRecord: CanRecord(true));
            Assert.IsFalse(first);
            Assert.IsTrue(second);
            Assert.IsFalse(third);
        }

        [Test]
        public void AlwaysModerate_OutranksPlayTestNotesGating()
        {
            bool result = PlaySafeRecordingDecision.ShouldRecord(
                alwaysModerate: true,
                isEditorDebugRecord: false,
                shouldRecordPlayTestNotes: true,
                isRecording: true,
                secondsSinceLastRecording: 0,
                recordingIntermissionSeconds: int.MaxValue,
                canRecord: CanRecord(true));
            Assert.IsTrue(result);
        }

        [Test]
        public void AlwaysModerate_WhileAlreadyRecording_StillReturnsTrue()
        {
            // The !_isRecording guard in Update() is what prevents a double-start, not this.
            bool result = PlaySafeRecordingDecision.ShouldRecord(
                alwaysModerate: true,
                isEditorDebugRecord: false,
                shouldRecordPlayTestNotes: false,
                isRecording: true,
                secondsSinceLastRecording: 0,
                recordingIntermissionSeconds: int.MaxValue,
                canRecord: CanRecord(true));
            Assert.IsTrue(result);
        }

        [Test]
        public void AlwaysModerate_InEditorWithDebugRecordOff_StillReturnsTrue()
        {
            bool result = PlaySafeRecordingDecision.ShouldRecord(
                alwaysModerate: true,
                isEditorDebugRecord: false, // Application.isEditor && debugEnableRecord == false
                shouldRecordPlayTestNotes: false,
                isRecording: false,
                secondsSinceLastRecording: 0,
                recordingIntermissionSeconds: int.MaxValue,
                canRecord: CanRecord(true));
            Assert.IsTrue(result);
        }

        [Test]
        public void AlwaysModerate_FlippingWhileRecording_TakesEffectImmediately()
        {
            bool whenTrue = PlaySafeRecordingDecision.ShouldRecord(
                alwaysModerate: true,
                isEditorDebugRecord: false,
                shouldRecordPlayTestNotes: false,
                isRecording: true,
                secondsSinceLastRecording: 0,
                recordingIntermissionSeconds: 60,
                canRecord: CanRecord(true));
            bool whenFalse = PlaySafeRecordingDecision.ShouldRecord(
                alwaysModerate: false,
                isEditorDebugRecord: false,
                shouldRecordPlayTestNotes: false,
                isRecording: true,
                secondsSinceLastRecording: 0,
                recordingIntermissionSeconds: 60,
                canRecord: CanRecord(true));
            Assert.IsTrue(whenTrue);
            Assert.IsFalse(whenFalse);
        }
    }
}
