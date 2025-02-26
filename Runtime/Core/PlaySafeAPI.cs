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

namespace Core {
    public class PlaySafeApiResponse<T> {
        [JsonProperty("ok")]
        public bool Ok { get; set; }

        [JsonProperty("data")]
        public T Data { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }

    public class PlaySafeAPI {
        // Base URL for PlaySafe API endpoints
        private string _appKey;
        private string _baseEndpointUrl;
        private static string _playSafeApiUrl = "https://dl-voice-ai.dogelabs.workers.dev";

        public PlaySafeAPI(string appKey, string baseApiUrl) {
            this._appKey = appKey;
            this._baseEndpointUrl = _playSafeApiUrl + baseApiUrl;
        }
    
        public PlaySafeApiResponse<T> PostRequest<T>(string endpoint, object body) {
            // TODO: Implement
            return 0;
        }

        public PlaySafeApiResponse<T> FormPostRequest<T>(string endpoint, object body)
        {
            return 0;
        }

        public T GetRequest<T>(string endpoint) {
            // TODO: Implement
            return 0;
        }

    }
}