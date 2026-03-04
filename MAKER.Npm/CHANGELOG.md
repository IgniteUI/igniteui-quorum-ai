# Changelog

All notable changes to `@igniteui/maker-mcp` will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

---

## [1.0.0] - 2026-01-01

### Added

- Initial release of `@igniteui/maker-mcp`.
- Bootstrap script (`bin/maker-mcp.js`) that downloads the self-contained .NET 8 binary from GitHub Releases on first run and caches it locally.
- Platform support: `win-x64`, `osx-x64`, `osx-arm64`, `linux-x64`.
- Configurable cache location via `MAKER_MCP_CACHE` environment variable.
- MCP STDIO transport (`--stdio` flag) for Claude Desktop and other STDIO-based MCP clients.
- Three MCP tools:
  - `maker_plan` — decomposes a task into a validated, ordered step list using consensus-based voting.
  - `maker_execute` — executes a step list produced by `maker_plan` with per-batch voting and rejection feedback.
  - `maker_plan_and_execute` — convenience tool that runs both phases in one call with streaming progress events.
- Configurable `batchSize` and `k` (consensus threshold) parameters on all three tools.
- Support for OpenAI, Anthropic, and Google AI providers, independently selectable per internal client (Planning, PlanVoting, Execution, ExecutionVoting).
- All configuration via environment variables using `__` as the section separator (e.g. `Executor__AIProviderKeys__OpenAI`).

[Unreleased]: https://github.com/IgniteUI/MAKER/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/IgniteUI/MAKER/releases/tag/v1.0.0
