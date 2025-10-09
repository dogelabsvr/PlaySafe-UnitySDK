#nullable enable
using System;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace _DL.PlaySafe  
{

    public class PlaySafeApiResponse : PlaySafeApiResponse<object> {}
    
    public class PlaySafeApiResponse<T>
    {
        [JsonProperty("ok")]
        public bool Ok { get; set; }
        
        [JsonProperty("data")]
        public T? Data { get; set; }
        
        [JsonProperty("message")]
        public string Message { get; set; }
    }

    public class PlaySafeActionResponse : PlaySafeApiResponse<DataRecommendation> {}
    

    public class DataRecommendation
    {
        // The recommendation data
        [JsonProperty("recommendation")]
        public Recommendation Recommendation { get; set; }
        
        // Server time
        [JsonProperty("serverTime")]
        public string ServerTime { get; set; }
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

    //#region Player Status
    public class PlayerStatusResponse : PlaySafeApiResponse<PlayerStatusData> {}

    public class PlayerStatusData
    {
        [JsonProperty("hasViolation")]
        public bool HasViolation { get; set; }
        
        [JsonProperty("activeActionLog")]
        public ActionLog ActiveActionLog { get; set; }
        
        [JsonProperty("serverTime")]
        public DateTime ServerTime { get; set; }
    }

    public class ActionLog
    {
        [JsonProperty("actionValue")]
        public string ActionValue { get; set; }
        
        [JsonProperty("endDate")]
        public DateTime EndDate { get; set; }
        
        [JsonProperty("isActive")]
        public bool IsActive { get; set; }
        
        [JsonProperty("durationInMinutes")]
        public int DurationInMinutes { get; set; }
    }
    //#endregion

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


    public class RemoteConfigVoiceAIResponse: PlaySafeApiResponse<RemoteConfigVoiceAIData>{}


    public class RemoteConfigVoiceAIData
    {
        [JsonProperty("samplingRate")]
        public float SamplingRate { get; set; }

        [JsonProperty("isSmartSamplingEnabled")]
        public bool IsSmartSamplingEnabled { get; set;}

        [JsonProperty("audioSilenceThreshold")]
        public float AudioSilenceThreshold { get; set;}

        [JsonProperty("playerStatsExpiryInDays")]
        public int PlayerStatsExpiryInDays { get; set; }
        
        [JsonProperty("sessionPulseIntervalSeconds")]
        public int SessionPulseIntervalSeconds { get; set; }
    }

    //
    public class SenseiPollVoteResponse {
        [JsonProperty("value")]
        public string Value { get; set; }
    }

    public class SenseiPollCastVoteData {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("pollId")] 
        public string PollId { get; set; }

        [JsonProperty("playerUserId")]
        public string UserId { get; set; }

        [JsonProperty("response")]
        public SenseiPollVoteResponse Response { get; set; }

        [JsonProperty("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("updatedAt")]
        public DateTime UpdatedAt { get; set; }
    } 

    public class SenseiPollCastVoteResponse : PlaySafeApiResponse<SenseiPollCastVoteData> {}

    public class ActiveSenseiPollData {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("question")] 
        public string Question { get; set; }   
        
        [JsonProperty("assetUrl")] 
        public string AssetUrl { get; set; }        
        
        [JsonProperty("assetType")] 
        public string AssetType { get; set; }

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

    public class ActiveSenseiPollResponse: PlaySafeApiResponse<ActiveSenseiPollData> {}

    public class SenseiPollVoteResultsData {
        [JsonProperty("votes")]
        public int Votes { get; set; }

        [JsonProperty("breakdown")]
        public Dictionary<string, int> Breakdown { get; set; }
    }

    public class SenseiPollVoteResultsResponse: PlaySafeApiResponse<SenseiPollVoteResultsData> {}

    public class SenseiPlayerPollVote
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("pollId")]
        public string PollId { get; set; }

        [JsonProperty("playerUserId")]
        public string PlayerUserId { get; set; }

        [JsonProperty("hashedPlayerUsername")]
        public string HashedPlayerUsername { get; set; }

        [JsonProperty("response")]
        public SenseiPollVoteResponse Response { get; set; }

        [JsonProperty("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("updatedAt")]
        public DateTime UpdatedAt { get; set; }
    }
    
    public class SenseiPlayerPollVotesResponse : PlaySafeApiResponse<List<SenseiPlayerPollVote>> {}

    //#region Ban Appeal
    public class BanAppealResponse: PlaySafeApiResponse<bool> {}
    //#endregion

    //#region Moderation events
    public class ModerationEvent
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("productId")]
        public string ProductId { get; set; }

        [JsonProperty("reporterPlayerUserId")]
        public string ReporterPlayerUserId { get; set; }

        [JsonProperty("reporterRole")]
        public string ReporterRole { get; set; }

        [JsonProperty("targetPlayerUserId")]
        public string TargetPlayerUserId { get; set; }

        [JsonProperty("eventType")]
        public string EventType { get; set; }

        [JsonProperty("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("updatedAt")]
        public DateTime UpdatedAt { get; set; }
    }

    public class ModerationEventResponse : PlaySafeApiResponse<ModerationEvent>  {}

    //#endregion
}