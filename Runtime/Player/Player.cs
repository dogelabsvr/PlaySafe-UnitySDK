using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace Player
{
    public class PlaySafePlayer {
        private PlaySafeAPI _api;

        public PlaySafePlayer(string appKey) {
          this._api = new PlaySafeAPI(appKey, "/player");
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
  }
}