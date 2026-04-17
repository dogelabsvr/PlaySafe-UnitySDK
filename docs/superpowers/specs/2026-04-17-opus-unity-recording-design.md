# Opus Audio Recording - Unity PlaySafe SDK

**Date:** 2026-04-17
**Version:** targets 0.13.0

## Overview

Replace the WAV audio recording and send path in `PlaySafeManager` with an Opus-encoded path. The backend at `/products/moderation/opus` already exists and expects raw Opus packets in a length-prefixed binary format. The WAV path is deprecated (not deleted) during a transitional testing period, then removed.

Opus is encoded using Concentus — a pure C# port of libopus, MIT licensed — vendored directly into `Runtime/Vendor/Concentus/`. No native plugins, no external package references, no public API changes.

---

## Architecture

### Files Added
- `Runtime/PlaySafeOpusEncoder.cs` — `internal static` encoder wrapper around Concentus
- `Runtime/Vendor/Concentus/` — vendored Concentus source (~30 .cs files, MIT)
- `ThirdPartyNotices.md` — Concentus copyright attribution
- `docs/superpowers/specs/2026-04-17-opus-unity-recording-design.md` — this file

### Files Modified
- `Runtime/PlaySafeManager.cs` — new coroutine, 2 new constants, sample rate change, deprecated markers
- `package.json` — version bump `0.12.0` → `0.13.0`

### Files Unchanged
- `Runtime/PlaySafeManagerResponse.cs`
- `Runtime/PhotonPlaySafeProcessor.cs`
- `Samples/`

---

## Data Flow

### Recording (unchanged)
The state machine (`StartRecording`, `PauseRecording`, `ResumeRecording`, `StopRecording`) is identical. Both Unity Mic and Photon Voice paths continue writing float PCM into `audioBufferFromExistingMic`.

**One change:** recording sample rate increases from 16,000 Hz to 48,000 Hz.

```
Buffer size: sampleRate * channelCount * RecordingDurationSeconds
           = 48,000 * 1 * 10 = 480,000 floats  (was 160,000)
```

### Send Path (Opus)

```
StopRecording()
  └── SendOpusForAnalysisCoroutine()
        ├── Guard: _sampleIndex == 0 → LogError, return
        ├── Silence check on float[] buffer → Log, return if silent
        ├── PlaySafeOpusEncoder.Encode(audioBufferFromExistingMic, _sampleIndex)
        │     ├── Concentus encoder: 48kHz, mono, VOIP mode, 24kbps
        │     ├── 960-sample frames (20ms each), zero-pad last frame
        │     ├── Per packet: write [uint16 BE length][encoded bytes]
        │     └── Returns (byte[] lengthPrefixedBlob, int packetCount)
        ├── Build WWWForm:
        │     opusChunks       → blob ("audio.opus", "application/octet-stream")
        │     packetCount      → int
        │     sampleRate       → 48000
        │     channels         → 1
        │     estimatedDuration → max(1, _sampleIndex / OpusSampleRate)
        │     userId, roomId, username → GetTelemetry()
        └── POST /products/moderation/opus
              Authorization: Bearer {appKey}
```

### Deprecated WAV Path
`SendAudioClipForAnalysisCoroutine` and `AudioClipToFile` are marked `[Obsolete]`. The call site in `StopRecording` is commented out with a `TODO: Delete after Opus path verified` note. Switching back for testing is a two-line comment swap (plus reverting the sample rate constant).

---

## Components

### `PlaySafeOpusEncoder.cs`

