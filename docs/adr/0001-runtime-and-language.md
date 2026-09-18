# ADR 0001: .NET 10 / C# 14 for the whole MVP

Status: accepted (2026-09-17)

## Decision
All MVP components are C# on .NET 10 (LTS, supported to Nov 2028). No .NET Framework
code in this repo; the engine only reads .NET Framework solutions.

## Why
Roslyn is the only production-grade semantic model for C#, and it is a .NET library.
Z3 and the SARIF SDK have first-party .NET bindings. One language keeps the agent
workflow and the quality gates uniform. .NET 11 ships Nov 2026; retarget only if a
ticket needs a feature from it.

## Rejected
- Rust/Go core with a C# sidecar: no benefit until a non-.NET frontend exists. When
  Java arrives, the Java frontend becomes a sidecar that emits IR over stdio; Core stays.
