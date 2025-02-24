using PlaySafe.Runtime.Core;

namespace PlaySafe.Runtime.Moderation {
  public class Moderation {
    private PlaySafeAPI _api;

    public Moderation(string appKey) {
      this._api = new PlaySafeAPI(appKey, "/moderation");
    }
    
  }
}