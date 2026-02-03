using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;
#if PHOTON_VOICE_DEFINED
    using Photon.Voice;
    using Photon.Voice.Unity;
#endif
using JetBrains.Annotations;

namespace _DL.PlaySafe
{
    public class PlaySafeManager : MonoBehaviour
    {
        [Header("Logging")]
        [SerializeField] private PlaySafeLogLevel logLevel = PlaySafeLogLevel.Info;
        
        private const string PlaysafeBaseURL = "https://dl-voice-ai.dogelabs.workers.dev";
        private const string VoiceModerationEndpoint = "/products/moderation";
        private const string PlayTestDevBaseEndpoint = "/dev";
        private const string ReportEndpoint = "/products/moderation";
        
        #region Singleton & Initialization

        public static PlaySafeManager Instance;

        private bool _isInitialized = false;
        private bool _isPlayerDev = false;
        private bool _hasPlayerDevStatus = false;
        private bool _isFetchingPlayerDevStatus = false;
        private bool _isProductNotesSyncRunning = false;

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
        public Action<ActionItem, DateTime> OnPolicyViolationEvent { private get; set; }

        private float _playerSessionIntervalInSeconds = 60;

        /// <summary>
        /// Call this method to initialize the voice AI moderation system.
        /// </summary>
        public void Initialize()
        {
            Instance = this;

            if (CanRecord == null)
            {
                LogError("Must set CanRecord delegate before initializing");
                return;
            }

            if (GetTelemetry == null)
            {
                LogError("Must set GetTelemetry delegate before initializing");
                return;
            }

            if (_isInitialized)
            {
                LogError("PlaySafeManager is already initialized");
                return;
            }

            _isInitialized = true;
            Setup();

            // Invoke the initialization callback if set
            OnPlaySafeInitialized?.Invoke(this);

            Log($"<color=#00FF00>PlaySafeManager is running!</color>");
        }



        /// <summary>
        /// Called when PlaySafeManager is successfully initialized.
        /// </summary>
        public Action<PlaySafeManager> OnPlaySafeInitialized { get; set; }

