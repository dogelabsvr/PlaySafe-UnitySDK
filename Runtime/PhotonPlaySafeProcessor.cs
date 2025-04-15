#if PHOTON_VOICE_DEFINED
using System;
using _DL.PlaySafe;
using Photon.Voice;

public class PhotonPlaySafeProcessor : IProcessor<float>
{
  public PlaySafeManager playSafeManager;
  
  public float[] Process (float[] buf)
  {
    if (!playSafeManager) return Array.Empty<float>();
    
    playSafeManager.AppendToBuffer(buf);

    return buf;
  }

  public void Dispose ()
  {
    if (!playSafeManager) return;
    playSafeManager.audioBufferFromExistingMic = null;
  }
}
#endif