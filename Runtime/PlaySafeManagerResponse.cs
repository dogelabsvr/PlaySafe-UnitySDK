using System;
using Newtonsoft.Json;
using System.Collections.Generic;

public class PlaySafeActionResponse
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

public class DataRecommendation
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

//#region Player Status
public class PlayerStatusResponse
{
    [JsonProperty("ok")]
    public bool Ok { get; set; }
    
    [JsonProperty("data")]
    public PlayerStatusData Data { get; set; }
    
    [JsonProperty("message")]
    public string Message { get; set; }
}

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

//
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

