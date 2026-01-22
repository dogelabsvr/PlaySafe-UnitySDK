// using System;
// using System.Threading;
// using _DL.PlaySafe;
// using UnityEngine;
// #if NORMCORE
// using Normal.Realtime;


// public class PlaySafeNormcoreDemoIntegration : MonoBehaviour
// {
//     [SerializeField, Tooltip("The PlaySafeManager component to use for the demo.") ] PlaySafeManager playSafeManager;
//     [SerializeField] private Realtime realtime;
//     [SerializeField] private RealtimeAvatarManager _realtimeAvatarManager;
    
//     async void Start()
//     {
//         playSafeManager.CanRecord = CanRecord;
//         playSafeManager.GetTelemetry = GetTelemetry;
//         playSafeManager.OnPolicyViolationEvent = OnPolicyViolationEvent;
//         playSafeManager.OnPlaySafeInitialized = OnPlaySafeInitialized;
        
//         playSafeManager.Initialize();

//         // Example: Get player status
//         // var playerStatus = await playSafeManager.GetPlayerStatusAsync();
//         // if (playerStatus != null && playerStatus.Ok)
//         // {
//         //     Debug.Log($"Player has violation: {playerStatus.Data.HasViolation}");
//         // }

//         // Example: Sensei poll management
//         // string personaId = "your-persona-id"; // From PlaySafe dashboard
//         // var poll = await playSafeManager.GetActivePollAsync(personaId);
//         // 
//         // if (poll != null && poll.Ok && poll.Data != null)
//         // {
//         //     var vote = await playSafeManager.CastVoteAsync(poll.Data.Id, "yes");
//         //     var results = await playSafeManager.GetPollResultsAsync(poll.Data.Id);
//         // }
//     }

//     private void OnPlaySafeInitialized(PlaySafeManager playSafeManager)
//     {
//         Debug.Log("[invoked] PlaySafeManager initialized", playSafeManager);
//     }
    
//     // When a user is banned / or timed out 
//     private void OnPolicyViolationEvent(ActionItem actionEvent, DateTime serverTime)
//     {
//         string duration = actionEvent.DurationInMinutes >= 60 ? 
//             $"{(actionEvent.DurationInMinutes / 60f).ToString("F1")} hours" :
//             $"{actionEvent.DurationInMinutes} minutes";

//         DateTime bannedUntil = serverTime + System.TimeSpan.FromMinutes(actionEvent.DurationInMinutes);
//         string msgToUser = $"Voice chat disabled for {duration}. This can happen due to using slurs, fighting, or general disrespectful behavior";
//         // TODO: Notify the user they were banned 

//         // TODO: Turn off their microphone until the bannedUntil date passes
//     }
    
//     // When to record a users microphone 
//     private bool CanRecord()
//     {
//         return IsVoiceMuted() &&
//                IsConnectedToRoom() &&
//                GetRoomPlayerCount() >= 2; // Only record if there are other players in the room
//         // Alternatively, you can simply return true when testing in the editor
//     }
    
//     private bool IsVoiceMuted()
//     {
//         // TODO: Implement your own logic to determine if a player's mic is muted
//         throw new NotImplementedException();
//     }

//     private bool IsConnectedToRoom()
//     {
//         return realtime.connected;
//     }

//     public int GetRoomPlayerCount()
//     {
//         if (IsConnectedToRoom())
//         {
//             return _realtimeAvatarManager.avatars.Count;
//         }
        
//         return 0;
//     }

//     // Used to identify a player when an event is sent to PlaySafe - e.g. an audio event
//     private PlaySafeManager.AudioEventRequestData GetTelemetry()
//     {
//         string userId = "1234"; // TODO: Get user account id / platform user id
//         string language = Application.systemLanguage.ToString();
        
//         // The public username of the player. This value is encrypted before storage
//         string userName = "ExampleUsername"; // TODO: Get public player username
//         string roomId = "";
        
//         if (IsConnectedToRoom())
//         {
//             roomId = realtime.room.name;
//         }
        
//         PlaySafeManager.AudioEventRequestData telemetry = new PlaySafeManager.AudioEventRequestData()
//         {
//             UserId = userId,
//             UserName = userName, // Optional (having this allows you to search for the user by username in the PlaySafe dashboard)
//             RoomId = roomId,
//             Language = language,
//         };

//         return telemetry;
//     }
// }

// #endif