# Census verdict, 2026-09-23 (M3-022)

ADR 0028's rules applied to the `census` runs of 2026-09-23. equiv at 87b730e; Poly-MigrationBench
list @ b0a91412d64a. Per-pair detail is in the four `2026-09-23-census-*/SUMMARY.md` files.

## Pairs and selection
- Human pair: `gitextensions-8522`.
- Agent pairs, in `corpus.ps1 -Select -Count 3` order:
  1. `ShiningRush/ServiceAnt`: used.
  2. `chrismckelt/WebMinder`: **skipped**. The agent's migration does not build: `Core` depends on
     `System.Web` (`IHttpModule`, `HttpApplication`, `HttpContext.Current`), which has no .NET 10
     equivalent, and the test site is an ASP.NET MVC 5 application. Replaced by the next row.
  3. `TomasJohansson/adapters-shortest-paths-dotnet`: used.
  4. `lethek/SignalR.Extras.Autofac`: used, in place of WebMinder.

## Metrics

| Pair | Unchanged share | Lowerable share | Project load rate | Line-scoped Unknown share | Seeded recall |
|---|---|---|---|---|---|
| gitextensions-8522 (human) | 74.0% | n/a (run crashed, P2-010) | n/a (run crashed) | n/a (census) | n/a (census) |
| pmb-shiningrush__serviceant | 100% | 22.1% (30/136) | 100% | n/a | n/a |
| pmb-tomasjohansson__adapters-shortest-paths-dotnet | 100% | 25.6% (80/312) | 100% | n/a | n/a |
| pmb-lethek__signalr.extras.autofac | 100% | 8.0% (2/25) | 40% | n/a | n/a |
| **Agent median** | **100%** | **22.1%** | **100%** | n/a | n/a |

Unchanged share is the "unchanged files" proxy (`corpus.ps1 -Unchanged`), because M3-015 has not
landed. The three agents changed project files only, so every `.cs` file is byte-identical.

## Rules
- **Unchanged share below 40%: stop.** Git Extensions 74.0%, agent median 100%. Not triggered, but
  it could not have fired on the agent pairs: the migration prompt forbids modernising code that
  already compiles, so the three agents changed project files only.
- **Lowerable share, top three to 15%.** Not evaluated. Git Extensions, the only pair with changed
  code, aborts in lowering (P2-010). The agent pairs' 22.1% counts bodies that are byte-identical on
  both sides. Once M3-015 lands they are all Equivalent by congruence, so the solver never sees
  them, and their lowerable share says nothing about solver precision.
- **Lowerable share, 5% bar per M4 ticket.** Not applied, for the same reason. The per-ticket
  numbers below are recorded as data only.
- **Project load rate below 100% on a human pair is a ticket.** Git Extensions has no load report
  because the run crashed first. The crash is P2-010, and its containment P2-011. SignalR's 40% is
  an agent pair. Its cause is filed anyway as P2-013.
- **Line-scoped Unknown share, seeded recall.** Not measured in `census` mode (M4-007).

## M4 tickets by bodies holding their reasons (data only, not applied)

Share of matched pairs whose lowered body (legacy side) holds at least one opaque with a reason the
ticket removes. It is an upper bound: the SARIF counts bodies per reason, not the set of reasons per
body, and a whole-body opaque hides the reasons inside it. `Conversion` (153 bodies over the three
pairs) is not attributed. It is split between M3-010, M4-005 and M4-002, and the census cannot tell
the kinds apart. Every body counted here is unchanged by its migration.

| Ticket | Reasons counted | ServiceAnt | ShortestPaths | SignalR | Median | Effort |
|---|---|---|---|---|---|---|
| M4-001 | ConstructorBodyOperation, foreach-enumerator, using | 19.1% | 14.1% | 44.0% | 19.1% | L (4) |
| M4-008 | no-body, Block | 16.9% | 9.3% | 0% | 9.3% | M (2) |
| M4-004 | DelegateCreation | 19.1% | 7.4% | 4.0% | 7.4% | L (4) |
| M4-002 | Binary, CompoundAssignment, Decrement | 2.9% | 9.0% | 0% | 2.9% | L (4) |
| M4-003 | ref-argument, lock | 2.2% | 0% | 4.0% | 2.2% | M (2) |
| M4-005 | IsType, switch-pattern | 0% | 0.3% | 4.0% | 0.3% | M (2) |
| M4-006 | Await | 5.9% | 0% | 0% | 0% | M (2) |

## Caveats
- The three agent pairs are small libraries (25, 136 and 312 matched pairs), and all three are pure
  retargets. The human pair, about 250,000 legacy lines, has no census yet.
- The selection skipped WebMinder, a System.Web application, because its migration does not build.
  System.Web applications are a large, hard share of real .NET Framework migrations, and the
  replacement was another library.
- In adapters-shortest-paths-dotnet the agent raised NHibernate from 5.2.7 to 5.5.2. Member names
  did not change, so congruence would call every method that calls NHibernate Equivalent while the
  library underneath changed. On a pure retarget, the trust question is about runtime and package
  behaviour. The model covers that only through the runtime-changes catalogue.
- Every run needed environment work on this box (P2-014), and three NuGet warnings had to be
  silenced for projects to load at all (P2-012).

## Outcome

**incomplete: human pair pending.** Neither continue nor stop can be decided from this data. The
M4 order in `docs/ROADMAP.md` is unchanged. Next: P2-011, then P2-010 and P2-012, then rerun the
census on Git Extensions. That run is the feasibility test.