        private void Setup()
        {
            if (GetMicrophoneDeviceCount() <= 0)
            {
                LogError("PlaySafeManager: No microphone found");
            }
            StartCoroutine(GetProductAIConfig());
            _lastRecording.Restart();
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

        // Pause/Resume recording fields
        private bool _isPaused = false;
        private bool _previousCanRecordState = false;
        private Stopwatch _activeRecordingTime = new Stopwatch();
        private Stopwatch _pauseTimer = new Stopwatch();
        private const int PauseTimeoutSeconds = 10;
        private const int MinimumAudioDurationSeconds = 1;
        private const int UnityMicSampleRate = 16000;

        #endregion

        #region Playtest related
        private float _syncProductIsTakingNotesIntervalInSeconds = 5.0f;

        public bool ShouldRecordPlayTestNotes => _shouldRecordPlayTestNotes;
        private bool _shouldRecordPlayTestNotes = false;
        private bool _shouldRecordNotesFetched = false;
        private string _playTestNotesId;
        private bool _hasPendingNotes = false;
        private bool _hasCustomAuth = false;
        private string _authToken = null;

        #endregion 

        #region Unity Lifecycle
        private void Start() {
            // Photon specific setup
            #if PHOTON_VOICE_DEFINED
            _photonPlaySafeProcessor = new PhotonPlaySafeProcessor();
            _photonPlaySafeProcessor.playSafeManager = this;
            #endif
            
            StartCoroutine(SendSessionPulseCoroutine());
            StartCoroutine(GetIsPlayerDevCoroutine(startNotesSyncIfDev: true));
        }

        private void Update()
        {
            UpdateDebugInfo();

            if (!_isInitialized)
            {
                Debug.Log("PlaySafeManager is not initialized");
                return;
            }

            if (!HasMicrophonePermission())
            {
                Debug.Log("PlaySafeManager no microphone permissions");
                return;
            }

            // Track CanRecord state for transition detection
            bool currentCanRecord = CanRecord();

            // State transitions: Recording -> Paused (when CanRecord becomes false)
            if (_isRecording && !_isPaused && !currentCanRecord && !Application.isEditor)
            {
                Log($"[StateMachine] RECORDING -> PAUSED (CanRecord became false, accumulated: {GetAccumulatedAudioDuration():F1}s)");
                PauseRecording();
            }
            // State transitions: Paused -> Recording (when CanRecord becomes true)
            else if (_isRecording && _isPaused && currentCanRecord)
            {
                Log($"[StateMachine] PAUSED -> RECORDING (CanRecord became true, total pause time: {_pauseTimer.Elapsed.TotalSeconds:F1}s)");
                ResumeRecording();
            }

            // Check total pause time: if cumulative pause time exceeds threshold, stop and send what we have
            // This prevents exploit where user rapidly toggles mute to avoid accumulating 10s of audio
            // Checked regardless of current state (recording/paused) - if total pause time hits threshold, send immediately
            if (_isRecording && _pauseTimer.Elapsed.TotalSeconds >= PauseTimeoutSeconds)
            {
                bool hasMinimumAudio = GetAccumulatedAudioDuration() >= MinimumAudioDurationSeconds;
                if (hasMinimumAudio)
                {
                    Log($"[StateMachine] -> SEND (total pause time {_pauseTimer.Elapsed.TotalSeconds:F1}s >= {PauseTimeoutSeconds}s threshold, sending {GetAccumulatedAudioDuration():F1}s of audio)");
                }
                else
                {
                    Log($"[StateMachine] -> DISCARD (total pause time {_pauseTimer.Elapsed.TotalSeconds:F1}s >= {PauseTimeoutSeconds}s threshold, discarding {GetAccumulatedAudioDuration():F1}s of audio - below {MinimumAudioDurationSeconds}s minimum)");
                }
                StopRecording(hasMinimumAudio);
            }

            // Check if accumulated audio reached target duration (applies to both Recording and Paused states)
            if (_isRecording && GetAccumulatedAudioDuration() >= RecordingDurationSeconds)
            {
                string currentState = _isPaused ? "PAUSED" : "RECORDING";
                Log($"[StateMachine] {currentState} -> SEND (target duration {RecordingDurationSeconds}s reached)");
                StopRecording(true);
            }

            // Start new recording if conditions are met
            if (ShouldRecord() && !_isRecording)
            {
                Log("[StateMachine] IDLE -> RECORDING (ShouldRecord() returned true)");
                StartRecording();
            }

            _previousCanRecordState = currentCanRecord;
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

        public void SetupPhotonVoice (PhotonVoiceCreatedParams voiceCreatedParams)
        {
            var voice = voiceCreatedParams.Voice as LocalVoiceAudioFloat;
            if (voice != null)
            {
                Debug.Log("SetupPhotonVoice: Photon voice created");
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
            // Don't start recording until we've fetched the notes status at least once
            // if (!_shouldRecordNotesFetched)
            //     return false;
                
            if (Application.isEditor && debugEnableRecord && !_isRecording)
                return true;

            // For continuous notes recording - start immediately when not recording
            if (_shouldRecordPlayTestNotes && !_isRecording)
                return CanRecord();
            var totalSeconds = _lastRecording.Elapsed.TotalSeconds;
            bool timeHasElapsed =   totalSeconds > _recordingIntermissionSeconds;
            return (!_isRecording || _shouldRecordPlayTestNotes) &&timeHasElapsed
                    &&
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
                // For Unity Microphone path: set sample rate and create buffer
                sampleRate = UnityMicSampleRate;
                channelCount = 1;

                string mic = GetDefaultMicrophone();
                _audioClipRecording =
                    Microphone.Start(mic, false, RecordingDurationSeconds, sampleRate);
            }

            // Create buffer for both paths (Unity Mic and Photon use the same buffer)
            CreateNewBuffer();

            _isRecording = true;
            _isPaused = false;
            _lastRecording.Restart();
            _activeRecordingTime.Restart();
            _pauseTimer.Reset();

            Log($"[StateMachine] Recording started - mode: {(isUsingExistingUnityMic ? "Photon" : "UnityMic")}, sampleRate: {sampleRate}, channels: {channelCount}" +
                (_shouldRecordPlayTestNotes ? ", playtest notes: ON" : ""));
        }

        private void PauseRecording()
        {
            if (!_isRecording || _isPaused)
                return;

            _isPaused = true;
            _activeRecordingTime.Stop();
            _pauseTimer.Start(); // Continue accumulating total pause time (not reset)

            if (!isUsingExistingUnityMic)
            {
                // For Unity Microphone: extract recorded samples to buffer, then stop microphone
                string mic = GetDefaultMicrophone();
                int currentPosition = Microphone.GetPosition(mic);

                if (currentPosition > 0 && _audioClipRecording != null)
                {
                    // Extract samples from the AudioClip
                    float[] samples = new float[currentPosition];
                    _audioClipRecording.GetData(samples, 0);

                    // Copy to buffer if there's room
                    int samplesToCopy = Mathf.Min(samples.Length, audioBufferFromExistingMic.Length - _sampleIndex);
                    if (samplesToCopy > 0)
                    {
                        System.Array.Copy(samples, 0, audioBufferFromExistingMic, _sampleIndex, samplesToCopy);
                        _sampleIndex += samplesToCopy;
                    }

                    Log($"[StateMachine] PauseRecording: extracted {samplesToCopy} samples from Unity Microphone");
                }

                Microphone.End(mic);
            }
            // For Photon path: AppendToBuffer will check _isPaused and skip appending

            Log($"[StateMachine] Recording PAUSED - accumulated audio: {GetAccumulatedAudioDuration():F1}s, samples in buffer: {_sampleIndex}");
        }

        private void ResumeRecording()
        {
            if (!_isRecording || !_isPaused)
                return;

            _isPaused = false;
            _pauseTimer.Stop(); // Stop but preserve total pause time (for cumulative tracking)
            _activeRecordingTime.Start();

            if (!isUsingExistingUnityMic)
            {
                // For Unity Microphone: start fresh recording (will continue accumulating to same buffer)
                string mic = GetDefaultMicrophone();
                _audioClipRecording = Microphone.Start(mic, false, RecordingDurationSeconds, sampleRate);
                Log("[StateMachine] ResumeRecording: Unity Microphone restarted");
            }
            // For Photon path: AppendToBuffer will resume appending since _isPaused is now false

            Log($"[StateMachine] Recording RESUMED - continuing from {GetAccumulatedAudioDuration():F1}s, samples in buffer: {_sampleIndex}");
        }
        
        private void CreateNewBuffer ()
        {
            audioBufferFromExistingMic = new float[sampleRate * channelCount * RecordingDurationSeconds]; // 10 seconds of audio
            _sampleIndex = 0;
        }
        
        public void AppendToBuffer(float[] newData)
        {
            // Guard: only append when actively recording (not paused, not stopped)
            if (!_isRecording || _isPaused) return;
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
            if (_sampleIndex == 0) return null;

            // Create a trimmed buffer with only the recorded samples
            float[] trimmedBuffer = new float[_sampleIndex];
            System.Array.Copy(audioBufferFromExistingMic, 0, trimmedBuffer, 0, _sampleIndex);

            AudioClip clip = AudioClip.Create("RecordedAudio", _sampleIndex / channelCount,
                channelCount, sampleRate, false);
            clip.SetData(trimmedBuffer, 0);
            LogWarning($"PlaySafeManager: AudioClip created - length: {clip.length}s, samples: {_sampleIndex}, sample rate: {sampleRate}, channels: {channelCount}");
            return clip;
        }

        /// <summary>
        /// Returns the accumulated audio duration in seconds.
        /// Uses the active recording time stopwatch which only runs while actively recording (not paused).
        /// </summary>
        private double GetAccumulatedAudioDuration()
        {
            return _activeRecordingTime.Elapsed.TotalSeconds;
        }

        private void StopRecording(bool shouldSendAudioClip = true)
        {
            if (!_isRecording)
                return;

            // For Unity Microphone path: extract any remaining samples if not paused
            if (!isUsingExistingUnityMic && !_isPaused)
            {
                string mic = GetDefaultMicrophone();
                int currentPosition = Microphone.GetPosition(mic);

                if (currentPosition > 0 && _audioClipRecording != null)
                {
                    float[] samples = new float[currentPosition];
                    _audioClipRecording.GetData(samples, 0);

                    int samplesToCopy = Mathf.Min(samples.Length, audioBufferFromExistingMic.Length - _sampleIndex);
                    if (samplesToCopy > 0)
                    {
                        System.Array.Copy(samples, 0, audioBufferFromExistingMic, _sampleIndex, samplesToCopy);
                        _sampleIndex += samplesToCopy;
                    }
                }

                Microphone.End(mic);
            }

            // Create AudioClip from buffer (same for both Unity Mic and Photon paths)
            _audioClipRecording = CreateAudioClip();

            if (shouldSendAudioClip && _hasFocus)
            {
                Log($"[StateMachine] StopRecording: SENDING {GetAccumulatedAudioDuration():F1}s of audio ({_sampleIndex} samples) for processing");
                StartCoroutine(SendAudioClipForAnalysisCoroutine(_audioClipRecording));
            }
            else
            {
                LogWarning($"[StateMachine] StopRecording: DISCARDED - shouldSendAudioClip={shouldSendAudioClip}, hasFocus={_hasFocus}, audio={GetAccumulatedAudioDuration():F1}s");
            }

            // Reset all recording state
            _isRecording = false;
            _isPaused = false;
            _activeRecordingTime.Reset();
            _pauseTimer.Reset();
            Log("[StateMachine] Recording STOPPED - state reset to IDLE");
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

        #region URL Helper
        
        /// <summary>
        /// Adds the authentication token to the URL as a query parameter if custom auth is enabled.
        /// </summary>
        /// <param name="url">The URL to add the token to</param>
        /// <returns>The URL with the token added if custom auth is enabled, otherwise the original URL</returns>
        private string AddTokenToUrl(string url)
        {
            if (!_hasCustomAuth || string.IsNullOrEmpty(_authToken))
                return url;
            
            // Check if the URL already has query parameters
            string separator = url.Contains("?") ? "&" : "?";
            return $"{url}{separator}token={_authToken}";
        }
        
        #endregion

        #region Audio Processing

        /// <summary>
        /// Converts an AudioClip to a WAV file byte array and checks if it is silent.
        /// </summary>
        /// <param name="clip">The AudioClip to convert.</param>
        /// <returns>A tuple containing the WAV file bytes and a flag indicating if the clip is silent.</returns>
        // Create a persistent MemoryStream once
        private MemoryStream _reusableStream = new MemoryStream();

        public (byte[] wavFileBytes, bool isSilent) AudioClipToFile(AudioClip clip)
        {
            if (!clip)
                throw new ArgumentNullException(nameof(clip));

            // Reset the reusable MemoryStream.
            _reusableStream.SetLength(0);
            _reusableStream.Position = 0;

            int sampleCount = clip.samples * clip.channels;
            // Reserve header space (44 bytes) for the WAV header.
            _reusableStream.Write(new byte[44], 0, 44);

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
            _reusableStream.Write(audioBytes, 0, audioBytes.Length);

            // Write the WAV header at the beginning.
            _reusableStream.Position = 0;
            WriteWavHeader(_reusableStream, clip, audioBytes.Length);

            // Return the complete byte array and the silence flag.
            byte[] result = _reusableStream.ToArray();
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
            
            if(telemetry.UserName != null) 
            {
                form.AddField("username", telemetry.UserName);
            }

            return form;
        }

        WaitForEndOfFrame WaitForEndOfFrame = new WaitForEndOfFrame();
        
        private IEnumerator SendAudioClipForAnalysisCoroutine(AudioClip clip)
        {
            if (clip == null)
            {
                LogError("PlaySafeManager: AudioClip is null.");
                yield break;
            }

            var (wavFileBytes, isSilent) = AudioClipToFile(clip);
            if (isSilent)
            {
                Log("PlaySafeManager: The AudioClip is silent. Skipping upload.");
                yield break;
            }
            yield return WaitForEndOfFrame;
            WWWForm form = SetupForm();

            int audioDurationInSeconds = (int) Math.Max(1,clip.length);  
            form.AddField("durationInSeconds", audioDurationInSeconds);
            
            form.AddBinaryData("audio", wavFileBytes, "audio.wav", "audio/wav");
            yield return WaitForEndOfFrame;


            if(_shouldRecordPlayTestNotes) {
                form.AddField("playerUserId", GetTelemetry().UserId);

                if(!string.IsNullOrEmpty(_playTestNotesId)) {
                    form.AddField("playTestNotesId", _playTestNotesId);
                }

                Debug.Log("PlaySafeManager: Taking notes, sending audio for transcription");

                string url = AddTokenToUrl(PlayTestDevBaseEndpoint + "/notes/transcripts");
                yield return StartCoroutine(SendFormCoroutine(url, form));
            }else { // Disable moderation when taking playtest notes
                yield return StartCoroutine(SendFormCoroutine(AddTokenToUrl(VoiceModerationEndpoint), form));
            }
            
        }

        public IEnumerator SendTextForAnalysisCoroutine(string text)
        {
            yield return WaitForEndOfFrame;
            WWWForm form = SetupForm();
            form.AddField("text", text);
            yield return StartCoroutine(SendFormCoroutine(AddTokenToUrl(VoiceModerationEndpoint), form));
        }

        private IEnumerator SendFormCoroutine(string endpoint, WWWForm form)
        {
            using (UnityWebRequest www = UnityWebRequest.Post(PlaysafeBaseURL + endpoint, form))
            {
                www.SetRequestHeader("Authorization", "Bearer " + appKey);
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    LogError($"PlaySafeManager: {www.error}");
                    Log(www.downloadHandler.text);
                }
                else
                {
                    yield return WaitForEndOfFrame;
                    ProcessModerationResponse(www.downloadHandler.text);
                }
            }
        }

        private void ProcessModerationResponse(string jsonResponse)
        {
            // No need to try processing moderation responses when taking playtest notes
            if(_shouldRecordPlayTestNotes) {
                return;
            }

            try
            {
                PlaySafeActionResponse response = JsonConvert.DeserializeObject<PlaySafeActionResponse>(jsonResponse);
                if (response.Ok)
                {
                    Recommendation recommendation = response.Data.Recommendation;
                    DateTime serverTime = DateTime.Parse(response.Data.ServerTime, null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();
                    Log($"[ProcessModerationResponse] Server time: {serverTime}");
                    if (recommendation.HasViolation && recommendation.Actions.Count > 0)
                    {

                        OnPolicyViolationEvent?.Invoke(recommendation.Actions[0], serverTime);
                    }
                }
                else
                {
                    Log($"PlaySafeManager: Operation failed: {response.Message}");
                }
            }
            catch (Exception e)
            {
                LogError("PlaySafeManager: Could not parse moderation response."); 
                LogException(e);
            }
        }

        // TODO: Deprecate this method in favor of the async version + mention the removal of the reporter user id argument
        [Obsolete("Use ReportUserAsync instead. The reporterUserId parameter has been removed and is now automatically retrieved from telemetry. This method will be removed in a future version.")]
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
            string url = AddTokenToUrl(PlaysafeBaseURL + ReportEndpoint + "/" + eventType);

            UnityWebRequest www = new UnityWebRequest(url, "POST");
            www.uploadHandler = new UploadHandlerRaw(jsonToSend);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Authorization", "Bearer " + appKey);

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                LogError("ReportUser error: " + www.error);
                Log(www.downloadHandler.text);
            }
            else
            {
                Log("PlaySafeManager: Report upload complete!");
                Log(www.downloadHandler.text);
            }
        }

        /// <summary>
        /// Reports a user via a POST call (async version).
        /// </summary>
        [ItemCanBeNull]
        public async Task<ModerationEventResponse> ReportUserAsync(string targetUserId, string eventType)
        {
            var tcs = new TaskCompletionSource<ModerationEventResponse>();
            StartCoroutine(ReportUserCoroutine(targetUserId, eventType, tcs));
            return await tcs.Task;
        }

        private IEnumerator ReportUserCoroutine(string targetUserId, string eventType, TaskCompletionSource<ModerationEventResponse> tcs)
        {
            string url = AddTokenToUrl($"{PlaysafeBaseURL}{ReportEndpoint}/{eventType}");

            var reportRequest = new PlayerReportRequest
            {
                reporterPlayerUserId = GetTelemetry().UserId,
                targetPlayerUserId = targetUserId
            };

            string json = JsonConvert.SerializeObject(reportRequest);
            byte[] jsonToSend = new System.Text.UTF8Encoding().GetBytes(json);

            UnityWebRequest www = new UnityWebRequest(url, "POST");
            www.uploadHandler = new UploadHandlerRaw(jsonToSend);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Authorization", "Bearer " + appKey);

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                LogError("ReportUser error: " + www.error);
                Log(www.downloadHandler.text);
                tcs.SetResult(null);
            }
            else
            {
                try
                {
                    Log("PlaySafeManager: Report upload complete!");
                    Log(www.downloadHandler.text);
                    var result = JsonConvert.DeserializeObject<ModerationEventResponse>(www.downloadHandler.text);
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    LogError("Could not parse report user response.");
                    LogException(ex);
                    tcs.SetResult(null);
                }
            }

            www.Dispose();
        }

