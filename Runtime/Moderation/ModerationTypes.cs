using System;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace Moderation {
    
    #region Request Types

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

    #endregion

    #region Response Types
    public class ModerationRecommendationResponse
    {
        // Indicates whether the operation was successful
        [JsonProperty("ok")]
        public bool Ok { get; set; }

        // Contains the data returned by the operation
        [JsonProperty("data")]
        public DataRecommendation Data { get; set; }

        // A message describing the result of the operation
        [JsonProperty("message")]
        public string Message { get; set; }
    }

    public class ModerationRecommendationData
    {
        // The recommendation data
        [JsonProperty("recommendation")]
        public Recommendation Recommendation { get; set; }
    }

    public class Recommendation
    {
        // The name of the policy
        [JsonProperty("policyName")]
        public string PolicyName { get; set; }

        // Indicates whether there was a violation
        [JsonProperty("hasViolation")]
        public bool HasViolation { get; set; }

        // A list of actions to be taken
        [JsonProperty("actions")]
        public List<ActionItem> Actions { get; set; }
    }

    public class ActionItem
    {
        [JsonProperty("action")]
        public string Action { get; set; }

        [JsonProperty("durationInMinutes")]
        public int DurationInMinutes { get; set; }

        [JsonProperty("actionEndDate")]
        public DateTime ActionEndDate { get; set; }

        [JsonProperty("reason")]
        public string Reason { get; set; }
    }
    #endregion

}