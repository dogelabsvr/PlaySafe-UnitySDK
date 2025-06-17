using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.Networking;
using UnityEngine.Serialization;
using Debug = UnityEngine.Debug;
#if PHOTON_VOICE_DEFINED
    using Photon.Voice;
    using Photon.Voice.Unity;
#endif

namespace _DL.PlaySafe
{
    public class PlaySafeManager : MonoBehaviour
    {
        private const string PlaysafeBaseURL = "https://dl-voice-ai.dogelabs.workers.dev";
        private const string VoiceModerationEndpoint = "/products/moderation";
        private const string ReportEndpoint = "/products/moderation";
        
        #region Singleton & Initialization

        public static PlaySafeManager Instance;

        private bool _isInitialized = false;

        /// <summary>
        /// Must be set to a delegate that returns whether recording is permitted.
        /// </summary>
        public Func<bool> CanRecord { private get; set; }

        /// <summary>
        /// Must be set to provide telemetry data.
        /// </summary>
        public Func<AudioEventRequestData> GetTelemetry { private get; set; }

        /// <summary>
        /// Called when an action is returned from voice moderation.
        /// </summary>
        /// <summary>
        /// Called when an action is returned from voice moderation.
        /// </summary>
        public Action<ActionItem, DateTime> OnActionEvent { private get; set; }

        // Session state management
        private enum SessionState { Inactive, Starting, Active, Ending }
        private SessionState _currentSessionState = SessionState.Inactive;
        private string _currentUserId = null;
        private Coroutine _activeSessionCoroutine = null;
        private float _lastSessionChangeTime = 0f;
        private const float MIN_SESSION_CHANGE_INTERVAL = 2.0f; // seconds

        /// <summary>
        /// Call this method to initialize the voice AI moderation system.
        /// </summary>
        public void Initialize()
        {
            Instance = this;
            if (CanRecord == null)
            {
                Debug.LogError("Must set CanRecord delegate before initializing");
                return;
            }

            if (GetTelemetry == null)
            {
                Debug.LogError("Must set GetTelemetry delegate before initializing");
                return;
            }

            if (_isInitialized)
            {
                Debug.LogError("PlaySafeManager is already initialized");
                return;
            }

            _isInitialized = true;
            Setup();
            Debug.Log($"<color=#00FF00>PlaySafeManager is running!</color>");
        }

        private void Setup()
        {
            if (GetMicrophoneDeviceCount() <= 0)
            {
                Debug.LogError("PlaySafeManager: No microphone found");
            }
            StartCoroutine(GetProductAIConfig());
            _lastRecording.Restart();
            _currentSessionState = SessionState.Inactive;
            _lastSessionChangeTime = 0f;
        }

        #endregion

        #region Inspector & Debug Fields

        [Header("Configuration")]
        [SerializeField] private bool debugEnableRecord = false;
        [SerializeField] private string appKey;
         private float _silenceThreshold = 0.02f;

        public int sampleRate = 24000;
        public int channelCount = 1;
        public bool isUsingExistingUnityMic = false;

        [Header("Debug Information - Not For Editing Purposes")]
        [SerializeField, Tooltip("Indicates whether microphone permission has been granted.")]
        private bool hasPermission = false;
        [SerializeField, Tooltip("Selected microphone.")]
        private string selectedMicrophone = "Not set";
        [SerializeField, Tooltip("Indicates whether recording is currently active.")]
        private bool isMicRecording = false;
        [SerializeField, Tooltip("Indicates whether dev wants to record via ShouldRecord().")]
        private bool shouldRecord = false;
        [SerializeField, Tooltip("Number of microphone devices detected.")]
        private int microphoneDevicesCount = 0;

        #endregion

        #region Recording Fields

        private AudioClip _audioClipRecording;
        private bool _isRecording = false;
        private Stopwatch _lastRecording = new Stopwatch();
        private const int RecordingDurationSeconds = 10;
        private int _recordingIntermissionSeconds = 60;  // May be updated from remote config
        
        #if PHOTON_VOICE_DEFINED
        private PhotonPlaySafeProcessor _photonPlaySafeProcessor;
        #endif
        