        /// <summary>
        /// Undoes a report on a user via a POST call (async version).
        /// </summary>
        [ItemCanBeNull]
        public async Task<PlaySafeApiResponse> UnReportUserAsync(string targetUserId, string eventType)
        {
            var tcs = new TaskCompletionSource<PlaySafeApiResponse>();
            StartCoroutine(UnReportUserCoroutine(targetUserId, eventType, tcs));
            return await tcs.Task;
        }

        private IEnumerator UnReportUserCoroutine(string targetUserId, string eventType, TaskCompletionSource<PlaySafeApiResponse> tcs)
        {
            string url = AddTokenToUrl($"{PlaysafeBaseURL}{ReportEndpoint}/{eventType}/undo");

            var reportRequest = new PlayerReportRequest
            {
                reporterPlayerUserId = GetTelemetry().UserId,
                targetPlayerUserId = targetUserId
            };

            string json = JsonConvert.SerializeObject(reportRequest);
            byte[] jsonToSend = new System.Text.UTF8Encoding().GetBytes(json);

            UnityWebRequest www = new UnityWebRequest(url, "POST");
            www.uploadHandler = new UploadHandlerRaw(jsonToSend);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Authorization", "Bearer " + appKey);

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                LogError("UnReportUser error: " + www.error);
                Log(www.downloadHandler.text);
                tcs.SetResult(null);
            }
            else
            {
                try
                {
                    Log("PlaySafeManager: UnReport upload complete!");
                    Log(www.downloadHandler.text);
                    var result = JsonConvert.DeserializeObject<PlaySafeApiResponse>(www.downloadHandler.text);
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    LogError("Could not parse unreport user response.");
                    LogException(ex);
                    tcs.SetResult(null);
                }
            }

