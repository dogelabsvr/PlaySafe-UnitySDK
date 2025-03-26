#if PHOTON_VOICE_DEFINED
using System;
using _DL.PlaySafe;
using Photon.Voice;
using Photon.Voice.Unity;
using UnityEngine;

public class PhotonPlaySafeProcessor : IProcessor<float>
{
  public PlaySafeManager playSafeManager;
  
  public float[] Process (float[] buf)
  {
    if (playSafeManager.audioBufferFromExistingMic == null) return Array.Empty<float>();
    
    playSafeManager.AppendToBuffer(buf);

    return buf;
  }

  public void Dispose ()
  {
    playSafeManager.audioBufferFromExistingMic = null;
  }
}
#endif