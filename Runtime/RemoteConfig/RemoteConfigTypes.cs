namespace PlaySafe.Runtime.RemoteConfig {


  public class RemoteConfigData
  {
    [JsonProperty("samplingRate")]
    public float SamplingRate { get; set; }

    [JsonProperty("audioSilenceThresholdDb")]
    public float AudioSilenceThresholdDb { get; set;}

    [JsonProperty("playerStatsExpiryInDays")]
    public int PlayerStatsExpiryInDays { get; set; }
  }
}