// Normcore demo integration. Commented out so the package compiles in projects without Normcore.
// To use it: copy this file into your Assets folder and uncomment everything below this header.

// using System;
// using _DL.PlaySafe;
// using Normal.Realtime;
// using UnityEngine;
//
// // Requires Normcore 2.9.3 or newer (RealtimeAvatarVoice.voiceData). Works with Normcore 3.
// public class PlaySafeNormcoreDemoIntegration : MonoBehaviour
// {
//     [SerializeField, Tooltip("The PlaySafeManager component to use for the demo.") ] PlaySafeManager playSafeManager;
//     [SerializeField] private Realtime realtime;
//     [SerializeField] private RealtimeAvatarManager _realtimeAvatarManager;
//
//     // The local player's voice component. Normcore creates it with the local avatar once connected to a room.
//     private RealtimeAvatarVoice _localVoice;
//
//     async void Start()
//     {
//         playSafeManager.CanRecord = CanRecord;
//         playSafeManager.GetTelemetry = GetTelemetry;
//         playSafeManager.OnPolicyViolationEvent = OnPolicyViolationEvent;
//         playSafeManager.OnPlaySafeInitialized = OnPlaySafeInitialized;
//
//         // PlaySafe reads the audio Normcore already captures (see OnVoiceData) instead of opening the microphone a second time.
//         playSafeManager.isUsingExistingUnityMic = true;
//         playSafeManager.channelCount = 1;
//
//         _realtimeAvatarManager.avatarCreated += OnAvatarCreated;
//         _realtimeAvatarManager.avatarDestroyed += OnAvatarDestroyed;
//         if (_realtimeAvatarManager.localAvatar != null)
//         {
//             AttachLocalVoice(_realtimeAvatarManager.localAvatar);
//         }
//
//         playSafeManager.Initialize();
//
//         // Example: Get player status
//         // var playerStatus = await playSafeManager.GetPlayerStatusAsync();
//         // if (playerStatus != null && playerStatus.Ok)
//         // {
//         //     Debug.Log($"Player has violation: {playerStatus.Data.HasViolation}");
//         // }
//
//         // Example: Sensei poll management
//         // string personaId = "your-persona-id"; // From PlaySafe dashboard
//         // var poll = await playSafeManager.GetActivePollAsync(personaId);
//         //
//         // if (poll != null && poll.Ok && poll.Data != null)
//         // {
//         //     var vote = await playSafeManager.CastVoteAsync(poll.Data.Id, "yes");
//         //     var results = await playSafeManager.GetPollResultsAsync(poll.Data.Id);
//         // }
//
//         // Force moderation on regardless of remote-config sampling; read fresh each tick.
//         // playSafeManager.alwaysModerate = IsCompetitiveMatch();
//     }
//
//     private void OnDestroy()
//     {
//         if (_realtimeAvatarManager != null)
//         {
//             _realtimeAvatarManager.avatarCreated -= OnAvatarCreated;
//             _realtimeAvatarManager.avatarDestroyed -= OnAvatarDestroyed;
//         }
//
//         DetachLocalVoice();
//     }
//
//     private void OnPlaySafeInitialized(PlaySafeManager playSafeManager)
//     {
//         Debug.Log("[invoked] PlaySafeManager initialized", playSafeManager);
//     }
//
//     // Normcore re-creates the local avatar on every (re)connect, so the voice component is picked up here each time.
//     private void OnAvatarCreated(RealtimeAvatarManager avatarManager, RealtimeAvatar avatar, bool isLocalAvatar)
//     {
//         if (isLocalAvatar)
//         {
//             AttachLocalVoice(avatar);
//         }
//     }
//
//     private void OnAvatarDestroyed(RealtimeAvatarManager avatarManager, RealtimeAvatar avatar, bool isLocalAvatar)
//     {
//         if (isLocalAvatar)
//         {
//             DetachLocalVoice();
//         }
//     }
//
//     private void AttachLocalVoice(RealtimeAvatar avatar)
//     {
//         DetachLocalVoice();
//
//         _localVoice = avatar.GetComponentInChildren<RealtimeAvatarVoice>();
//         if (_localVoice == null)
//         {
//             Debug.LogWarning("[PlaySafe] The local avatar has no RealtimeAvatarVoice, so no voice audio will be moderated.", avatar);
//             return;
//         }
//
//         _localVoice.voiceData += OnVoiceData;
//     }
//
//     private void DetachLocalVoice()
//     {
//         if (_localVoice != null)
//         {
//             _localVoice.voiceData -= OnVoiceData;
//         }
//
//         _localVoice = null;
//     }
//
//     // The microphone audio Normcore is transmitting: already processed (gain, noise suppression) and silent while muted.
//     private void OnVoiceData(float[] frame)
//     {
//         // Normcore delivers 10 ms mono frames, so the frame length gives the microphone sample rate.
//         playSafeManager.sampleRate = frame.Length * 100;
//         playSafeManager.AppendToBuffer(frame);
//     }
//
//     // When a user is banned / or timed out
//     private void OnPolicyViolationEvent(ActionItem actionEvent, DateTime serverTime)
//     {
//         string duration = actionEvent.DurationInMinutes >= 60 ?
//             $"{(actionEvent.DurationInMinutes / 60f).ToString("F1")} hours" :
//             $"{actionEvent.DurationInMinutes} minutes";
//
//         DateTime bannedUntil = serverTime + System.TimeSpan.FromMinutes(actionEvent.DurationInMinutes);
//         string msgToUser = $"Voice chat disabled for {duration}. This can happen due to using slurs, fighting, or general disrespectful behavior";
//
//         Debug.Log($"[OnPolicyViolationEvent] {msgToUser} (Action: {actionEvent.Action}, Reason: {actionEvent.Reason}, until: {bannedUntil})");
//         // TODO: Notify the user they were banned
//
//         // Example: muting the local RealtimeAvatarVoice stops sending this player's audio to everyone in the room.
//         // if (_localVoice != null) _localVoice.mute = true;
//         // TODO: Unmute once bannedUntil passes, and re-apply the mute when the avatar is re-created or the game restarts
//         //       (see GetPlayerStatusAsync above).
//     }
//
//     // When to record a users microphone
//     private bool CanRecord()
//     {
//         return !IsVoiceMuted() &&
//                IsConnectedToRoom() &&
//                GetRoomPlayerCount() >= 2; // Only record if there are other players in the room
//         // Alternatively, you can simply return true when testing in the editor
//     }
//
//     private bool IsVoiceMuted()
//     {
//         // No local voice yet (not connected, or the avatar has no RealtimeAvatarVoice) counts as muted.
//         // TODO: If your game mutes players some other way (push-to-talk, a settings toggle), check that here too
//         return _localVoice == null || _localVoice.mute;
//     }
//
//     private bool IsConnectedToRoom()
//     {
//         return realtime.connected;
//     }
//
//     public int GetRoomPlayerCount()
//     {
//         if (IsConnectedToRoom())
//         {
//             return _realtimeAvatarManager.avatars.Count;
//         }
//
//         return 0;
//     }
//
//     // Used to identify a player when an event is sent to PlaySafe - e.g. an audio event
//     private PlaySafeManager.AudioEventRequestData GetTelemetry()
//     {
//         string userId = "1234"; // TODO: Get user account id / platform user id
//         string language = Application.systemLanguage.ToString();
//
//         // The public username of the player. This value is encrypted before storage
//         string userName = "ExampleUsername"; // TODO: Get public player username
//
//         // Null when the player is not in a room. Quickmatch rooms have a regular room name too.
//         string roomId = IsConnectedToRoom() ? realtime.room.name : null;
//
//         PlaySafeManager.AudioEventRequestData telemetry = new PlaySafeManager.AudioEventRequestData()
//         {
//             UserId = userId,
//             UserName = userName, // Optional but encouraged (having this allows you to search for the user by username in the PlaySafe dashboard)
//             RoomId = roomId,
//             Language = language,
//         };
//
//         return telemetry;
//     }
// }
