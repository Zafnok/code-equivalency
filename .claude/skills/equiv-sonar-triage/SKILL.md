---
name: equiv-sonar-triage
description: Refresh the SonarQube debt queue. Use when asked to triage Sonar findings, file or update sonar GitHub issues, run sonar-triage, or decide whether a Sonar finding is a false positive for this repo.
---

# Sonar triage

Turns SonarCloud's overall backlog into batched GitHub issues. Design: `docs/adr/0016-sonar-issue-triage.md`.
Tool reference: `tools/sonar-triage/README.md`. This skill is the judgement part; the script
is the mechanical part.

## Loop

1. Dry run first, always:
   `./tools/sonar-triage/sonar-triage.ps1`
   It reads SonarCloud anonymously and writes nothing.
2. Read the summary line — `fetched N / suppressed N / batches N` — and the create/edit/close
   list under it. Three things deserve a pause:
   - **A batch you do not recognise.** New rule, or a rule that has spread. Decide its verdict
     (below) before filing it.
   - **An `edit` on a batch whose findings you expect to be unchanged.** Usually means a file
     moved or Sonar re-analysed; harmless, but check it is not a fix PR half-landed.
   - **A `close` you did not earn.** A batch closes when Sonar reports nothing in it. If no
     one fixed it, Sonar's analysis may have failed or `sonar.exclusions` may have changed.
3. Settle any new verdict in `tools/sonar-triage/policy.jsonc`, then re-run the dry run.
4. Sync: `./tools/sonar-triage/sonar-triage.ps1 -Apply`
5. Re-run `-Apply` once more. It must report `created 0 / edited 0 / closed 0`. If it does
   not, the body renderer is non-deterministic and that is a bug to fix, not to re-run past.

## Deciding a verdict

Default is `fix`. A finding is `accept` only if one of these is true:

- Fixing it would **weaken a gate** — most often the 100% branch-coverage gate, which is why
  `S2178` is accepted for `Equiv.Core`'s record equality.
- Fixing it would **contradict an accepted ADR**.
- Fixing it would **break a documented repo pattern** whose reason is written down — e.g. the
  empty `AssemblyMarker.cs` types that exist for ArchUnitNET assembly discovery.

`defer` is for a finding that is real and should be fixed but is blocked on a ticket that has
not landed. Name that ticket in the reason.

Not reasons to `accept`:

- "The suggestion is ugly." Aesthetics lose to a green dashboard.
- "It is only in tests." Test code is in scope; the batching already keeps it separate.
- "It would be a big diff." That is what rule-wide batching is for.
- "I disagree with the rule in general." Then the rule belongs out of the Sonar quality
  profile, which is an ADR, not a policy entry.

Every `accept` needs a `reason`, and a reason appealing to a repo-wide design choice must cite
an ADR or a ticket. The script refuses to run on a reasonless suppression.

## Keeping SonarCloud honest

`-PushResolutions` marks accepted findings Won't Fix in SonarCloud, with the policy reason as
the comment, so the dashboard matches the repo. It writes to shared external state, needs
`SONAR_TOKEN` with issue-admin rights, and prompts before doing anything. Run it after the
policy file settles, not during experimentation.

## Do not

- File findings by hand. If the script would not file it, neither should you.
- Edit an issue body by hand: the next `-Apply` overwrites it. Change the policy or the
  renderer instead.
- Add `// NOSONAR` to make a batch disappear. That hides the reasoning at the call site;
  `policy.jsonc` is where it goes now.
- Promote the `sonar` check to required as part of a triage run. That is its own change,
  gated on the backlog actually being drained (ADR 0009, `docs/ROADMAP.md`).
