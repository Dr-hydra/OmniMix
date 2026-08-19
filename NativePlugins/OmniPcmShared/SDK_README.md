# OmniPcmShared Native SDK 2.0.0

This package is the stable Windows x64 PCM ABI for OmniMix game integrations.
The DLL uses the static MSVC runtime (`/MT`) and exports a C ABI with `__cdecl`.
Loading it does not start UI, access the network, or modify the registry.

## Package contents

- `OmniPcmShared.dll`: Windows x64 native library.
- `include/OmniPcmShared.h`: canonical include entry point.
- `include/omni_pcm_shared.h`: complete C ABI declarations.
- `VERSION`: independent ABI SDK version.
- `SHA256SUMS`: SHA-256 for every shipped input file.
- `test_stream_48k_stereo.wav`: two-second PCM test stream, 48 kHz, stereo, 16-bit.

## Compatibility check

Call `OmniPcm_GetAbiVersion()` before opening a session. The high 16 bits are
the ABI major version and must equal `2`. `OmniPcm_GetAbiInfo()` reports the
supported shared-memory protocol and sample format mask. ABI mismatch is a
recoverable condition: disable custom audio, restore original game music, and
never fail game startup.

## Opening an instance

Use `OmniPcm_OpenInstanceUtf8(instance_id)`. It opens this Windows mapping:

```text
Global\OmniMixPlayer_PCM_<instance_id>
```

When an unelevated desktop backend cannot create a `Global\` mapping on
Windows, it creates the same suffix under `Local\`. The SDK probes this
session-local compatibility name transparently after the canonical name.

The instance ID and all narrow strings are UTF-8. Returned string pointers are
owned by the DLL and remain valid until the next call on that handle or close.
Close every handle with `OmniPcm_Close()`.

## Audio and lifecycle

PCM returned by `OmniPcm_ReadFrames()` is interleaved float32 in `[-1, 1]`.
The count and return value are frames, not bytes. Query
`OmniPcm_GetStreamDescriptionV2()` for sample rate, channels, sample format,
`stream_id`, and format generation.

On `stream_id` change, discard all locally buffered data from the old stream.
Natural EOF follows `Draining -> Ended`; keep reporting `audible_cursor` until
the final buffered audio has reached the output device. Pause, buffering,
playing, draining, ended, and error states are distinct.

Use `OmniPcm_GetHeartbeatAgeMs()` or `OmniPcm_IsHeartbeatAlive()` to detect a
dead backend. A timeout, unsupported format, cursor regression, or stream error
must stop custom playback and restore original game music.

## Threading

Different handles are independent. Serialize concurrent access to the same
handle. `OmniPcm_ReadFrames()` is suitable for an audio callback when the
caller avoids allocation and blocking around it.
