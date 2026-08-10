# ADR 017: Portal-mediated visual verification

- Status: Accepted
- Date: 2026-08-10
- Extends: [ADR 005](005-isolated-goal-execution.md), [ADR 010](010-docked-desktop-workbench.md), [ADR 013](013-chat-first-desktop-workflow.md), [ADR 016](016-model-accessible-ide-capabilities.md)

## Context

Harness.NET can validate code and accessibility deterministically, but neither a
developer nor a model can attach a captured application frame to the action being
reviewed. Direct compositor APIs, generic desktop automation, background recording,
or shell screenshot commands would bypass desktop consent and create broad authority.

Linux and Wayland provide the XDG Desktop Portal Screenshot interface. Its response
contains an image URI but does not identify the selected monitor, window, or scale.
Harness.NET must not infer metadata that the portal did not provide.

## Decision

Business Logic owns platform-neutral capture requests, policy, retention, artifact
metadata, model disclosure, and user-visible results. Data Access owns the Linux D-Bus
adapter and private artifact files. Presentation owns the initiating window context,
user actions, image rendering, and accessibility. Host selects the Linux adapter.

Each capture is a single interactive Screenshot portal request. The portal supplies
the consent and source-selection UI for every request. Harness.NET does not reuse a
grant, capture in the background, record video, synthesize input, or expose a generic
desktop API. Cancellation closes the outstanding portal request.

The request records a goal, workspace, initiator, related action, requested source
kind, application identity, optional parent-window identifier, and optional UI scale.
The result records success, user cancellation, denial, portal absence, invalid image,
size rejection, stale request, storage failure, or policy rejection. Portal-selected
window/display identity and native scale are recorded as `Unavailable` unless a
future portal revision returns them. Pixel dimensions and the Presentation-supplied
UI scale remain separate values.

Successful PNG or JPEG captures are copied into private XDG state. Each immutable
manifest contains capture ID, goal, workspace, time, initiator, related action,
application/source identity, dimensions, scale knowledge, media type, byte count,
SHA-256, and artifact filename. Writes use a private temporary file, exact size and
format checks, hash verification, and atomic publication. User repository paths are
never used.

Settings owns these persisted defaults:

- capture enabled: `true`;
- maximum encoded image size: 5 MiB, configurable from 1–16 MiB;
- retention: 7 days, configurable from 1–90 days;
- maximum retained captures per goal: 20, configurable from 1–100;
- remote-model image access: `false` and explicitly opt-in.

Cleanup runs at startup and before capture. It removes stale temporary files, expired
captures, excess oldest captures, malformed manifests, and orphan artifacts. A user
may also delete a capture explicitly. Deletion revokes future model inspection.

Eligible roles receive only `request_visual_capture` and `inspect_visual_capture`.
Both are goal-scoped. A remote route may inspect image bytes only while the current
remote-access preference allows it. The inspection result carries the exact bounded
image bytes and stored metadata; it never carries a filesystem path. The user sees
the same stored bytes and metadata. Provider adapters map those bytes to native
multimodal image content instead of placing encoded image data in ordinary text.
Remote provider selection, spending authority,
and screenshot disclosure remain separate checks.

Visual evidence supplements Build, Test, Roslyn, AT-SPI, and human review. It does not
replace them and does not prove behavior outside the captured frame.

## Consequences

- Wayland capture follows the desktop's consent boundary.
- Linux-specific D-Bus types remain in Data Access.
- Models cannot capture or inspect arbitrary files through this feature.
- Window, display, and native-scale identity can remain explicitly unknown.
- Captures are sensitive private state and are excluded from application backups.
- Other platforms can replace the adapter without changing workflow contracts.

## Alternatives considered

- ScreenCast and PipeWire add persistent streams and are unnecessary for one frame.
- Direct compositor APIs and screenshot utilities bypass the portal consent model.
- Storing captures in the goal worktree leaks private evidence into user repositories.
- Treating a portal URI as durable evidence leaves integrity and retention outside
  Harness.NET.
- Enabling remote image access by default is inconsistent with explicit disclosure.
