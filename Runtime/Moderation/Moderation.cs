using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Core;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace Moderation
{
    public class PlaySafeModeration
    {
        private string _appKey;
        private string _playsafeBaseURL;
        
        private PlaySafeAPI _api;

        public PlaySafeModeration(string appKey)
        {
            this._appKey = appKey;
            this._api = new PlaySafeAPI(appKey, "/moderation");
        }

        private ProcessAudioEventRequest GetTelemetry()
        {
            string userId = "1234";
            string roomName = "ExampleRoom";
            return new DLVoiceTelemetry()
          
        }

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
            using (UnityWebRequest www = UnityWebRequest.Post(_playsafeBaseURL + endpoint, form))
            {
                www.SetRequestHeader("Authorization", "Bearer " + _appKey);
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
            
            // TODO: cross-check this with production
            string url = _playsafeBaseURL + 
                         "report"+ "/" + eventType;

            UnityWebRequest www = new UnityWebRequest(url, "POST");
            www.uploadHandler = new UploadHandlerRaw(jsonToSend);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Authorization", "Bearer " + _appKey);

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

    }
}
}