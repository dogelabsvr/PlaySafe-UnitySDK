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

namespace Player
{
    public class SenseiPollCastVoteData {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("pollId")] 
        public string PollId { get; set; }

        [JsonProperty("userId")]
        public string UserId { get; set; }

        [JsonProperty("response")]
        public string Response { get; set; }
    } 

    public class SenseiPollCastVoteResponse { 
        [JsonProperty("ok")]
        public bool Ok { get; set; }

        [JsonProperty("data")]
        public SenseiPollCastVoteData Data { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }

    public class ActiveSenseiPollData {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("question")] 
        public string Question { get; set; }

        [JsonProperty("options")]
        public List<string> Options { get; set; }

        [JsonProperty("expiresAt")]
        public string ExpiresAt { get; set; }

        [JsonProperty("personaId")]
        public string PersonaId { get; set; }

        [JsonProperty("productId")]
        public string ProductId { get; set; }

        [JsonProperty("isActive")]
        public bool IsActive { get; set; }
    }

    public class ActiveSenseiPollResponse {
        [JsonProperty("ok")]
        public bool Ok { get; set; }

        [JsonProperty("data")]
        public ActiveSenseiPollData Data { get; set; }  

        [JsonProperty("message")]
        public string Message { get; set; }
    }

    public class SenseiPollVoteResultsData {
        [JsonProperty("votes")]
        public int Votes { get; set; }

        [JsonProperty("breakdown")]
        public Dictionary<string, int> Breakdown { get; set; }
    }

    public class SenseiPollVoteResultsResponse {
        [JsonProperty("ok")]
        public bool Ok { get; set; }

        [JsonProperty("data")] 
        public SenseiPollVoteResultsData Data { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }


}