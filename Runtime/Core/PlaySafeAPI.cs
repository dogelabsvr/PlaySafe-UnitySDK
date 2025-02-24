namespace PlaySafe.Runtime.Core {
  public class ApiResponse<T> {
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
    private string _baseApiUrl;

    public PlaySafeAPI(string appKey, string baseApiUrl) {
      this._appKey = appKey;
      this._baseApiUrl = PlaySafeSettings.PlaySafeBaseURL + baseApiUrl;
    }
    
    public PostRequest<T>(string endpoint, object body) {
      // TODO: Implement
    }

    public GetRequest<T>(string endpoint) {
      // TODO: Implement
    }

  }
}