            www.Dispose();
        }

        private bool _hasFocus = true;
        private void OnApplicationFocus(bool hasFocus)
        {
            if (!Application.isEditor)
            {
                _hasFocus = hasFocus;
            }

            // When we regain focus, refresh dev status and start notes sync if applicable.
            if (hasFocus)
            {
                StartCoroutine(GetIsPlayerDevCoroutine(startNotesSyncIfDev: true));
            }
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            // if (pauseStatus)
            // {
            //     StopRecording();
            // }
        }

        private  void OnApplicationQuit()
        {
            // StopRecording();
        }
        
        #endregion

        #region Player Sessions
        
        IEnumerator SendSessionPulseCoroutine()
        {
            var wait = new WaitForSecondsRealtime(_playerSessionIntervalInSeconds);
            
            while (true)
            {
                yield return wait; // yields back to Unity; gameplay continues

                var task = SendSessionPulseAsync();

                // Poll the task without blocking
                while (!task.IsCompleted)
                    yield return null; // yield each frame until it’s done

                if (task.IsFaulted)
                    Debug.LogException(task.Exception);
            }
        }


        async Task SendSessionPulseAsync()
        {
            var tcs = new TaskCompletionSource<bool>();
            StartCoroutine(SendSessionPulseCoroutineInternal(tcs));
            await tcs.Task;
        }

