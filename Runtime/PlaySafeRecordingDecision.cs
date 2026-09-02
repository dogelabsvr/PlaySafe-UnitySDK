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
            bool alwaysModerate,
            bool isEditorDebugRecord,
            bool shouldRecordPlayTestNotes,
            bool isRecording,
            double secondsSinceLastRecording,
            int recordingIntermissionSeconds,
            Func<bool> canRecord)
        {
            // Outranks the other early-outs, but not the caller's CanRecord gate.
            if (alwaysModerate)
                return canRecord();

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
