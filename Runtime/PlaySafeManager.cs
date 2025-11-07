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
using System.Net.Http;
using System.Net.Http.Headers;
using JetBrains.Annotations;

namespace _DL.PlaySafe
{
    public class PlaySafeManager : MonoBehaviour
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        [Header("Logging")]
        [SerializeField] private PlaySafeLogLevel logLevel = PlaySafeLogLevel.Info;
        
        private const string PlaysafeBaseURL = "https://dl-voice-ai.dogelabs.workers.dev";
        private const string VoiceModerationEndpoint = "/products/moderation";
        private const string PlayTestDevBaseEndpoint = "/dev";
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
        public Action<ActionItem, DateTime> OnActionEvent { private get; set; }

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
            StartCoroutine(SyncProductIsTakingNotesCoroutine());
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
				StopRecording(shouldSendAudioForProcessing);
			}
            else if (ShouldRecord() && !_isRecording)
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
            if (!_shouldRecordNotesFetched)
                return false;
                
            if (Application.isEditor && debugEnableRecord && !_isRecording)
                return true;

            // For continuous notes recording - start immediately when not recording
            if (_shouldRecordPlayTestNotes && !_isRecording)
                return CanRecord();

            return (!_isRecording || _shouldRecordPlayTestNotes) &&
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
            Log("PlaySafeManager: Recording started" + (_shouldRecordPlayTestNotes ? " (continuous notes recording)" : "") + (ShouldRecord() ? " (should record)" : " (should not record)") + (CanRecord() ? " (can record)" : " (cannot record)"));
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
            LogWarning("PlaySafeManager Audioclip length: " + clip.length + ", sample rate: " + sampleRate + ", channels: " + channelCount);
            return clip;
        }


        private void StopRecording(bool shouldSendAudioClip = true)
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

			if(shouldSendAudioClip && _hasFocus) {
                Log("PlaySafeManager: Sending audio for processing");
            	StartCoroutine(SendAudioClipForAnalysisCoroutine(_audioClipRecording));
			}
            else
            {
                LogWarning($"PlaySafeManager: Recording cancelled – shouldSendAudioClip = {shouldSendAudioClip}, Game Has Focus = {_hasFocus}. This can happen if you mute your mic during recording");
            }

            _isRecording = false;
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

                        OnActionEvent?.Invoke(recommendation.Actions[0], serverTime);
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
            string url = AddTokenToUrl($"{PlaysafeBaseURL}{ReportEndpoint}/{eventType}");

            var reportRequest = new PlayerReportRequest
            {
                reporterPlayerUserId = GetTelemetry().UserId,
                targetPlayerUserId = targetUserId
            };

            string json = JsonConvert.SerializeObject(reportRequest);

            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = content;
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", appKey);

            HttpResponseMessage httpResponse;
            try
            {
                httpResponse = await _httpClient.SendAsync(request).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogError($"ReportUser network error: {ex.Message}");
                LogException(ex);
                return null;
            }

            string responseBody = await httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!httpResponse.IsSuccessStatusCode)
            {
                LogError($"ReportUser HTTP {(int)httpResponse.StatusCode}: {httpResponse.ReasonPhrase}");
                Log(responseBody);
                return null;
            }

            try
            {
                Log("PlaySafeManager: Report upload complete!");
                Log(responseBody);
                var result = JsonConvert.DeserializeObject<ModerationEventResponse>(responseBody);
                return result;
            }
            catch (Exception ex)
            {
                LogError("Could not parse report user response.");
                LogException(ex);
                return null;
            }
        }

        /// <summary>
        /// Undoes a report on a user via a POST call (async version).
        /// </summary>
        [ItemCanBeNull]
        public async Task<PlaySafeApiResponse> UnReportUserAsync(string targetUserId, string eventType)
        {
            string url = AddTokenToUrl($"{PlaysafeBaseURL}{ReportEndpoint}/{eventType}/undo");

            var reportRequest = new PlayerReportRequest
            {
                reporterPlayerUserId = GetTelemetry().UserId,
                targetPlayerUserId = targetUserId
            };

            string json = JsonConvert.SerializeObject(reportRequest);

            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = content;
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", appKey);

            HttpResponseMessage httpResponse;
            try
            {
                httpResponse = await _httpClient.SendAsync(request).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogError($"ReportUser network error: {ex.Message}");
                LogException(ex);
                return null;
            }

            string responseBody = await httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!httpResponse.IsSuccessStatusCode)
            {
                LogError($"ReportUser HTTP {(int)httpResponse.StatusCode}: {httpResponse.ReasonPhrase}");
                Log(responseBody);
                return null;
            }

            try
            {
                Log("PlaySafeManager: Report upload complete!");
                Log(responseBody);
                var result = JsonConvert.DeserializeObject<PlaySafeApiResponse>(responseBody);
                return result;
            }
            catch (Exception ex)
            {
                LogError("Could not parse report user response.");
                LogException(ex);
                return null;
            }
        }

        private bool _hasFocus = true;
        private void OnApplicationFocus(bool hasFocus)
        {
            if (!Application.isEditor)
            {
                _hasFocus = hasFocus;
            }
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
            string url = AddTokenToUrl($"{PlaysafeBaseURL}/player/session/pulse");
            string playerUserId = GetTelemetry().UserId;
            string playerUsername = GetTelemetry().UserName;
            
            var requestBody = new
            {
                playerUserId,   
                playerUsername = string.IsNullOrEmpty(playerUsername) ? null : playerUsername
            };
            string json = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = content;
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", appKey);

            HttpResponseMessage httpResponse;
            try
            {
                Debug.Log($"[SendSessionPulse] Sending session pulse");
                httpResponse = await _httpClient.SendAsync(request).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogError($"SendSessionPulse network error: {ex.Message}");
                LogException(ex);
                return;
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
            string url = AddTokenToUrl($"{PlaysafeBaseURL}/sensei/personas/{personaId}/polls/active/single");

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", appKey);

            HttpResponseMessage httpResponse;
            try
            {
                httpResponse = await _httpClient.SendAsync(request).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogError($"GetActivePoll network error: {ex.Message}");
                LogException(ex);
                return null;
            }

            string json = await httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!httpResponse.IsSuccessStatusCode)
            {
                LogError($"GetActivePoll HTTP {(int)httpResponse.StatusCode}: {httpResponse.ReasonPhrase}");
                Log(json);
                return null;
            }

            try
            {
                var result = JsonConvert.DeserializeObject<ActiveSenseiPollResponse>(json);
                Log("Active poll retrieved successfully");
                return result;
            }
            catch (Exception ex)
            {
                LogError("Could not parse active poll response.");
                LogException(ex);
                return null;
            }
        }

        /// <summary>
        /// Casts a vote for a specific poll.
        /// </summary>
        /// <param name="pollId">The ID of the poll to vote on</param>
        /// <param name="response">The user's response/vote</param>
        public async Task<SenseiPollCastVoteResponse?> CastVoteAsync(string pollId, string response)
        {
            string url = AddTokenToUrl($"{PlaysafeBaseURL}/sensei/polls/{pollId}/votes");
            string playerUserId = GetTelemetry().UserId;
            
            var requestBody = new
            {
                userId = playerUserId,
                response = response
            };

            string json = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = content;
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", appKey);

            HttpResponseMessage httpResponse;
            try
            {
                httpResponse = await _httpClient.SendAsync(request).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogError($"CastVote network error: {ex.Message}");
                LogException(ex);
                return null;
            }

            string responseJson = await httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!httpResponse.IsSuccessStatusCode)
            {
                LogError($"CastVote HTTP {(int)httpResponse.StatusCode}: {httpResponse.ReasonPhrase}");
                Log(responseJson);
                return null;
            }

            try
            {
                Log("CastVote response: " + responseJson);
                var result = JsonConvert.DeserializeObject<SenseiPollCastVoteResponse>(responseJson);
                Log("Vote cast successfully");
                return result;
            }
            catch (Exception ex)
            {
                LogError("Could not parse cast vote response.");
                LogException(ex);
                return null;
            }
        }

        /// <summary>
        /// Gets the voting results for a specific poll.
        /// </summary>
        /// <param name="pollId">The ID of the poll to get results for</param>
        public async Task<SenseiPollVoteResultsResponse?> GetPollResultsAsync(string pollId)
        {
            string url = AddTokenToUrl($"{PlaysafeBaseURL}/sensei/polls/{pollId}/results");

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", appKey);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogError($"GetPollResults network error: {ex.Message}");
                LogException(ex);
                return null;
            }

            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                LogError($"GetPollResults HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
                Log(json);
                return null;
            }

            try
            {
                var result = JsonConvert.DeserializeObject<SenseiPollVoteResultsResponse>(json);
                Log("Poll results retrieved successfully");
                return result;
            }
            catch (Exception ex)
            {
                LogError("Could not parse poll results response.");
                LogException(ex);
                return null;
            }
        }
       
        /// <summary>
        /// Gets the voting results for a specific poll.
        /// </summary>
        /// <param name="pollId">The ID of the poll to get results for</param>
        public async Task<SenseiPlayerPollVotesResponse?> GetPlayerPollVotesAsync(string pollId)
        {
            string playerUserId = GetTelemetry().UserId;

            string url = AddTokenToUrl($"{PlaysafeBaseURL}/sensei/polls/{pollId}/votes/player?userId={playerUserId}");

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", appKey);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogError($"GetPlayerPollVotes network error: {ex.Message}");
                LogException(ex);
                return null;
            }

            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                LogError($"GetPlayerPollVotes HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
                Log(json);
                return null;
            }

            try
            {
                var result = JsonConvert.DeserializeObject<SenseiPlayerPollVotesResponse>(json);
                Log("Player poll votes retrieved successfully");
                return result;
            }
            catch (Exception ex)
            {
                LogError("Could not parse player poll votes response.");
                LogException(ex);
                return null;
            }
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

            string url = AddTokenToUrl($"{PlaysafeBaseURL}/player/status?userId={playerUserId}");

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", appKey);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogError($"GetPlayerStatus network error: {ex.Message}");
                LogException(ex);
                return null;
            }

            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                LogError($"GetPlayerStatus HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
                Log(json);
                return null;
            }

            try
            {
                var result = JsonConvert.DeserializeObject<PlayerStatusResponse>(json);
                Log("Player status retrieved successfully");
                // Optionally inspect result for violations etc. here
                return result;
            }
            catch (Exception ex)
            {
                LogError("Could not parse player status response.");
                LogException(ex);
                return null;
            }
        }

         /// <summary>
        /// Gets the current status of a player including any active violations.
        /// </summary>
        public async Task<BanAppealResponse?> AppealBanAsync(string appealReason)
        {
            string url = AddTokenToUrl($"{PlaysafeBaseURL}/ban-appeals");
            string playerUsername = GetTelemetry().UserName;
            
            var requestBody = new 
            {
                playerUsername,
                appealReason = !string.IsNullOrEmpty(appealReason) ? appealReason : null
            };
            

            string json = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = content;
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", appKey);

            HttpResponseMessage httpResponse;
            try
            {
                httpResponse = await _httpClient.SendAsync(request).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogError($"AppealBan network error: {ex.Message}");
                LogException(ex);
                return null;
            }

            string responseJson = await httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!httpResponse.IsSuccessStatusCode)
            {
                LogError($"AppealBan HTTP {(int)httpResponse.StatusCode}: {httpResponse.ReasonPhrase}");
                Log(responseJson);
                return null;
            }

            try
            {
                Log("AppealBan response: " + responseJson);
                var result = JsonConvert.DeserializeObject<BanAppealResponse>(responseJson);
                Log("Ban appeal successful");
                return result;
            }
            catch (Exception ex)
            {
                LogError("Could not parse ban appeal response.");
                LogException(ex);
                return null;
            }
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

            string url = AddTokenToUrl(PlaysafeBaseURL + PlayTestDevBaseEndpoint + "/notes");

            string playerUserId = GetTelemetry().UserId;

            var requestBody = new
            {
                playerUserId
            };

            string json = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = content;
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", appKey);

            HttpResponseMessage httpResponse;
            try
            {
                httpResponse = await _httpClient.SendAsync(request).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _shouldRecordPlayTestNotes = false;
                
                LogError($"StartTakingNotes network error: {ex.Message}");
                LogException(ex);
                return null;
            }

            string responseJson = await httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!httpResponse.IsSuccessStatusCode)
            {
                LogError($"StartTakingNotes HTTP {(int)httpResponse.StatusCode}: {httpResponse.ReasonPhrase}");
                Log(responseJson);
                return null;
            }

            try
            {
                Log("StartTakingNotes response: " + responseJson);
                var result = JsonConvert.DeserializeObject<PlayTestNotesResponse>(responseJson);
                
                if (result != null && result.Ok && result.Data != null)
                {
                    _playTestNotesId = result.Data.Id;
                    _shouldRecordPlayTestNotes = true;
                    Log($"Started taking notes with ID: {_playTestNotesId}");
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _shouldRecordPlayTestNotes = false;
                
                LogError("Could not parse StartTakingNotes response.");
                LogException(ex);
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

            
            string url = AddTokenToUrl(PlaysafeBaseURL + PlayTestDevBaseEndpoint + "/notes/stop");

            HttpContent content = null;
            
            if (!string.IsNullOrEmpty(_playTestNotesId))
            {
                var requestBody = new
                {
                    playTestNotesId = _playTestNotesId
                };

                string json = JsonConvert.SerializeObject(requestBody);
                content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = content;
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", appKey);

            HttpResponseMessage httpResponse;
            try
            {
                httpResponse = await _httpClient.SendAsync(request).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogError($"StopTakingNotes network error: {ex.Message}");
                LogException(ex);
                return null;
            }

            string responseJson = await httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!httpResponse.IsSuccessStatusCode)
            {
                LogError($"StopTakingNotes HTTP {(int)httpResponse.StatusCode}: {httpResponse.ReasonPhrase}");
                Log(responseJson);
                return null;
            }

            try
            {
                Log("StopTakingNotes response: " + responseJson);
                var result = JsonConvert.DeserializeObject<PlayTestNotesResponse>(responseJson);
                
                if (result != null && result.Ok)
                {
                    _playTestNotesId = null;
                    _shouldRecordPlayTestNotes = false;
                    Log("Stopped taking notes");
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _shouldRecordPlayTestNotes = true;
                
                LogError("Could not parse StopTakingNotes response.");
                LogException(ex);
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
            string url = AddTokenToUrl(PlaysafeBaseURL + PlayTestDevBaseEndpoint + "/active-notes?playerUserId=" + playerUserId);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", appKey);

            HttpResponseMessage httpResponse;
            try
            {
                httpResponse = await _httpClient.SendAsync(request).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogError($"GetProductIsTakingNotes network error: {ex.Message}");
                LogException(ex);
                return null;
            }

            string responseJson = await httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!httpResponse.IsSuccessStatusCode)
            {
                LogError($"GetProductIsTakingNotes HTTP {(int)httpResponse.StatusCode}: {httpResponse.ReasonPhrase}");
                Log(responseJson);
                return null;
            }

            try
            {
                Log("GetProductIsTakingNotes response: " + responseJson);
                var result = JsonConvert.DeserializeObject<PlayTestProductIsTakingNotesResponse>(responseJson);
                
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
            catch (Exception ex)
            {
                LogError("Could not parse GetProductIsTakingNotes response.");
                LogException(ex);
                return null;
            }
        }

        IEnumerator SyncProductIsTakingNotesCoroutine()
        {
            Debug.Log("PlaySafeManager: Starting product notes sync");
            
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

        public void SetCustomAuth()
        {
            _hasCustomAuth = true;
            _authToken = jwt;
        }
    }
}
