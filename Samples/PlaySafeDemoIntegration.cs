using System;
using _DL.PlaySafe;
using UnityEngine;

public class DemoPlaySafeIntegration : MonoBehaviour
{
    [SerializeField, Tooltip("The PlaySafeManager component to use for the demo.") ] PlaySafeManager playSafeManager;
    void Start()
    {
        playSafeManager.CanRecord = CanRecord;
        playSafeManager.GetTelemetry = GetTelemetry;
        playSafeManager.OnActionEvent = OnActionEvent;
        playSafeManager.Initialize();
    }
    
    // When a user is banned / or timed out 
    private void OnActionEvent(ActionItem actionEvent)
    {
        string duration = actionEvent.DurationInMinutes >= 60 ? 
            $"{(actionEvent.DurationInMinutes / 60f).ToString("F1")} hours" :
            $"{actionEvent.DurationInMinutes} minutes";

        Debug.Log(
            $"Voice chat disabled for {duration}. This can happen due to using slurs, fighting, or general disrespectful behavior");
        // Example: DateTime bannedUntil = System.DateTime.Now + System.TimeSpan.FromMinutes(actionEvent.DurationInMinutes);
    }
    
    // When to record a users microphone 
    private bool CanRecord()
    {
        // Example settings to choose when to listen to microphone 
        bool IsMicrophoneMuted = false; 
        bool IsInMultiplayerLobby = true; 
        int playerCount = 3;
        return !IsMicrophoneMuted &&
               IsInMultiplayerLobby &&
               playerCount >= 2;
    }

    // When an event is sent to PlaySafe - e.g. an audio event 
    private PlaySafeManager.AudioEventRequestData GetTelemetry()
    {
        string userId = "1234";
        string roomName = "ExampleRoom";
        string language = Application.systemLanguage.ToString();
        PlaySafeManager.AudioEventRequestData telemetry = new PlaySafeManager.AudioEventRequestData()
        {
            UserId = userId,
            RoomId = roomName,
            Language = language,
        };

        return telemetry;
    }
    

}
