# ADR 0033: `equiv mcp` exposes the pipeline to coding agents as an MCP server

Status: accepted (2026-09-23)

## Context
Migrations from .NET Framework are increasingly done by coding agents (the public corpus in ADR
0028 includes agent-authored migration PRs). An agent that can ask "is my port equivalent?" while
it works is the most direct use of this tool. The Model Context Protocol is the standard way
agents call tools; it is multi-vendor, and its official C# SDK (`ModelContextProtocol`, Apache-2.0)
is maintained under the protocol's own organisation by several .NET team engineers. ADR 0006
allows only SARIF plus an exit code, headless.

## Decision
`Equiv.Cli` gains an `mcp` subcommand that runs an MCP server over stdio in the same binary and
container. Its tools call the same pipeline `equiv compare` does and return the SARIF log as the
tool result, with a short verdict summary; there is no second result schema. Tools are registered
explicitly (no assembly scanning, per the no-reflection rule). A remote (Streamable HTTP)
transport is a later decision, tied to the hosted tier (ADR 0032) and its authentication.

## Why
- One binary, one image, one pipeline: `equiv mcp` is a new front door, not a new component.
- SARIF stays the only result shape, so ADR 0006 holds; MCP is a headless transport.
- stdio is what local agents (IDE and terminal agents) launch; it needs no auth or network.

## Rejected
- A separate `Equiv.Mcp` project: a second executable to package for no boundary gain.
- A bespoke REST/gRPC API for agents: every agent host already speaks MCP.
- Hand-rolled JSON-RPC: the official SDK tracks protocol revisions for us.

## Consequences
- New dependency row in ADR 0002 (`ModelContextProtocol`), added by the ticket that uses it.
- stdout becomes the protocol channel in `mcp` mode, so anything the pipeline prints to stdout
  today (for example the analysed line counts) must go through a writer the command controls.
- ARCHITECTURE.md's `Equiv.Cli` section lists `equiv mcp`. ROADMAP gains milestone M5, ticket M5-001.
