using PlaySafe.Runtime.Core;

namespace PlaySafe.Runtime.Player {
  public class Player {
    private PlaySafeAPI _api;

    public Player(string appKey) {
      this._api = new PlaySafeAPI(appKey, "/player");
    }

    public void StartSession(){ 
      // TODO: Implement
    }

    public void EndSession() {
      // TODO: Implement
    }
  }
}