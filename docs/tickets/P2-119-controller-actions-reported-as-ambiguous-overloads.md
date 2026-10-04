# P2-119 A controller action with one method is not an ambiguous overload
Status: todo
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-066

## Goal
On P2-066's `jellyfin-13023`, 58 results in `Jellyfin.Api.Controllers` are Unknown
(`unmatched-overload`), two for each of 29 action methods across 19 controllers. Each says the
identity "matches more than one overload with this identity". The source has one method with that
name and those parameters on each side, and the two sides are byte-identical, so 29 actions are never
compared. Examples:
- `Jellyfin.Api.Controllers.AudioController::GetAudioStream(...)` has `[HttpGet]` and `[HttpHead]`
  on one method;
- `Jellyfin.Api.Controllers.BrandingController::GetBrandingOptions()` has a single `[HttpGet("Configuration")]`,
  and its controller's route comes from a base class (`BaseJellyfinApiController`).

Other actions of the same controllers are matched by route (the log shows items such as
`GET /videos/{itemid}/hls/{playlistid}/stream.m3u8`). So some actions get an endpoint identity, and
these 29 get the method identity more than once. The cause is not established: find which step
yields the duplicate (`EndpointDiscovery`, `ProcedureEnumerator`, or the matcher), with a minimal
controller that reproduces it, before changing anything.

## Spec references
ARCHITECTURE.md frontend step 3 (endpoints), M2-005 (attribute routing),
`src/Equiv.Frontend.CSharp/Endpoints/EndpointDiscovery.cs`,
`src/Equiv.Core/Matching/StableIdentityMatcher.cs`, `docs/runs/2026-10-03-upgrade-verdict.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. `## Notes` names the step that yields the duplicate identity and the shape of action that
   triggers it.
2. A sample pair with that shape, identical on both sides, has no `unmatched-overload` result and
   its action is congruent.
3. Two real overloads that differ only in a way the identity cannot express stay `unmatched-overload`.

## Tests
Integration test on the sample; a unit test for criterion 3.

## Out of scope
Convention-based routing (M2-005 criterion 2).

## Notes
- Found by P2-066: 58 results on `jellyfin-13023`. The controllers: Devices (6), Audio, Channels,
  Genres, MusicGenres, Persons, Studios, System, Videos (4 each), Branding, LiveTv, Localization,
  Plugins, ScheduledTasks, Startup, SyncPlay, Trailers, UserLibrary, Years (2 each).
