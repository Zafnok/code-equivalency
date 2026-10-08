# webapi-inherited-route

Both sides are the same ASP.NET Core source on .NET 10 (ticket P2-119). No controller has a route of
its own: all four take `[Route("[controller]")]` from `BaseApiController`, as ASP.NET Core does, and
the token becomes each controller's own name.

- `BrandingController` and `StartupController` both declare `[HttpGet("Configuration")]`. With the
  inherited route they are `GET /branding/configuration` and `GET /startup/configuration`. Read
  without it they were both `GET /configuration`, and each was reported Unknown
  (`unmatched-overload`) instead of being compared.
- `AudioController` and `VideosController` both declare `[HttpGet("{itemId}/stream")]` and
  `[HttpHead("{itemId}/stream")]` on one method. That is one endpoint per method, `GET`.

## Expected verdicts

| Procedure | Verdict | `proofMethod` |
|---|---|---|
| `BrandingController.GetBrandingOptions()` (`GET /branding/configuration`) | Equivalent | `congruence` |
| `StartupController.GetStartupConfiguration()` (`GET /startup/configuration`) | Equivalent | `congruence` |
| `AudioController.GetAudioStream(int)` (`GET /audio/{itemid}/stream`) | Equivalent | `congruence` |
| `VideosController.GetVideoStream(int)` (`GET /videos/{itemid}/stream`) | Equivalent | `congruence` |

Exit code: 0 (no Divergent or Unknown result).
