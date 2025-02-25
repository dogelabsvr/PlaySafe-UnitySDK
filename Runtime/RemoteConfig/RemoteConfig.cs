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
namespace RemoteConfig
{
    public class PlaySafeRemoteConfig
    {
        private PlaySafeAPI _api;
        private string _appKey;
        private string _playsafeBaseURL;
        private int _recordingIntermissionSeconds = 60;  // May be updated from remote config
        private const int RecordingDurationSeconds = 10;

        public PlaySafeRemoteConfig(string appKey)
        {
            this._appKey = appKey;
            this._api = new PlaySafeAPI(appKey, "/remote-config");
        }

        public IEnumerator GetProductAIConfig()
        {
            using (UnityWebRequest www = UnityWebRequest.Get(_playsafeBaseURL + "/remote-config"))
            {
                www.SetRequestHeader("Authorization", "Bearer " + _appKey);
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

        public void ProcessRemoteConfig(string jsonResponse)
        {
            try
            {
                RemoteConfigVoiceAIResponse response =
                    JsonConvert.DeserializeObject<RemoteConfigVoiceAIResponse>(jsonResponse);
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
    }
}