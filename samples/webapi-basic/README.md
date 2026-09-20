# webapi-basic

`OrdersController.Get` has a different C# identity on each side (Web API 2's
`[RoutePrefix]`/`[Route]`/`[HttpGet]` vs ASP.NET Core's `[Route]`/`[HttpGet(template)]`,
different base classes, different namespced attribute types), but both normalise to the
same endpoint identity `GET /api/orders/{id}` (ticket M2-005). Endpoint discovery matches
the pair on that identity ahead of ordinary C# signature matching.

## Expected verdicts

| Procedure | Verdict |
|---|---|
| `OrdersController.Get(int)` (`GET /api/orders/{id}`) | Equivalent |
