using UnityEngine;

namespace Playsafe.Runtime.Core {
  public class AudioRecorder: MonoBehaviour {
        private AudioClip _audioClipRecording;
        private bool _isRecording = false;
        private Stopwatch _lastRecording = new Stopwatch();
        private const int RecordingDurationSeconds = 10;
        private int _recordingIntermissionSeconds = 60;  // May be updated from remote config

        #endregion


        private bool HasMicrophonePermission()
        {
            return Permission.HasUserAuthorizedPermission(Permission.Microphone);
        }

        private int GetMicrophoneDeviceCount()
        {
            return Microphone.devices.Length;
        }

        private string GetDefaultMicrophone()
        {
            return (Microphone.devices.Length > 0) ? Microphone.devices[0] : "Not set";
        }

        /// <summary>
        /// Returns true if recording should be started.
        /// </summary>
        private bool ShouldRecord()
        {
            if (Application.isEditor && debugEnableRecord && !_isRecording)
                return true;

            return !_isRecording &&
                   _lastRecording.Elapsed.TotalSeconds > _recordingIntermissionSeconds &&
                   CanRecord();
        }

        /// <summary>
        /// Public method to manually toggle recording.
        /// </summary>
        public void ToggleRecording()
        {
            if (_isRecording)
            {
                StopRecording();
            }
            else
            {
                StartRecording();
            }
        }

        private void StartRecording()
        {
            if (GetMicrophoneDeviceCount() <= 0)
                return;

            string mic = GetDefaultMicrophone();
            _audioClipRecording = Microphone.Start(mic, false, RecordingDurationSeconds, 16000); // 10 seconds at 16 kHz
            _isRecording = true;
            _lastRecording.Restart();
            Debug.Log("DLVoiceAIModeration: Recording started");
        }

        private void StopRecording()
        {
            if (!_isRecording)
                return;

            Microphone.End(null);
            StartCoroutine(SendAudioClipForAnalysisCoroutine(_audioClipRecording));
            _isRecording = false;
            Debug.Log("DLVoiceAIModeration: Recording stopped");
        }
        
        public void _ToggleRecording()
        {
            if (_isRecording)
            {
                StopRecording();
            }
            else
            {
                StartRecording();
            }
        }

        #endregion
  }
}