using PlaySafe.Runtime.Core;

namespace PlaySafe.Runtime.RemoteConfig {
  public class RemoteConfig {
    private PlaySafeAPI _api;

    public RemoteConfig(string appKey) {
      this._api = new PlaySafeAPI(appKey, "/remote-config");
    }

    public ApiResponse<RemoteConfigData> GetRemoteConfig() {
      // TODO: Implement
    }
  }
}