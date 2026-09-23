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
- **Unchanged share below 40%: stop.** Git Extensions 74.0%, agent median 100%. Not triggered.
- **Lowerable share, top three to 15%.** Agent median is already 22.1%, above 15% before any M4
  ticket, so solver precision continues after M3. Git Extensions cannot be evaluated: the run
  aborts in lowering (P2-010). This rule is re-applied to it when P2-010 lands.
- **Lowerable share, 5% bar per M4 ticket.** Applied to the agent median in the table below.
  Git Extensions has no census, so the agent median alone decides.
- **Project load rate below 100% on a human pair is a ticket.** Git Extensions has no load report
  because the run crashed first. The crash is P2-010, and its containment P2-011. SignalR's 40% is
  an agent pair. Its cause is filed anyway as P2-013.
- **Line-scoped Unknown share, seeded recall.** Not measured in `census` mode (M4-007).

## M4 tickets by pairs unlocked

"Unlocked" is the share of matched pairs whose lowered body (legacy side) holds at least one opaque
with a reason the ticket removes. It is an upper bound: the SARIF counts bodies per reason, not the
set of reasons per body, and a whole-body opaque hides the reasons inside it. `Conversion`
(153 bodies over the three pairs) is not attributed. It is split between M3-010 (implicit reference
and boxing), M4-005 (downcasts) and M4-002 (floating-point and decimal), and the census cannot tell
the kinds apart.

| Ticket | Reasons counted | ServiceAnt | ShortestPaths | SignalR | Median | Effort | Per point | Outcome |
|---|---|---|---|---|---|---|---|---|
| M4-001 | ConstructorBodyOperation, foreach-enumerator, using | 19.1% | 14.1% | 44.0% | 19.1% | L (4) | 4.8 | keep, 1st |
| M4-008 | no-body, Block | 16.9% | 9.3% | 0% | 9.3% | M (2) | 4.7 | keep, 2nd |
| M4-004 | DelegateCreation | 19.1% | 7.4% | 4.0% | 7.4% | L (4) | 1.9 | keep, 3rd |
| M4-002 | Binary, CompoundAssignment, Decrement | 2.9% | 9.0% | 0% | 2.9% | L (4) | 0.7 | backlog |
| M4-003 | ref-argument, lock | 2.2% | 0% | 4.0% | 2.2% | M (2) | 1.1 | backlog |
| M4-005 | IsType, switch-pattern | 0% | 0.3% | 4.0% | 0.3% | M (2) | 0.2 | backlog |
| M4-006 | Await | 5.9% | 0% | 0% | 0% | M (2) | 0 | backlog |

No kept ticket depends on a demoted one, so the soundness order does not change the list.

## Caveats (reported, not reinterpreted)
- The three agent pairs are small libraries (25, 136 and 312 matched pairs). The human pair,
  about 250,000 legacy lines, has no census yet.
- Two demoted tickets, M4-002 and M4-005, own part of the unattributed `Conversion` count. A census
  after M3-010 shows what is left of it. Applying the same 5% rule to that data may return either
  ticket to M4. That is the fixed rule applied to new data, not a changed threshold.
- Every run needed environment work on this box (P2-014), and three NuGet warnings had to be
  silenced for projects to load at all (P2-012).

## Outcome

**continue**
