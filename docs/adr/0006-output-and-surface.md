# ADR 0006: SARIF only, headless only, no UI in the MVP

Status: accepted (2026-09-17)

## Decision
The only artefact is SARIF 2.1.0 plus an exit code. No custom JSON, no HTML, no UI.

## Why
SARIF is already consumed by GitHub Code Scanning (upload-sarif), VS Code (SARIF
Viewer), Visual Studio, Azure DevOps and SonarQube (`sonar.sarifReportPaths`, which
requires `version`, `tool.driver.name`, `ruleId`, `message.text` and `locations` per
result; all present). Baselining (`baselineState`) is in the spec. Building a UI before
a single real solution has been verified is the surest way to miss the one-week MVP.
Gemini's "SARIF first, headless first" points were correct; its Blazor / React Native /
React Flow suggestions were premature and are parked.

## Rejected for now
- Blazor / React Native desktop app; React Flow CFG viewer. Post-MVP, after real usage.
- Custom diff schema. Extra data goes in SARIF `properties` bags.
- Hosted tier / AKS / API keys. The container is the free tier; hosting wraps it later.

## Clarifications
- 2026-09-23 (ADRs 0032, 0033). The parked hosted tier targets Azure Container Apps Jobs, not AKS
  (ADR 0032). `equiv mcp` is a headless transport whose tool results are the same SARIF log, so it
  is within this ADR (ADR 0033).
