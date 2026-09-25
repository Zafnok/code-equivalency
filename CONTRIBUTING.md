# Contributing

## This repository is source-available, not open source

`equiv` is licensed under the [Business Source License 1.1](LICENSE). The source is public so it
can be read, audited and evaluated. That is not a grant to use it in production beyond the limits
in the licence, and it is not an invitation to fork it into a competing product. See
[ADR 0017](docs/adr/0017-licensing-and-ip.md) for the reasoning.

## Pull requests

**Unsolicited pull requests are not accepted.** Please open an issue first.

If a change is agreed, a contribution requires a signed Contributor License Agreement assigning
copyright in the contribution to the Licensor (Nicholas Wentz and Ibasho Corp.). This is not
bureaucracy for its own sake: without an inbound grant, a single accepted outside patch leaves the
codebase un-relicensable, which would block both the scheduled Apache-2.0 conversion on the Change
Date and any future commercial edition. There is no CLA bot configured yet, so in practice this
means a PR cannot be merged until that is in place.

Bug reports, reproductions and counterexamples are welcome as issues and need no agreement.

## If you do work on the code

The rules are in [CLAUDE.md](CLAUDE.md) and are enforced by CI, not by review taste:

- Work is defined in `docs/tickets/`. One ticket, one PR.
  `.claude/skills/equiv-task-loop/SKILL.md` is the definition of done.
- 100% line and branch coverage on every `src/` project, warnings as errors,
  `dotnet format` clean, architecture tests green. See [docs/QUALITY-GATES.md](docs/QUALITY-GATES.md).
- Run `./build.ps1` before pushing. It fetches `Microsoft.Z3` into `.z3-feed/` first; before
  any direct `dotnet restore`/`build`/`test`, run `./tools/z3-feed/fetch.ps1` once (ADR 0030).
- Conventional Commits, with the ticket id in the footer (`Ticket: M1-003`).

## Dependencies

No new NuGet package without a row in [docs/adr/0002-dependencies.md](docs/adr/0002-dependencies.md),
and that row must state the package's licence. Only `MIT`, `Apache-2.0`, `BSD-2-Clause`,
`BSD-3-Clause`, `MS-PL` and `ISC` are allowed for anything linked into the product. `GPL-*`,
`AGPL-*`, `LGPL-*`, `SSPL`, `CeCILL*`, `EUPL` and any non-commercial or sponsorship-conditioned
licence are denied. The full policy, including the two standing exceptions, is in
[ADR 0017](docs/adr/0017-licensing-and-ip.md).
