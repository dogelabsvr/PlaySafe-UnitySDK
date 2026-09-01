using System;

namespace _DL.PlaySafe
{
    /// <summary>
    /// The pure "should a new recording start?" decision behind PlaySafeManager.ShouldRecord(),
    /// extracted so it can be unit-tested without a MonoBehaviour/UnityEngine dependency.
    /// </summary>
    public static class PlaySafeRecordingDecision
    {
        public static bool ShouldRecord(
            bool? overrideValue,
            bool isEditorDebugRecord,
            bool shouldRecordPlayTestNotes,
            bool isRecording,
            double secondsSinceLastRecording,
            int recordingIntermissionSeconds,
            Func<bool> canRecord)
        {
            // The business-logic override outranks every other early-out here, including playtest
            // notes and the samplingRate/remote-config derivation - but it does not outrank the
            // caller's own CanRecord permission gate (mute/consent/lobby state). False short-circuits
            // canRecord entirely since "never record" is unconditional either way.
            if (overrideValue.HasValue)
                return overrideValue.Value && canRecord();

            if (isEditorDebugRecord && !isRecording)
                return true;

            // For continuous notes recording - start immediately when not recording
            if (shouldRecordPlayTestNotes && !isRecording)
                return canRecord();

            bool timeHasElapsed = secondsSinceLastRecording > recordingIntermissionSeconds;
            return (!isRecording || shouldRecordPlayTestNotes) && timeHasElapsed && canRecord();
        }
    }
}
