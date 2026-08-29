# Portal visual verification acceptance — 2026-08-10

## Delivered behavior

- The Linux adapter uses `org.freedesktop.portal.Screenshot` for one interactive
  frame. It subscribes to the request response before invoking capture and closes an
  outstanding request on cancellation.
- The non-interactive availability probe is bounded to five seconds and returns the
  typed `portal_timeout` outcome when a desktop portal accepts the D-Bus connection
  but does not answer. Portal discovery therefore cannot indefinitely block desktop
  startup or layout restoration.
- Business Logic returns distinct success, cancellation, denial, portal absence,
  stale request, invalid image, size rejection, storage failure, and policy outcomes.
- PNG and JPEG dimensions are read without image decoding. Encoded frames are capped
  at 1–16 MiB and 64 million pixels.
- Captures and manifests use private XDG state permissions, a SHA-256 integrity
  check, atomic publication, goal scoping, age/count retention, startup cleanup, and
  explicit deletion. Backups and user repositories exclude them.
- Settings exposes availability, privacy, retention, size, count, remote access,
  manual capture, exact-byte preview, refresh, and deletion.
- Lead, Implementer, and Reviewer receive typed request and inspection tools. Remote
  inspection fails unless the user enables screenshot disclosure. Authorized local
  Ollama calls receive the exact bytes through the native image field; authorized
  OpenRouter calls receive the same bytes as multimodal image content.
- The Screenshot v2 portal used by the acceptance workstation was reachable in a
  Wayland session. The live KDE topology had one 2880×1800 panel at 1.6× scale;
  multi-display hardware was not attached. Version 2 does not report target,
  monitor/window identity, or
  native scale; Harness.NET records those fields as unavailable. Presentation records
  the application render scale when it is known.

## Deterministic checks

`VisualCaptureServiceTests` covers portal denial, cancellation, absence, stale
requests, exact bytes, scale metadata, remote denial, and multi-target capability
mapping. `FileVisualCaptureArtifactStoreTests` covers goal isolation, integrity,
revocation, age/count retention, and interrupted-write cleanup. Migration tests cover
restart persistence and upgrade. Scale metadata is tested at 1× and 2×. The
production AT-SPI verifier opens the searchable Settings page and validates the
accessible names for consent, limits, remote disclosure, capture, list, and deletion.
It also passed a production restart with a non-responsive screenshot portal, proving
the bounded availability fallback and saved-layout restoration.

Run:

```text
dotnet test Harness.slnx --no-build --no-restore -m:1 -p:UseSharedCompilation=false
./eng/verify-linux-x64-publish.sh
```

The full solution passed 545 deterministic tests. The production AT-SPI verifier
passed, and the Linux x64 script passed self-contained publish, isolated startup and
shutdown, backup exclusion, restore, and migration from schema 17 to schema 25.

## Live checks still requiring a person

On each supported compositor, use Settings → Visual verification to test consent,
denial, and cancellation at 100% and 200% application scaling and with multiple
displays attached. Confirm that the portal picker owns selection, the stored preview
matches the selected frame, and AT-SPI names the page controls. These are interactive
desktop checks; the deterministic suite does not claim to simulate them.