        public float[] audioBufferFromExistingMic;

        private int _sampleIndex = 0;

        #endregion

        #region Unity Lifecycle
        private void Start() {
            // Photon specific setup
            #if PHOTON_VOICE_DEFINED
            _photonPlaySafeProcessor = new PhotonPlaySafeProcessor();
            _photonPlaySafeProcessor.playSafeManager = this;
            #endif
        }

        private void Update()
        {
            UpdateDebugInfo();

            if (!_isInitialized)
                return;

            if (!HasMicrophonePermission())
                return;
			
			
			if(_isRecording && (!CanRecord() && !Application.isEditor))
			{ 
				bool shouldSendAudioForProcessing = _lastRecording.Elapsed.TotalSeconds > RecordingDurationSeconds;
                Debug.Log($"<color=#FF0000>PlaySafeManager: Recording stopped because player muted. Time elapsed: {_lastRecording.Elapsed.TotalSeconds:F2}s. Sending for processing: {shouldSendAudioForProcessing}</color>");
                
				StopRecording(shouldSendAudioForProcessing);
			}
            else if (ShouldRecord())
            {
                StartRecording();
            }
            else if (_isRecording && _lastRecording.Elapsed.TotalSeconds > RecordingDurationSeconds)
            {
                StopRecording();
            }
        }

        #endregion

        #region Debug Helpers

        private void UpdateDebugInfo()
        {
            if (Application.isEditor)
            {
                hasPermission = HasMicrophonePermission();
                isMicRecording = _isRecording;
                shouldRecord = ShouldRecord();
                microphoneDevicesCount = GetMicrophoneDeviceCount();
                if (hasPermission && microphoneDevicesCount > 0)
                {
                    selectedMicrophone = GetDefaultMicrophone();
                }
            }
        }

        #endregion

        #region Photon Specific
        #if PHOTON_VOICE_DEFINED
            // init PlaySafe once we set up photon voice; called from Photon's Recorder via SendMessage
        public void PhotonVoiceCreated (PhotonVoiceCreatedParams voiceCreatedParams)
        {
            Debug.Log("Photon voice created, initializing PlaySafe");
            Initialize();

            var voice = voiceCreatedParams.Voice as LocalVoiceAudioFloat;
            if (voice != null)
            {
                channelCount = voice.Info.Channels;
                sampleRate = voice.Info.SamplingRate;
                
                voice.AddPostProcessor(_photonPlaySafeProcessor);
            }
        }
        #endif

        #endregion

        #region Microphone & Recording Helpers

        public int GetRecordingDuration ()
        {
            return RecordingDurationSeconds;
        }

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

            if (!isUsingExistingUnityMic)
            {
                string mic = GetDefaultMicrophone();
                _audioClipRecording =
                    Microphone.Start(mic, false, RecordingDurationSeconds, 16000); // 10 seconds at 16 kHz
            }
            else
            {
                CreateNewBuffer();
            }
            
            _isRecording = true;
            _lastRecording.Restart();
            Debug.Log("PlaySafeManager: Recording started");
        }
        
        private void CreateNewBuffer ()
        {
            audioBufferFromExistingMic = new float[sampleRate * channelCount * RecordingDurationSeconds]; // 10 seconds of audio
            _sampleIndex = 0;
        }
        
        public void AppendToBuffer(float[] newData)
        {
            if (audioBufferFromExistingMic == null) return;
            
            int newDataLength = newData.Length;
            if (newDataLength > 0 && _sampleIndex + newDataLength < audioBufferFromExistingMic.Length)
            {
                System.Array.Copy(newData, 0, audioBufferFromExistingMic, _sampleIndex, newData.Length);
                _sampleIndex += newDataLength;
            }
        }
        
        private AudioClip CreateAudioClip()
        {
            if (channelCount < 1) return null;
            if (audioBufferFromExistingMic == null) return null;
            if (audioBufferFromExistingMic.Length == 0) return null;
            
            AudioClip clip = AudioClip.Create("RecordedAudio", audioBufferFromExistingMic.Length / channelCount, 
                channelCount, sampleRate, false);
            clip.SetData(audioBufferFromExistingMic, 0);
            //Debug.LogWarning("PlaySafeManager Audioclip length: " + clip.length + ", sample rate: " + sampleRate + ", channels: " + channelCount);
            return clip;
        }


