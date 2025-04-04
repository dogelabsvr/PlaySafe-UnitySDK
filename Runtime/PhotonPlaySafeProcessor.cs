#if PHOTON_VOICE_DEFINED
using System;
using _DL.PlaySafe;
using Photon.Voice;
using Photon.Voice.Unity;
using UnityEngine;
#if UNITY_ANDROID
  using UnityEngine.Android;
#endif

public class PhotonPlaySafeProcessor : IProcessor<float>
{
  public PlaySafeManager playSafeManager;
  
  private readonly object _bufferLock = new object();
  
  public float[] Process (float[] buf)
  {
    if (playSafeManager.audioBufferFromExistingMic == null) return Array.Empty<float>();
#if UNITY_ANDROID
    if (!Permission.HasUserAuthorizedPermission(Permission.Microphone)) return Array.Empty<float>();
#endif
    
    playSafeManager.AppendToBuffer(buf);

    return buf;
  }

  public void Dispose ()
  {
    lock (_bufferLock)
    {
      playSafeManager.audioBufferFromExistingMic = null;
    }
  }
}
#endif