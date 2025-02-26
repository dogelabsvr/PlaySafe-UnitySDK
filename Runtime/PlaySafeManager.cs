using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Moderation;
using Newtonsoft.Json;
using Player;
using RemoteConfig;
using Sensei;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace PlaySafe.Runtime {
  public class PlaySafeManager: MonoBehaviour {

       // user facing script 
       public PlaySafeModeration Moderation;
       public PlaySafeRemoteConfig RemoteConfig;
       public PlaySafePlayer Player;
       public PlaySafeSensei Sensei;
       
       public static PlaySafeManager Instance;

       #region Inspector & Debug Fields
       [SerializableField] private string appKey = "";
       [SerializableField] private RemoteConfigVoiceAIData remoteConfig;


        [Header("Configuration")]
        [SerializeField] private bool debugEnableRecord = false;
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
       	
		
       public void Awake()
       {
           Moderation = new PlaySafeModeration(appKey);
           RemoteConfig = new PlaySafeRemoteConfig(appKey);
           Player = new PlaySafePlayer(appKey);
           Sensei = new PlaySafeSensei(appKey);

           Instance = this;

          // Load the remote config

          RemoteConfig.ProcessRemoteConfig();

            
           // PlaySafeCore core =new PlayerSafeCore(appKey)
           // core.LoadRemoteConfg((newConfig) => { remoteConfig = newConfig})
           // LoadRemoteConfig( 
       }
      
    private void Update()
    {
       Debug.Log("PlaySafe.Update()");
    }


        private const string playsafeBaseURL = "https://dl-voice-ai.dogelabs.workers.dev";
        private const string voiceModerationEndpoint = "/products/moderation";
        private const string reportEndpoint = "/products/moderation";
        
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
        public Func<ModerationPlayerData> GetTelemetry { private get; set; }

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

        #region Web Requests
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

        #endregion

        #region Data Classes

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
