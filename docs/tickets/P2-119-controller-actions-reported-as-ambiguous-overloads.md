# P2-119 A controller action with one method is not an ambiguous overload
Status: in-progress
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
- Cause (criterion 1). The step is `EndpointDiscovery`: `TypePrefix` read a controller's route from
  the controller's own attributes only. The shape is an action whose controller has no `[Route]` of
  its own and inherits `[Route("[controller]")]` from a base class. Its template was built with no
  prefix, so `[HttpGet("Configuration")]` on `BrandingController` and on `StartupController` were both
  `GET /configuration`. `CSharpFrontend.EndpointOverrides.Build` then saw one verb and template on two
  actions per side and put all four in `ForcedAmbiguous`, which is where the two results per action
  come from (one per side). `ProcedureEnumerator` and `StableIdentityMatcher` yield no duplicate: each
  method is enumerated once, and the forced-ambiguous actions never reach the matcher.
- The other shape the ticket names, `[HttpGet]` and `[HttpHead]` on one method, is not a cause. It is
  one endpoint (`GET`; `HEAD` is not in the verb map) and pairs on its own
  (`Analyze_OneActionWithGetAndHeadIsOneEndpoint` passed before the fix). `AudioController` and
  `VideosController` collided for the first reason: both inherit their route and both declare
  `{itemId}/stream`.
- Reproduced with `Analyze_ControllerRouteInheritedFromABaseClassKeepsActionsApart`
  (`CSharpFrontendTests`), which failed before the fix with no pairs. `jellyfin-13023` was not run
  again, so the 58 results are not re-counted here; that they all have this cause is inferred from the
  controller list above pairing up by template (Genres with MusicGenres, Persons with Studios, Audio
  with Videos, Branding with Startup), not measured.
- Decision: a controller with no route of its own takes the route of the nearest base class carrying
  ASP.NET Core's `[Route]`; its own route wins when it has one. ASP.NET Core strictly gives a
  controller both routes when both are declared, a case the existing first-attribute rule already does
  not model, so it is left alone.
- Decision: Web API 2 and MVC 5 route attributes are not inherited. Both frameworks read a
  controller's route with attribute inheritance off, so a legacy base class's route does not apply.
- Decision: the sample is `samples/webapi-inherited-route`, an SDK-style .NET 10 project on both sides
  with identical source, since the finding is from a version bump and needs no .NET Framework side.
- Criterion 3 is `Analyze_ActionsAnInheritedRouteCannotTellApartStayAmbiguous`: an inherited
  `[Route("api")]` with no `[controller]` token leaves two controllers' actions on one verb and
  template, and all four stay Ambiguous.
