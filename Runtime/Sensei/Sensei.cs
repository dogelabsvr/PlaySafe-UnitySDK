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

namespace Sensei {
  public class PlaySafeSensei
  {
      private string _appKey;

      public PlaySafeSensei(string appKey)
      {
          _appKey = appKey;
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
  }
}