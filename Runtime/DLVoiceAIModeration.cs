using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace _DL.PlaySafe
{
    public class DLVoiceAIModeration : MonoBehaviour
    {
        private const string playsafeBaseURL = "https://dl-voice-ai.dogelabs.workers.dev";
        private const string voiceModerationEndpoint = "/products/moderation";
        private const string reportEndpoint = "/products/moderation";
        
        #region Singleton & Initialization

        public static DLVoiceAIModeration Instance;

        private bool _isInitialized = false;

        /// <summary>
        /// Must be set to a delegate that returns whether recording is permitted.
        /// </summary>
        public Func<bool> CanRecord { private get; set; }

        /// <summary>
        /// Must be set to provide telemetry data.
        /// </summary>
        public Func<DLVoiceTelemetry> GetTelemetry { private get; set; }

        /// <summary>
        /// Called when an action is returned from voice moderation.
        /// </summary>
        public Action<ActionItem> OnActionEvent { private get; set; }

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
                Debug.LogError("DLVoiceAIModeration is already initialized");
                return;
            }

            _isInitialized = true;
            Setup();
        }

        private void Setup()
        {
            if (GetMicrophoneDeviceCount() <= 0)
            {
                Debug.LogError("DLVoiceAIModeration: No microphone found");
            }
            StartCoroutine(GetProductAIConfig());
            _lastRecording.Restart();
        }

        #endregion

        #region Inspector & Debug Fields

        [Header("Configuration")]
        [SerializeField] private bool debugEnableRecord = false;
        [SerializeField] private string appKey;
        [SerializeField] private float silenceThreshold = 0.02f;

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

        #endregion

        #region Unity Lifecycle

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

        #region Microphone & Recording Helpers

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

        #region Audio Processing

        /// <summary>
        /// Converts an AudioClip to a WAV file byte array and checks if it is silent.
        /// </summary>
        /// <param name="clip">The AudioClip to convert.</param>
        /// <returns>A tuple containing the WAV file bytes and a flag indicating if the clip is silent.</returns>
// Create a persistent MemoryStream once
        private MemoryStream reusableStream = new MemoryStream();

        public (byte[] wavFileBytes, bool isSilent) AudioClipToWav(AudioClip clip)
        {
            if (clip == null)
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
                if (isSilent && Mathf.Abs(sample) > silenceThreshold)
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
            DLVoiceTelemetry telemetry = GetTelemetry();
            WWWForm form = new WWWForm();
            form.AddField("userId", telemetry.UserId);
            form.AddField("roomId", telemetry.RoomId);
            return form;
        }

        private IEnumerator SendAudioClipForAnalysisCoroutine(AudioClip clip)
        {
            if (clip == null)
            {
                Debug.LogError("DLVoiceAIModeration: AudioClip is null.");
                yield break;
            }

            var (wavFileBytes, isSilent) = AudioClipToWav(clip);
            if (isSilent)
            {
                Debug.Log("DLVoiceAIModeration: The AudioClip is silent. Skipping upload.");
                yield break;
            }

            WWWForm form = SetupForm();
            form.AddBinaryData("audio", wavFileBytes, "audio.wav", "audio/wav");

            yield return StartCoroutine(SendFormCoroutine(voiceModerationEndpoint, form));
        }

        private IEnumerator SendFormCoroutine(string endpoint, WWWForm form)
        {
            using (UnityWebRequest www = UnityWebRequest.Post(playsafeBaseURL + endpoint, form))
            {
                www.SetRequestHeader("Authorization", "Bearer " + appKey);
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"DLVoiceAIModeration: {www.error}");
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
                DLVoiceAIActionResponse response = JsonConvert.DeserializeObject<DLVoiceAIActionResponse>(jsonResponse);
                if (response.Ok)
                {
                    Recommendation recommendation = response.Data.Recommendation;
                    if (recommendation.HasViolation && recommendation.Actions.Count > 0)
                    {
                        OnActionEvent?.Invoke(recommendation.Actions[0]);
                    }
                }
                else
                {
                    Debug.Log($"DLVoiceAIModeration: Operation failed: {response.Message}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("DLVoiceAIModeration: Could not parse moderation response.");
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
            string url = playsafeBaseURL + reportEndpoint + "/" + eventType;

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
                Debug.Log("DLVoiceAIModeration: Report upload complete!");
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
                StartCoroutine(EndSession(GetTelemetry().UserId));
            }
            else
            {
                StartCoroutine(StartSession(GetTelemetry().UserId));
            }
        }

        private void OnApplicationPause(bool isPaused)
        {
            if (GetTelemetry == null || GetTelemetry() == null)
            {
                return;
            }
            if (isPaused)
            {
                StartCoroutine(EndSession(GetTelemetry().UserId));
            }
            else
            {
                StartCoroutine(StartSession(GetTelemetry().UserId));
            }
        }


        /// <summary>
        /// Starts a new session. If a session is already running (and was not properly ended), it is automatically ended and marked as decayed.
        /// </summary>
        /// <param name="playerUserId">The unique identifier for the player.</param>
        public IEnumerator StartSession(string playerUserId)
        {
            string url = playsafeBaseURL + "/player/session/start";
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
                }
                else
                {
                    Debug.Log("Session started successfully");
                    Debug.Log(www.downloadHandler.text);
                }
            }
        }

        /// <summary>
        /// Ends the current session. This call always returns a consistent response even if no session was active.
        /// </summary>
        /// <param name="playerUserId">The unique identifier for the player.</param>
        public IEnumerator EndSession(string playerUserId)
        {
            string url = playsafeBaseURL + "/player/session/end";
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
            }
        }


        private IEnumerator GetProductAIConfig()
        {
            using (UnityWebRequest www = UnityWebRequest.Get(playsafeBaseURL + "/remote-config"))
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
                }
                else
                {
                    Debug.Log($"DLVoiceAIModeration: Remote config failed: {response.Message}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("DLVoiceAIModeration: Could not parse remote config response.");
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// Gets the currently active poll for the product.
        /// </summary>
        public IEnumerator GetActivePoll()
        {
            string url = playsafeBaseURL + "/sensei/poll/active";

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
            string url = playsafeBaseURL + "/sensei/poll/vote";
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
            string url = playsafeBaseURL + "/sensei/poll/results/" + pollId;

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

        #endregion

        #region Data Classes

        public class PlayerReportRequest
        {
            public string reporterPlayerUserId;
            public string targetPlayerUserId;
        }

        public class DLVoiceTelemetry
        {
            public string UserId;
            public string RoomId;
        }

        // The following classes are assumed to be defined elsewhere in your project.
        // public class DLVoiceAIActionResponse { public bool Ok; public string Message; public DLVoiceAIActionData Data; }
        // public class DLVoiceAIActionData { public Recommendation Recommendation; }
        // public class Recommendation { public bool HasViolation; public List<ActionItem> Actions; }
        // public class ActionItem { /* ... */ }
        // public class RemoteConfigVoiceAIResponse { public bool Ok; public string Message; public RemoteConfigVoiceAIData Data; }
        // public class RemoteConfigVoiceAIData { public float SamplingRate; }

        #endregion
    }
}