        private void StopRecording(bool shouldStartCoroutine = true)
        {
            if (!_isRecording)
                return;

            if (isUsingExistingUnityMic)
            {
                _audioClipRecording = CreateAudioClip();
                
                // debug echo loopback
                /* 
                AudioSource audioSource = GetComponent<AudioSource>();
                if (audioSource == null)
                {
                    audioSource = gameObject.AddComponent<AudioSource>();
                }

                audioSource.clip = _audioClipRecording;
                audioSource.volume = 1f;
                audioSource.spatialBlend = 0f;
                audioSource.Play();
                */
            }
            else
            {
                Microphone.End(null);
            }


			if(shouldStartCoroutine) {
            	StartCoroutine(SendAudioClipForAnalysisCoroutine(_audioClipRecording));
			}

            _isRecording = false;
            
            Debug.Log("PlaySafeManager: Recording stopped");
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

        #region Audio Processing

        /// <summary>
        /// Converts an AudioClip to a WAV file byte array and checks if it is silent.
        /// </summary>
        /// <param name="clip">The AudioClip to convert.</param>
        /// <returns>A tuple containing the WAV file bytes and a flag indicating if the clip is silent.</returns>
// Create a persistent MemoryStream once
        private MemoryStream reusableStream = new MemoryStream();

        public (byte[] wavFileBytes, bool isSilent) AudioClipToFile(AudioClip clip)
        {
            if (!clip)
                throw new ArgumentNullException(nameof(clip));

            // Reset the reusable MemoryStream.
            reusableStream.SetLength(0);
            reusableStream.Position = 0;

            int sampleCount = clip.samples * clip.channels;
            // Reserve header space (44 bytes) for the WAV header.
            reusableStream.Write(new byte[44], 0, 44);

            // Get the audio samples.
            float[] samples = new float[sampleCount];
            clip.GetData(samples, 0);

            // Create a single byte array for the audio data.
            // Each sample will become 2 bytes (16 bits).
            byte[] audioBytes = new byte[sampleCount * sizeof(short)];
            
            bool isSilent = true;
            float rescaleFactor = 32767f;

            // Convert each sample directly to bytes.
            for (int i = 0; i < sampleCount; i++)
            {
                float sample = samples[i];
               
                if (isSilent && Mathf.Abs(sample) > _silenceThreshold)
                {
                    isSilent = false;
                }
                short intSample = (short)(sample * rescaleFactor);
                // Write in little-endian order.
                audioBytes[2 * i] = (byte)(intSample & 0xFF);
                audioBytes[2 * i + 1] = (byte)((intSample >> 8) & 0xFF);
            }

            // Write the converted audio data to the stream.
            reusableStream.Write(audioBytes, 0, audioBytes.Length);

            // Write the WAV header at the beginning.
            reusableStream.Position = 0;
            WriteWavHeader(reusableStream, clip, audioBytes.Length);

            // Return the complete byte array and the silence flag.
            byte[] result = reusableStream.ToArray();
            return (result, isSilent);
        }



        private void WriteWavHeader(Stream stream, AudioClip clip, int dataLength)
        {
            // RIFF header
            stream.Write(System.Text.Encoding.UTF8.GetBytes("RIFF"), 0, 4);
            stream.Write(BitConverter.GetBytes((int)(stream.Length - 8)), 0, 4);
            stream.Write(System.Text.Encoding.UTF8.GetBytes("WAVE"), 0, 4);

            // fmt subchunk
            stream.Write(System.Text.Encoding.UTF8.GetBytes("fmt "), 0, 4);
            stream.Write(BitConverter.GetBytes(16), 0, 4); // Subchunk1Size (16 for PCM)
            stream.Write(BitConverter.GetBytes((short)1), 0, 2); // AudioFormat (1 for PCM)
            stream.Write(BitConverter.GetBytes((short)clip.channels), 0, 2);
            stream.Write(BitConverter.GetBytes(clip.frequency), 0, 4);
            stream.Write(BitConverter.GetBytes(clip.frequency * clip.channels * sizeof(short)), 0, 4);
            stream.Write(BitConverter.GetBytes((short)(clip.channels * sizeof(short))), 0, 2);
            stream.Write(BitConverter.GetBytes((short)16), 0, 2);

            // data subchunk
            stream.Write(System.Text.Encoding.UTF8.GetBytes("data"), 0, 4);
            stream.Write(BitConverter.GetBytes(dataLength), 0, 4);
        }

        #endregion

        #region Web Requests

        /// <summary>
        /// Prepares a WWWForm with telemetry data.
        /// </summary>
        private WWWForm SetupForm()
        {
            AudioEventRequestData telemetry = GetTelemetry();
            WWWForm form = new WWWForm();
            form.AddField("userId", telemetry.UserId);
            form.AddField("roomId", telemetry.RoomId);
            return form;
        }

        private IEnumerator SendAudioClipForAnalysisCoroutine(AudioClip clip)
        {
            if (clip == null)
            {
                Debug.LogError("PlaySafeManager: AudioClip is null.");
                yield break;
            }

            var (wavFileBytes, isSilent) = AudioClipToFile(clip);
            if (isSilent)
            {
                Debug.Log("PlaySafeManager: The AudioClip is silent. Skipping upload.");
                yield break;
            }

            WWWForm form = SetupForm();
            form.AddBinaryData("audio", wavFileBytes, "audio.wav", "audio/wav");

            yield return StartCoroutine(SendFormCoroutine(VoiceModerationEndpoint, form));
        }

        private IEnumerator SendFormCoroutine(string endpoint, WWWForm form)
        {
            using (UnityWebRequest www = UnityWebRequest.Post(PlaysafeBaseURL + endpoint, form))
            {
                www.SetRequestHeader("Authorization", "Bearer " + appKey);
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"PlaySafeManager: {www.error}");
                    Debug.Log(www.downloadHandler.text);
                }
                else
                {
                    ProcessModerationResponse(www.downloadHandler.text);
                }
            }
        }

        private void ProcessModerationResponse(string jsonResponse)
        {
            try
            {
                PlaySafeActionResponse response = JsonConvert.DeserializeObject<PlaySafeActionResponse>(jsonResponse);
                if (response.Ok)
                {
                    Recommendation recommendation = response.Data.Recommendation;
                    DateTime serverTime = DateTime.Parse(response.Data.ServerTime, null, System.Globalization.DateTimeStyles.RoundtripKind);
                    Debug.Log($"[ProcessModerationResponse] Server time: {serverTime}");
                    if (recommendation.HasViolation && recommendation.Actions.Count > 0)
                    {

                        OnActionEvent?.Invoke(recommendation.Actions[0], serverTime);
                    }
                }
                else
                {
                    Debug.Log($"PlaySafeManager: Operation failed: {response.Message}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("PlaySafeManager: Could not parse moderation response.");
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// Reports a user via a POST call.
        /// </summary>
        public IEnumerator ReportUser(string reporterUserId, string targetUserId, string eventType)
        {
            PlayerReportRequest reportRequest = new PlayerReportRequest
            {
                reporterPlayerUserId = reporterUserId,
                targetPlayerUserId = targetUserId
            };

            string json = JsonConvert.SerializeObject(reportRequest);
            byte[] jsonToSend = new System.Text.UTF8Encoding().GetBytes(json);
            string url = PlaysafeBaseURL + ReportEndpoint + "/" + eventType;

            UnityWebRequest www = new UnityWebRequest(url, "POST");
            www.uploadHandler = new UploadHandlerRaw(jsonToSend);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Authorization", "Bearer " + appKey);

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("ReportUser error: " + www.error);
                Debug.Log(www.downloadHandler.text);
            }
            else
            {
                Debug.Log("PlaySafeManager: Report upload complete!");
                Debug.Log(www.downloadHandler.text);
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (GetTelemetry == null || GetTelemetry() == null)
            {
                return;
            }
            
            if (!hasFocus)
            {
                TryEndSession(GetTelemetry().UserId);
            }
            else
            {
                TryStartSession(GetTelemetry().UserId);
            }
        }

        private void OnApplicationPause (bool isPaused)
        {
            /*
            if (!isActiveAndEnabled || !gameObject.activeInHierarchy)
            {
                return;
            }

            if (GetTelemetry == null || GetTelemetry() == null)
            {
                return;
            }
            
            StartCoroutine(PauseHandler(isPaused));
            */
            
            if (GetTelemetry == null || GetTelemetry() == null)
            {
                return;
            }
            
            if (isPaused)
            {
                TryEndSession(GetTelemetry().UserId);
            }
            else
            {
                TryStartSession(GetTelemetry().UserId);
            }
        }
        
        private IEnumerator PauseHandler (bool isPaused)
        {
            yield return null; // Let Unity settle down a bit

            if (isPaused)
            {
                TryEndSession(GetTelemetry().UserId);
            }
            else
            {
                TryStartSession(GetTelemetry().UserId);
            }
        }

        // Helper method to check if we can change session state
        private bool CanChangeSessionState()
        {
            if (Time.time - _lastSessionChangeTime < MIN_SESSION_CHANGE_INTERVAL)
            {
                Debug.Log("PlaySafeManager: Session state change too frequent, ignoring request");
                return false;
            }
                
            _lastSessionChangeTime = Time.time;
            return true;
        }

        // Public methods to safely trigger session changes
        public void TryStartSession(string playerUserId)
        {
            if (string.IsNullOrEmpty(playerUserId))
            {
                Debug.LogError("PlaySafeManager: Cannot start session with null or empty user ID");
                return;
            }

            // Check if we're already in the desired state or transitioning to it
            if (_currentSessionState == SessionState.Active || _currentSessionState == SessionState.Starting)
            {
                if (_currentUserId == playerUserId)
                {
                    Debug.Log("PlaySafeManager: Session already active or starting for this user");
                    return;
                }
                else
                {
                    Debug.Log("Different user ending current session");
                    // Different user, end current session first
                    TryEndSession(_currentUserId);
                }
            }

            // Check cooldown period
            if (!CanChangeSessionState())
                return;

            // Cancel any active session coroutine
            if (_activeSessionCoroutine != null)
            {
                StopCoroutine(_activeSessionCoroutine);
                _activeSessionCoroutine = null;
            }

            // Start new session
            _currentSessionState = SessionState.Starting;
            _currentUserId = playerUserId;
            _activeSessionCoroutine = StartCoroutine(StartSessionInternal(playerUserId));
        }

        public void TryEndSession(string playerUserId)
        {
            if (string.IsNullOrEmpty(playerUserId))
            {
                Debug.LogError("PlaySafeManager: Cannot end session with null or empty user ID");
                return;
            }

            // Check if we're already in the desired state or transitioning to it
            if (_currentSessionState == SessionState.Inactive || _currentSessionState == SessionState.Ending)
            {
                Debug.Log("PlaySafeManager: Session already inactive or ending");
                return;
            }

            // Check if this is for a different user than the current session
            if (_currentUserId != playerUserId)
            {
                Debug.LogWarning($"PlaySafeManager: Attempting to end session for user {playerUserId} but active session is for {_currentUserId}");
                return;
            }

            // Check cooldown period
            if (!CanChangeSessionState())
                return;

            // Cancel any active session coroutine
            if (_activeSessionCoroutine != null)
            {
                StopCoroutine(_activeSessionCoroutine);
                _activeSessionCoroutine = null;
            }

            // End session
            _currentSessionState = SessionState.Ending;
            _activeSessionCoroutine = StartCoroutine(EndSessionInternal(playerUserId));
        }

        /// <summary>
        /// Starts a new session. If a session is already running (and was not properly ended), it is automatically ended and marked as decayed.
        /// </summary>
        /// <param name="playerUserId">The unique identifier for the player.</param>
        private IEnumerator StartSessionInternal(string playerUserId)
        {
            string url = PlaysafeBaseURL + "/player/session/start";
            var requestBody = new
            {
                playerUserId = playerUserId
            };

            string json = JsonConvert.SerializeObject(requestBody);
            byte[] jsonToSend = Encoding.UTF8.GetBytes(json);

            using (UnityWebRequest www = new UnityWebRequest(url, "POST"))
            {
                www.uploadHandler = new UploadHandlerRaw(jsonToSend);
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/json");
                www.SetRequestHeader("Authorization", "Bearer " + appKey);

                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError("StartSession error: " + www.error);
                    Debug.Log(www.downloadHandler.text);
                    _currentSessionState = SessionState.Inactive;
                }
                else
                {
                    Debug.Log("Session started successfully");
                    Debug.Log(www.downloadHandler.text);
                    _currentSessionState = SessionState.Active;
                }
                
                _activeSessionCoroutine = null;
            }
        }

        /// <summary>
        /// Ends the current session. This call always returns a consistent response even if no session was active.
        /// </summary>
        /// <param name="playerUserId">The unique identifier for the player.</param>
        private IEnumerator EndSessionInternal(string playerUserId)
        {
            string url = PlaysafeBaseURL + "/player/session/end";
            var requestBody = new
            {
                playerUserId = playerUserId
            };

            string json = JsonConvert.SerializeObject(requestBody);
            byte[] jsonToSend = Encoding.UTF8.GetBytes(json);

            using (UnityWebRequest www = new UnityWebRequest(url, "POST"))
            {
                www.uploadHandler = new UploadHandlerRaw(jsonToSend);
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/json");
                www.SetRequestHeader("Authorization", "Bearer " + appKey);

                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError("EndSession error: " + www.error);
                    Debug.Log(www.downloadHandler.text);
                }
                else
                {
                    Debug.Log("Session ended successfully");
                    Debug.Log(www.downloadHandler.text);
                }
                
                _currentSessionState = SessionState.Inactive;
                _currentUserId = null;
                _activeSessionCoroutine = null;
            }
        }

        // Public methods to maintain backward compatibility
        /// <summary>
        /// Starts a new session. If a session is already running (and was not properly ended), it is automatically ended and marked as decayed.
        /// </summary>
        /// <param name="playerUserId">The unique identifier for the player.</param>
        public IEnumerator StartSession(string playerUserId)
        {
            TryStartSession(playerUserId);
            
            // Wait until the session is no longer in Starting state
            while (_currentSessionState == SessionState.Starting)
            {
                yield return null;
            }
            
            // Return success based on final state
            yield return _currentSessionState == SessionState.Active;
        }

        /// <summary>
        /// Ends the current session. This call always returns a consistent response even if no session was active.
        /// </summary>
        /// <param name="playerUserId">The unique identifier for the player.</param>
        public IEnumerator EndSession(string playerUserId)
        {
            TryEndSession(playerUserId);
            
            // Wait until the session is no longer in Ending state
            while (_currentSessionState == SessionState.Ending)
            {
                yield return null;
            }
            
            // Return success based on final state
            yield return _currentSessionState == SessionState.Inactive;
        }

        #endregion

        #region Web API Calls

        private IEnumerator GetProductAIConfig()
        {
            using (UnityWebRequest www = UnityWebRequest.Get(PlaysafeBaseURL + "/remote-config?playerUserId=" + GetTelemetry().UserId))
            {
                www.SetRequestHeader("Authorization", "Bearer " + appKey);
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError(www.error);
                    Debug.Log(www.downloadHandler.text);
                }
                else
                {
                    ProcessRemoteConfig(www.downloadHandler.text);
                }
            }
        }

        private void ProcessRemoteConfig(string jsonResponse)
        {
            try
            {
                RemoteConfigVoiceAIResponse response = JsonConvert.DeserializeObject<RemoteConfigVoiceAIResponse>(jsonResponse);
                if (response.Ok)
                {
                    RemoteConfigVoiceAIData config = response.Data;
                    float samplingRate = Mathf.Clamp(config.SamplingRate, 0f, 1f);
                    _recordingIntermissionSeconds = samplingRate > 0.000001f
                        ? Mathf.Max(0, (int)((RecordingDurationSeconds / samplingRate) - RecordingDurationSeconds))
                        : int.MaxValue;
                    
                    _silenceThreshold = config.AudioSilenceThreshold;
                    Debug.Log($"Silence Threshold: {_silenceThreshold}");
                }
                else
                {
                    Debug.Log($"PlaySafeManager: Remote config failed: {response.Message}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("PlaySafeManager: Could not parse remote config response.");
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// Gets the currently active poll for the product.
        /// </summary>
        public IEnumerator GetActivePoll()
        {
            string url = PlaysafeBaseURL + "/sensei/poll/active";

            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                www.SetRequestHeader("Authorization", "Bearer " + appKey);
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError("GetActivePoll error: " + www.error);
                    Debug.Log(www.downloadHandler.text);
                }
                else
                {
                    Debug.Log("Active poll retrieved successfully");
                    Debug.Log(www.downloadHandler.text);
                }
            }
        }

        /// <summary>
        /// Casts a vote for a specific poll.
        /// </summary>
        /// <param name="pollId">The ID of the poll to vote on</param>
        /// <param name="userId">The ID of the user casting the vote</param>
        /// <param name="response">The user's response/vote</param>
        public IEnumerator CastVote(string pollId, string userId, string response)
        {
            string url = PlaysafeBaseURL + "/sensei/poll/vote";
            var requestBody = new
            {
                pollId = pollId,
                userId = userId,
                response = response
            };

            string json = JsonConvert.SerializeObject(requestBody);
            byte[] jsonToSend = Encoding.UTF8.GetBytes(json);

            using (UnityWebRequest www = new UnityWebRequest(url, "POST"))
            {
                www.uploadHandler = new UploadHandlerRaw(jsonToSend);
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/json");
                www.SetRequestHeader("Authorization", "Bearer " + appKey);

                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError("CastVote error: " + www.error);
                    Debug.Log(www.downloadHandler.text);
                }
                else
                {
                    Debug.Log("Vote cast successfully");
                    Debug.Log(www.downloadHandler.text);
                }
            }
        }

        /// <summary>
        /// Gets the voting results for a specific poll.
        /// </summary>
        /// <param name="pollId">The ID of the poll to get results for</param>
        public IEnumerator GetPollResults(string pollId)
        {
            string url = PlaysafeBaseURL + "/sensei/poll/results/" + pollId;

            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                www.SetRequestHeader("Authorization", "Bearer " + appKey);
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError("GetPollResults error: " + www.error);
                    Debug.Log(www.downloadHandler.text);
                }
                else
                {
                    Debug.Log("Poll results retrieved successfully");
                    Debug.Log(www.downloadHandler.text);
                }
            }
        }                
        /// <summary>
        /// Gets the current status of a player including any active violations.
        /// </summary>
        /// <param name="playerUserId">The unique identifier for the player.</param>
        public IEnumerator GetPlayerStatus(string playerUserId)
        {
            string url = PlaysafeBaseURL + "/player/status?userId=" + playerUserId;

            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                www.SetRequestHeader("Authorization", "Bearer " + appKey);
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError("GetPlayerStatus error: " + www.error);
                    Debug.Log(www.downloadHandler.text);
                }
                else
                {
                    Debug.Log("Player status retrieved successfully");
                    Debug.Log(www.downloadHandler.text);
                    
                    try
                    {
                        PlayerStatusResponse response = JsonConvert.DeserializeObject<PlayerStatusResponse>(www.downloadHandler.text);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("Could not parse player status response.");
                        Debug.LogException(e);
                    }
                }
            }
        }

        #endregion

        #region Data Classes

        public class PlayerReportRequest
        {
            public string reporterPlayerUserId;
            public string targetPlayerUserId;
        }

        public class AudioEventRequestData
        {
            public string UserId;
            public string RoomId;
            public string Language;
        }

        #endregion
    }
}
