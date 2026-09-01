using System;
using NUnit.Framework;
using _DL.PlaySafe;

namespace _DL.PlaySafe.Tests
{
    // Captures ShouldRecord()'s branch behavior 1:1, plus the PLAY-308 business-logic override.
    public class PlaySafeRecordingDecisionTests
    {
        private static Func<bool> CanRecord(bool value) => () => value;

        private static Func<bool> Unreachable() => () => throw new InvalidOperationException(
            "canRecord should not have been evaluated - the decision must short-circuit before it");

        [Test]
        public void EditorDebugRecord_NotRecording_ReturnsTrue()
        {
            bool result = PlaySafeRecordingDecision.ShouldRecord(
                overrideValue: null,
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
                overrideValue: null,
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
                overrideValue: null,
                isEditorDebugRecord: false,
                shouldRecordPlayTestNotes: true,
                isRecording: false,
                secondsSinceLastRecording: 0,
                recordingIntermissionSeconds: int.MaxValue,
                canRecord: CanRecord(true));
            bool whenFalse = PlaySafeRecordingDecision.ShouldRecord(
                overrideValue: null,
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
                overrideValue: null,
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
                overrideValue: null,
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
                overrideValue: null,
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
                overrideValue: null,
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
                overrideValue: null,
                isEditorDebugRecord: false,
                shouldRecordPlayTestNotes: false,
                isRecording: true,
                secondsSinceLastRecording: 9999,
                recordingIntermissionSeconds: 60,
                canRecord: Unreachable());
            Assert.IsFalse(result);
        }

        [Test]
        public void OverrideTrue_ForcesRecording_EvenAtZeroSamplingRate()
        {
            // recordingIntermissionSeconds: int.MaxValue is the samplingRate == 0 derivation.
            bool result = PlaySafeRecordingDecision.ShouldRecord(
                overrideValue: true,
                isEditorDebugRecord: false,
                shouldRecordPlayTestNotes: false,
                isRecording: false,
                secondsSinceLastRecording: 0,
                recordingIntermissionSeconds: int.MaxValue,
                canRecord: CanRecord(true));
            Assert.IsTrue(result);
        }

        [Test]
        public void OverrideFalse_SuppressesRecording_EvenAtFullSamplingRate()
        {
            // recordingIntermissionSeconds: 0 is the samplingRate == 1 derivation.
            bool result = PlaySafeRecordingDecision.ShouldRecord(
                overrideValue: false,
                isEditorDebugRecord: false,
                shouldRecordPlayTestNotes: false,
                isRecording: false,
                secondsSinceLastRecording: 9999,
                recordingIntermissionSeconds: 0,
                canRecord: CanRecord(true));
            Assert.IsFalse(result);
        }

        [Test]
        public void OverrideNull_ReproducesDefaultPathExactly()
        {
            bool notElapsed = PlaySafeRecordingDecision.ShouldRecord(
                overrideValue: null,
                isEditorDebugRecord: false,
                shouldRecordPlayTestNotes: false,
                isRecording: false,
                secondsSinceLastRecording: 10,
                recordingIntermissionSeconds: 60,
                canRecord: Unreachable());
            bool elapsed = PlaySafeRecordingDecision.ShouldRecord(
                overrideValue: null,
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
        public void Override_FlippingBetweenEvaluations_TakesEffectImmediately_NoCachedDecision()
        {
            // Same inputs every time except overrideValue - proves the decision is re-read fresh
            // each call rather than latched from a prior evaluation.
            bool first = PlaySafeRecordingDecision.ShouldRecord(
                overrideValue: false,
                isEditorDebugRecord: false,
                shouldRecordPlayTestNotes: false,
                isRecording: false,
                secondsSinceLastRecording: 9999,
                recordingIntermissionSeconds: 0,
                canRecord: CanRecord(true));
            bool second = PlaySafeRecordingDecision.ShouldRecord(
                overrideValue: true,
                isEditorDebugRecord: false,
                shouldRecordPlayTestNotes: false,
                isRecording: false,
                secondsSinceLastRecording: 0,
                recordingIntermissionSeconds: int.MaxValue,
                canRecord: CanRecord(true));
            bool third = PlaySafeRecordingDecision.ShouldRecord(
                overrideValue: null,
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
    }
}
