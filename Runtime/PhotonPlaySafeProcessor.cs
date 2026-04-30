#if PHOTON_VOICE_DEFINED
using System;
using System.Threading;
using _DL.PlaySafe;
using Photon.Voice;

public class PhotonPlaySafeProcessor : IProcessor<float>
{
  public PlaySafeManager playSafeManager;

  private long _processCallCount;
  private long _totalSamplesSeen;
  private long _nonSilentSampleCount;
  private const float DiagnosticSilenceThreshold = 0.02f;

  public struct Stats
  {
    public long ProcessCallCount;
    public long TotalSamplesSeen;
    public long NonSilentSampleCount;
  }

  public void ResetStats()
  {
    Interlocked.Exchange(ref _processCallCount, 0);
    Interlocked.Exchange(ref _totalSamplesSeen, 0);
    Interlocked.Exchange(ref _nonSilentSampleCount, 0);
  }

  public Stats GetStatsSnapshot()
  {
    return new Stats
    {
      ProcessCallCount = Interlocked.Read(ref _processCallCount),
      TotalSamplesSeen = Interlocked.Read(ref _totalSamplesSeen),
      NonSilentSampleCount = Interlocked.Read(ref _nonSilentSampleCount),
    };
  }

  public float[] Process (float[] buf)
  {
    Interlocked.Increment(ref _processCallCount);
    if (buf != null && buf.Length > 0)
    {
      Interlocked.Add(ref _totalSamplesSeen, buf.Length);
      long nonSilent = 0;
      for (int i = 0; i < buf.Length; i++)
      {
        if (Math.Abs(buf[i]) > DiagnosticSilenceThreshold) nonSilent++;
      }
      if (nonSilent > 0) Interlocked.Add(ref _nonSilentSampleCount, nonSilent);
    }

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