        private IEnumerator SendSessionPulseCoroutineInternal(TaskCompletionSource<bool> tcs)
        {
            string url = AddTokenToUrl($"{PlaysafeBaseURL}/player/session/pulse");
            string playerUserId = GetTelemetry().UserId;
            string playerUsername = GetTelemetry().UserName;

            var requestBody = new
            {
                playerUserId,
                playerUsername = string.IsNullOrEmpty(playerUsername) ? null : playerUsername
            };
            string json = JsonConvert.SerializeObject(requestBody);
            byte[] jsonToSend = new System.Text.UTF8Encoding().GetBytes(json);

            UnityWebRequest www = new UnityWebRequest(url, "POST");
            www.uploadHandler = new UploadHandlerRaw(jsonToSend);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Authorization", "Bearer " + appKey);

            Debug.Log($"[SendSessionPulse] Sending session pulse");
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                LogError($"SendSessionPulse network error: {www.error}");
            }

            www.Dispose();
            tcs.SetResult(true);
        }
        #endregion
        
        #region Web API Helper Methods

        public class ApiRequestOptions
        {
            public string Method { get; set; } = "GET";
            public object RequestBody { get; set; } = null;
            public string SuccessMessage { get; set; } = null;
        }

        public class PlaySafeApiResponse<T>
        {
            public bool Success { get; set; }
            public T Data { get; set; }
            public string ErrorMessage { get; set; }
            public string RawResponse { get; set; }
        }

        /// <summary>
        /// Unified API request method that handles both GET and POST requests using UnityWebRequest
        /// </summary>
        /// <typeparam name="T">The expected response type</typeparam>
        /// <param name="url">The API endpoint URL</param>
        /// <param name="options">Request options including method, body, and success message</param>
        /// <returns>A PlaySafeApiResponse containing the result</returns>
        public async Task<PlaySafeApiResponse<T>> SendApiRequest<T>(string url, ApiRequestOptions options = null)
        {
            if (options == null) options = new ApiRequestOptions();

            var tcs = new TaskCompletionSource<PlaySafeApiResponse<T>>();
            StartCoroutine(SendApiRequestCoroutine(url, options, tcs, typeof(T)));
            return await tcs.Task;
        }

        private IEnumerator SendApiRequestCoroutine<T>(string url, ApiRequestOptions options, TaskCompletionSource<PlaySafeApiResponse<T>> tcs, System.Type responseType)
        {
            yield return StartCoroutine(SendApiRequestCoroutineWithoutTokenUrl(AddTokenToUrl(url), options, tcs, responseType));
        }

        private IEnumerator SendApiRequestCoroutineWithoutTokenUrl<T>(string url, ApiRequestOptions options, TaskCompletionSource<PlaySafeApiResponse<T>> tcs, System.Type responseType)
        {
            UnityWebRequest www = null;

            try
            {
                // Create request based on method
                if (options.Method.ToUpper() == "GET")
                {
                    www = UnityWebRequest.Get(url);
                }
                else if (options.Method.ToUpper() == "POST")
                {
                    www = new UnityWebRequest(url, "POST");
                    www.downloadHandler = new DownloadHandlerBuffer();

                    if (options.RequestBody != null)
                    {
                        string json = JsonConvert.SerializeObject(options.RequestBody);
                        byte[] jsonToSend = new System.Text.UTF8Encoding().GetBytes(json);
                        www.uploadHandler = new UploadHandlerRaw(jsonToSend);
                        www.SetRequestHeader("Content-Type", "application/json");
                    }
                }
                else
                {
                    tcs.SetResult(new PlaySafeApiResponse<T>
                    {
                        Success = false,
                        ErrorMessage = $"Unsupported HTTP method: {options.Method}",
                        Data = default(T)
                    });
                    yield break;
                }

                // Set authorization header
                www.SetRequestHeader("Authorization", "Bearer " + appKey);

                // Send request
                yield return www.SendWebRequest();

                // Handle response
                var response = new PlaySafeApiResponse<T>
                {
                    RawResponse = www.downloadHandler.text,
                    Success = www.result == UnityWebRequest.Result.Success
                };

                if (!response.Success)
                {
                    response.ErrorMessage = www.error;
                    LogError($"{options.Method} request error: {www.error}");
                    Log(www.downloadHandler.text);
                }
                else
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(options.SuccessMessage))
                        {
                            Log(options.SuccessMessage);
                        }
                        Log(www.downloadHandler.text);

                        // Parse response if we have a specific type
                        if (responseType != typeof(object) && responseType != typeof(string) && !string.IsNullOrEmpty(www.downloadHandler.text))
                        {
                            response.Data = JsonConvert.DeserializeObject<T>(www.downloadHandler.text);
                        }
                        else if (responseType == typeof(string))
                        {
                            response.Data = (T)(object)www.downloadHandler.text;
                        }
                    }
                    catch (Exception ex)
                    {
                        response.Success = false;
                        response.ErrorMessage = "Could not parse response.";
                        LogError("Could not parse response.");
                        LogException(ex);
                    }
                }

                tcs.SetResult(response);
            }
            finally
            {
                www?.Dispose();
            }
        }

        #endregion

        #region Web API Calls

        private IEnumerator GetProductAIConfig()
        {
            string url = AddTokenToUrl(PlaysafeBaseURL + "/remote-config?playerUserId=" + GetTelemetry().UserId);
            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                www.SetRequestHeader("Authorization", "Bearer " + appKey);
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    LogError(www.error);
                    Log(www.downloadHandler.text);
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

                    // Override the recording intermission seconds (always be recording) if we are taking playtest notes
                    if(_shouldRecordPlayTestNotes) {
                        _recordingIntermissionSeconds = 0; // Zero intermission for continuous recording when taking notes
                    }else {
                        _recordingIntermissionSeconds = samplingRate > 0.000001f
                        ? Mathf.Max(0, (int)((RecordingDurationSeconds / samplingRate) - RecordingDurationSeconds))
                        : int.MaxValue;
                        
                        Debug.Log(response.Data);
                    }
                    _playerSessionIntervalInSeconds = config.SessionPulseIntervalSeconds;
                    
                    _silenceThreshold = config.AudioSilenceThreshold;
                    Log($"Silence Threshold: {_silenceThreshold}");
                }
                else
                {
                    Log($"PlaySafeManager: Remote config failed: {response.Message}");
                }
            }
            catch (Exception e)
            {
                LogError("PlaySafeManager: Could not parse remote config response."); 
                LogException(e);
            }
        }
        
        /// <summary>
        /// Gets the currently active poll for the product.
        /// </summary>
        public async Task<ActiveSenseiPollResponse?> GetActivePollAsync(string personaId)
        {
            string url = $"{PlaysafeBaseURL}/sensei/personas/{personaId}/polls/active/single";

            var response = await SendApiRequest<ActiveSenseiPollResponse>(url, new ApiRequestOptions
            {
                Method = "GET",
                SuccessMessage = "Active poll retrieved successfully"
            });

            return response.Success ? response.Data : null;
        }

        /// <summary>
        /// Casts a vote for a specific poll.
        /// </summary>
        /// <param name="pollId">The ID of the poll to vote on</param>
        /// <param name="response">The user's response/vote</param>
        public async Task<SenseiPollCastVoteResponse?> CastVoteAsync(string pollId, string response)
        {
            string url = $"{PlaysafeBaseURL}/sensei/polls/{pollId}/votes";
            string playerUserId = GetTelemetry().UserId;

            var requestBody = new
            {
                userId = playerUserId,
                response = response
            };

            var apiResponse = await SendApiRequest<SenseiPollCastVoteResponse>(url, new ApiRequestOptions
            {
                Method = "POST",
                RequestBody = requestBody,
                SuccessMessage = "Vote cast successfully"
            });

            return apiResponse.Success ? apiResponse.Data : null;
        }

        /// <summary>
        /// Gets the voting results for a specific poll.
        /// </summary>
        /// <param name="pollId">The ID of the poll to get results for</param>
        public async Task<SenseiPollVoteResultsResponse?> GetPollResultsAsync(string pollId)
        {
            string url = $"{PlaysafeBaseURL}/sensei/polls/{pollId}/results";

            var response = await SendApiRequest<SenseiPollVoteResultsResponse>(url, new ApiRequestOptions
            {
                Method = "GET",
                SuccessMessage = "Poll results retrieved successfully"
            });

            return response.Success ? response.Data : null;
        }
       
        /// <summary>
        /// Gets the voting results for a specific poll.
        /// </summary>
        /// <param name="pollId">The ID of the poll to get results for</param>
        public async Task<SenseiPlayerPollVotesResponse?> GetPlayerPollVotesAsync(string pollId)
        {
            string playerUserId = GetTelemetry().UserId;
            string url = $"{PlaysafeBaseURL}/sensei/polls/{pollId}/votes/player?userId={playerUserId}";

            var response = await SendApiRequest<SenseiPlayerPollVotesResponse>(url, new ApiRequestOptions
            {
                Method = "GET",
                SuccessMessage = "Player poll votes retrieved successfully"
            });

            return response.Success ? response.Data : null;
        }
                      
        /// <summary>
        /// Gets the current status of a player including any active violations.
        /// </summary>
        public async Task<PlayerStatusResponse?> GetPlayerStatusAsync()
        {
            string playerUserId = GetTelemetry().UserId;
            if (string.IsNullOrEmpty(playerUserId))
            {
                LogError("GetPlayerStatusAsync: No user ID found");
                return null;
            }

            string url = $"{PlaysafeBaseURL}/player/status?userId={playerUserId}";

            var response = await SendApiRequest<PlayerStatusResponse>(url, new ApiRequestOptions
            {
                Method = "GET",
                SuccessMessage = "Player status retrieved successfully"
            });

            return response.Success ? response.Data : null;
        }

         /// <summary>
        /// Appeals a player ban.
        /// </summary>
        public async Task<BanAppealResponse?> AppealBanAsync(string appealReason)
        {
            string url = $"{PlaysafeBaseURL}/ban-appeals";
            string playerUsername = GetTelemetry().UserName;

            var requestBody = new
            {
                playerUsername,
                appealReason = !string.IsNullOrEmpty(appealReason) ? appealReason : null
            };

            var response = await SendApiRequest<BanAppealResponse>(url, new ApiRequestOptions
            {
                Method = "POST",
                RequestBody = requestBody,
                SuccessMessage = "Ban appeal successful"
            });

            return response.Success ? response.Data : null;
        }

        #endregion

        #region Playtest related
        public async Task<PlayTestNotesResponse> StartTakingNotesAsync()
        {
            // We are already taking notes, don't let this run again
            if (_shouldRecordPlayTestNotes)
            {
                return null;
            }

            _shouldRecordPlayTestNotes = true;

            string url = PlaysafeBaseURL + PlayTestDevBaseEndpoint + "/notes";
            string playerUserId = GetTelemetry().UserId;

            var requestBody = new
            {
                playerUserId
            };

            var response = await SendApiRequest<PlayTestNotesResponse>(url, new ApiRequestOptions
            {
                Method = "POST",
                RequestBody = requestBody,
                SuccessMessage = "StartTakingNotes completed"
            });

            if (response.Success && response.Data != null && response.Data.Ok && response.Data.Data != null)
            {
                _playTestNotesId = response.Data.Data.Id;
                _shouldRecordPlayTestNotes = true;
                Log($"Started taking notes with ID: {_playTestNotesId}");
                return response.Data;
            }
            else
            {
                _shouldRecordPlayTestNotes = false;
                LogError("Failed to start taking notes");
                return null;
            }
        }
        
        public async Task<PlayTestNotesResponse> StopTakingNotesAsync()
        {
            // We already stopped recording notes, don't try stopping again
            if (!_shouldRecordPlayTestNotes)
            {
                return null;
            }
            
            // Ensure at least 15 seconds pass (10 seconds + 5 second buffer) before stopping
            double elapsed = _lastRecording.Elapsed.TotalSeconds;
            double delayNeeded = Math.Max(15 - elapsed, 0);
            
            if (delayNeeded > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(delayNeeded)).ConfigureAwait(false);
            }
            
            _shouldRecordPlayTestNotes = false;

            string url = PlaysafeBaseURL + PlayTestDevBaseEndpoint + "/notes/stop";
            object requestBody = null;

            if (!string.IsNullOrEmpty(_playTestNotesId))
            {
                requestBody = new
                {
                    playTestNotesId = _playTestNotesId
                };
            }

            var response = await SendApiRequest<PlayTestNotesResponse>(url, new ApiRequestOptions
            {
                Method = "POST",
                RequestBody = requestBody,
                SuccessMessage = "StopTakingNotes completed"
            });

            if (response.Success && response.Data != null && response.Data.Ok)
            {
                _playTestNotesId = null;
                _shouldRecordPlayTestNotes = false;
                Log("Stopped taking notes");
                return response.Data;
            }
            else
            {
                _shouldRecordPlayTestNotes = true;
                LogError("Failed to stop taking notes");
                return null;
            }
        }

        private async Task<PlayTestProductIsTakingNotesResponse> SyncProductIsTakingNotesAsync()
        {
            if (!_isInitialized)
            {
                return null;
            }
            
            string playerUserId = GetTelemetry().UserId;
            string url = PlaysafeBaseURL + PlayTestDevBaseEndpoint + "/active-notes?playerUserId=" + playerUserId;

            var response = await SendApiRequest<PlayTestProductIsTakingNotesResponse>(url, new ApiRequestOptions
            {
                Method = "GET",
                SuccessMessage = "GetProductIsTakingNotes completed"
            });

            if (response.Success)
            {
                var result = response.Data;
                
                if (result != null && result.Ok && result.Data != null)
                {
                    bool previousState = _shouldRecordPlayTestNotes;
                    _shouldRecordPlayTestNotes = result.Data.IsTakingNotes;
                    _shouldRecordNotesFetched = true;

                    // Override the recording intermission seconds (always be recording) if we are taking playtest notes
                    if(_shouldRecordPlayTestNotes) {
                        _recordingIntermissionSeconds = 0; // Zero intermission for continuous recording
                        
                        // If notes recording just became active, log the change
                        if (!previousState && _shouldRecordPlayTestNotes)
                        {
                            Log("PlaySafeManager: Notes recording activated, continuous recording enabled");
                        }
                    }

                    Log($"Product is taking notes: {_shouldRecordPlayTestNotes}");
                }
                
                return result;
            }
            else
            {
                LogError("Failed to get product taking notes status");
                return null;
            }
        }

        private async Task<PlayerIsDevResponse> GetIsPlayerDevAsync()
        {
            // Default to non-dev unless the API tells us otherwise.
            bool isDev = false;

            if (GetTelemetry == null)
            {
                LogWarning("GetIsPlayerDevAsync: GetTelemetry is not set; assuming non-dev.");
                _isPlayerDev = false;
                return null;
            }

            string playerUserId = GetTelemetry().UserId;
            if (string.IsNullOrEmpty(playerUserId))
            {
                LogWarning("GetIsPlayerDevAsync: No user ID found; assuming non-dev.");
                _isPlayerDev = false;
                return null;
            }

            if (string.IsNullOrEmpty(appKey))
            {
                LogWarning("GetIsPlayerDevAsync: appKey is not set; assuming non-dev.");
                _isPlayerDev = false;
                return null;
            }

            // Note: This endpoint returns { ok, data: { isDev }, message }.
            // We include playerUserId for consistency with other /dev endpoints.
            string url = $"{PlaysafeBaseURL}{PlayTestDevBaseEndpoint}/player-is-dev?playerUserId={playerUserId}";

            var response = await SendApiRequest<PlayerIsDevResponse>(url, new ApiRequestOptions
            {
                Method = "GET"
            });

            if (response.Success)
            {
                var result = response.Data;
                if (result != null && result.Ok && result.Data != null)
                {
                    isDev = result.Data.IsDev;
                }
                else if (result != null && !result.Ok)
                {
                    LogWarning($"GetIsPlayerDev failed: {result.Message}");
                }
                else
                {
                    LogWarning("GetIsPlayerDev returned an unexpected response; assuming non-dev.");
                }

                _isPlayerDev = isDev;
                Log($"Player is dev: {_isPlayerDev}");
                return result;
            }
            else
            {
                LogError("Failed to get player dev status");
                _isPlayerDev = false;
                return null;
            }
        }

        private IEnumerator GetIsPlayerDevCoroutine(bool startNotesSyncIfDev)
        {
            if (_isFetchingPlayerDevStatus)
                yield break;

            _isFetchingPlayerDevStatus = true;

            var task = GetIsPlayerDevAsync();

            while (!task.IsCompleted)
                yield return null;

            _isFetchingPlayerDevStatus = false;
            _hasPlayerDevStatus = true;

            if (task.IsFaulted)
                Debug.LogException(task.Exception);

            if (startNotesSyncIfDev && _isPlayerDev)
            {
                StartCoroutine(SyncProductIsTakingNotesCoroutine());
            }
        }

        private IEnumerator SyncProductIsTakingNotesCoroutine()
        {
            if (_isProductNotesSyncRunning)
                yield break;
            
            _isProductNotesSyncRunning = true;

            // Wait until we have fetched dev status at least once.
            while (!_hasPlayerDevStatus)
                yield return null;

            // Non-devs should never perform notes syncing.
            if (!_isPlayerDev)
            {
                Debug.Log("PlaySafeManager: Skipping product notes sync (non-dev)");
                _isProductNotesSyncRunning = false;
                yield break;
            }

            Debug.Log("PlaySafeManager: Starting product notes sync (dev)");

            // Perform initial sync immediately
            var initialTask = SyncProductIsTakingNotesAsync();
            while (!initialTask.IsCompleted)
                yield return null;
            
            if (initialTask.IsFaulted)
                Debug.LogException(initialTask.Exception);
            
            // Then continue with periodic sync
            var wait = new WaitForSecondsRealtime(_syncProductIsTakingNotesIntervalInSeconds);
            
            while (true)
            {
                yield return wait; // yields back to Unity; gameplay continues

                var task = SyncProductIsTakingNotesAsync();

                // Poll the task without blocking
                while (!task.IsCompleted)
                    yield return null; // yield each frame until it's done

                if (task.IsFaulted)
                    Debug.LogException(task.Exception);
            }
        }
        
        #endregion Playtest related

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
            public string UserName;
            public string Language;
        }

        #endregion
        
        public enum PlaySafeLogLevel
        {
            None    = 0,   // nothing
            Exception   = 1,   // only Exceptions
            Error   = 2,   // only LogError & Exceptions
            Warning = 3,   // + LogWarning
            Info    = 4,   // + Log (regular)
            Verbose = 5    // + super-noisy traces
        }

        public PlaySafeLogLevel GetLogLevel()
        {
            return logLevel;
        }
        public PlaySafeLogLevel SetLogLevel(PlaySafeLogLevel newLogLevel)
        {
            return logLevel = newLogLevel;
        }
        
        
        private void LogError(string msg)
        {
            Log(msg, PlaySafeLogLevel.Error);
        }
        
        private void LogWarning(string msg)
        {
            Log(msg, PlaySafeLogLevel.Warning);
        }
        
        private void LogException(Exception e)
        {
            Log(e.ToString(), PlaySafeLogLevel.Exception);
        }
        
        private void Log(string msg, PlaySafeLogLevel lvl = PlaySafeLogLevel.Info)
        {
            if (lvl > logLevel || logLevel == PlaySafeLogLevel.None) return;

            switch (lvl)
            {
                case PlaySafeLogLevel.Exception:   Debug.LogError(msg);   break;
                case PlaySafeLogLevel.Error:   Debug.LogError(msg);   break;
                case PlaySafeLogLevel.Warning: Debug.LogWarning(msg); break;
                default:                       Debug.Log(msg);        break;
            }
        }

        public async Task<PlayerAuthTokenResponse?> SetCustomAuth()
        {
            // Note: We don't use AddTokenToUrl here since we're getting the token in the first place
            // Note: We don't use AddTokenToUrl here since we're getting the token in the first place
            string url = $"{PlaysafeBaseURL}/auth-urls/player-auth-token";
            string playerUserId = GetTelemetry().UserId;

            var requestBody = new
            {
                playerUserId
            };

            // Special case: don't add token to URL since we're getting the token
            var tcs = new TaskCompletionSource<PlaySafeApiResponse<PlayerAuthTokenResponse>>();
            var options = new ApiRequestOptions
            {
                Method = "POST",
                RequestBody = requestBody,
                SuccessMessage = "SetCustomAuth completed"
            };
            StartCoroutine(SendApiRequestCoroutineWithoutTokenUrl(url, options, tcs, typeof(PlayerAuthTokenResponse)));
            var response = await tcs.Task;

            if (response.Success && response.Data != null)
            {
                var result = response.Data;

                if (result.Ok && result.Data != null)
                {
                    _hasCustomAuth = true;
                    _authToken = result.Data.Token;
                    Log("Custom auth token retrieved successfully");
                }

                return result;
            }
            else
            {
                LogError("Error! Failed to setup custom auth.");
                return null;
            }
        }
    }
}
