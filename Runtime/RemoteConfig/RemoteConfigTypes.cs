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
    public class RemoteConfigVoiceAIResponse
    {
        [JsonProperty("ok")]
        public bool Ok { get; set; }

        [JsonProperty("data")]
        public RemoteConfigVoiceAIData Data { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }

    public class RemoteConfigVoiceAIData
    {
        [JsonProperty("samplingRate")]
        public float SamplingRate { get; set; }

        [JsonProperty("audioSilenceThresholdDb")]
        public float AudioSilenceThresholdDb { get; set;}

        [JsonProperty("playerStatsExpiryInDays")]
        public int PlayerStatsExpiryInDays { get; set; }
    }

}