```csharp
internal static class PlaySafeOpusEncoder
{
    private const int SampleRate = 48000;
    private const int Channels = 1;
    private const int FrameSize = 960;       // 20ms @ 48kHz
    private const int Bitrate = 24000;       // 24kbps VOIP
    private const int MaxPacketSize = 4000;  // per Opus spec

    internal static (byte[] blob, int packetCount) Encode(float[] samples, int sampleCount)
    {
        var encoder = OpusCodecFactory.CreateEncoder(SampleRate, Channels, OpusApplication.OPUS_APPLICATION_VOIP);
        encoder.Bitrate = Bitrate;

        var output = new MemoryStream();
        var packetBuf = new byte[MaxPacketSize];
        var frame = new float[FrameSize];
        int packetCount = 0;
        int totalFrames = (sampleCount + FrameSize - 1) / FrameSize;

        for (int i = 0; i < totalFrames; i++)
        {
            int start = i * FrameSize;
            int available = Math.Min(FrameSize, sampleCount - start);
            Array.Clear(frame, 0, FrameSize);
            Array.Copy(samples, start, frame, 0, available);

            int encodedBytes = encoder.Encode(frame, FrameSize, packetBuf, MaxPacketSize);

            output.WriteByte((byte)(encodedBytes >> 8));
            output.WriteByte((byte)(encodedBytes & 0xFF));
            output.Write(packetBuf, 0, encodedBytes);
            packetCount++;
        }

        return (output.ToArray(), packetCount);
    }
}
```

### `PlaySafeManager.cs` - additions

```csharp
private const string OpusModerationEndpoint = "/products/moderation/opus";
private const int OpusSampleRate = 48000;
```

`StartRecording`: `sampleRate = OpusSampleRate` (was `UnityMicSampleRate`)

`StopRecording` toggle:
```csharp
// [Obsolete] TODO: Delete after Opus path verified
// StartCoroutine(SendAudioClipForAnalysisCoroutine(_audioClipRecording));

StartCoroutine(SendOpusForAnalysisCoroutine());
```

---

## Opus Parameters

| Parameter | Value | Rationale |
|---|---|---|
| Sample rate | 48,000 Hz | Required - backend WebM muxer hardcodes 48kHz in container header |
| Channels | 1 (mono) | Unity Mic default; Photon path also mono |
| Application | OPUS_APPLICATION_VOIP | Optimised for speech; includes formant emphasis and high-pass filter |
| Bitrate | 24,000 bps | VOIP sweet spot per RFC 6716; higher quality than minimum (16kbps) without significant size increase |
| Frame size | 960 samples | 20ms at 48kHz; standard VOIP frame size |
| Max packet size | 4,000 bytes | Per Opus spec recommendation |

---

## Error Handling

| Failure | Handling |
|---|---|
| `_sampleIndex == 0` | `LogError`, yield break |
| Silent clip | `Log` (info level), yield break |
| `OpusException` during encode | `LogError`, yield break |
| `packetCount == 0` after encode | `LogError`, yield break |
| Network error | Existing `SendFormCoroutine` handles (logs `www.error`) |
| Backend 4xx/5xx | Existing `SendFormCoroutine` handles (logs response body) |
| App lost focus | Existing `_hasFocus` guard in `StopRecording` prevents send |

No silent failures. Every exit path logs.

---

## Licensing

Concentus is MIT licensed. The copyright notice is preserved in the vendored source files. A `ThirdPartyNotices.md` is added to the package root listing Concentus, its author, and the MIT license text. The PlaySafe SDK license is unaffected.

---

## Version

`package.json`: `0.12.0` → `0.13.0` (minor bump - new feature, no breaking changes)

---

## Testing & Toggle

To switch back to WAV during testing:
1. In `StartRecording`: change `sampleRate = OpusSampleRate` back to `sampleRate = UnityMicSampleRate`
2. In `StopRecording`: uncomment WAV call, comment out Opus call

Both lines are marked with comments making this obvious at a glance.

**Cleanup note:** `StopRecording` currently calls `_audioClipRecording = CreateAudioClip()` before the send. The Opus path doesn't use the result - it reads directly from `audioBufferFromExistingMic`. This is harmless overhead during the transition period. Once the WAV path is deleted, `CreateAudioClip()`, `_audioClipRecording`, and `WriteWavHeader` can also be removed.
