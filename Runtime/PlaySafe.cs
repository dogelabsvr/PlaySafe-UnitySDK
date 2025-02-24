namespace PlaySafe.Runtime {
  public class PlaySafe: MonoBehaviour {
    private void Update()
    {
        UpdateDebugInfo();

        if (!_isInitialized)
            return;

        if (!HasMicrophonePermission())
            return;

        if (ShouldRecord())
        {
            StartRecording();
        }
        else if (_isRecording && _lastRecording.Elapsed.TotalSeconds > RecordingDurationSeconds)
        {
            StopRecording();
        }
    }
  }
}