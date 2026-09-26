# webapi-basic

`OrdersController.Get` has a different C# identity on each side (Web API 2's
`[RoutePrefix]`/`[Route]`/`[HttpGet]` vs ASP.NET Core's `[Route]`/`[HttpGet(template)]`,
different base classes, different namespced attribute types), but both normalise to the
same endpoint identity `GET /api/orders/{id}` (ticket M2-005). Endpoint discovery matches
the pair on that identity ahead of ordinary C# signature matching.

`OrdersController.Find` returns `NotFound()` or `Ok(id)`: Web API 2's `ApiController` helpers and
`IHttpActionResult` on the legacy side, ASP.NET Core's `ControllerBase` helpers and `IActionResult`
on the modern side. Every call and result type differs, so the pair is Equivalent only through the
shipped API-equivalence catalogue (ADR 0020, ticket M3-009), which rewrites the legacy side and lists
the entries it applied in `properties.equivalencesApplied`. It equates action results (status code
observed, body opaque), not serialized responses.

## Expected verdicts

| Procedure | Verdict | `equivalencesApplied` |
|---|---|---|
| `OrdersController.Get(int)` (`GET /api/orders/{id}`) | Equivalent | none |
| `OrdersController.Find(int)` (`GET /api/orders/find/{id}`) | Equivalent | `webapi.not-found`, `webapi.ok-of-int`, `webapi.type.action-result`, `webapi.type.not-found-result`, `webapi.type.ok-content-result` |

Exit code: 0 (no Divergent or Unknown result).
