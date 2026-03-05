# @igniteui/maker-mcp

**MAKER** — **M**aximal **A**gentic decomposition, first-to-ahead-by-**K** **E**rror correction, **R**ed-flagging.

An MCP server that brings the MAKER AI orchestration framework to any MCP-compatible client (Claude Desktop, VS Code, Cursor, etc.). It decomposes complex tasks into validated steps using a consensus-based multi-model voting system, then executes them with built-in error correction.

Based on the paper [_Solving a million-step LLM task with zero errors_](https://arxiv.org/pdf/2511.09030) by Cognizant AI Lab.

---

## Table of Contents

- [How It Works](#how-it-works)
- [Requirements](#requirements)
- [Installation](#installation)
  - [Claude Desktop](#claude-desktop)
  - [VS Code (GitHub Copilot)](#vs-code-github-copilot)
  - [Other MCP Clients](#other-mcp-clients)
- [Configuration](#configuration)
  - [AI Provider Keys](#ai-provider-keys)
  - [Model Selection](#model-selection)
  - [Tuning Parameters](#tuning-parameters)
- [MCP Tools](#mcp-tools)
  - [maker\_plan](#maker_plan)
  - [maker\_execute](#maker_execute)
  - [maker\_plan\_and\_execute](#maker_plan_and_execute)
- [Usage Examples](#usage-examples)
- [Caching](#caching)
- [Supported Platforms](#supported-platforms)
- [Troubleshooting](#troubleshooting)

---

## How It Works

MAKER operates in two phases, each guarded by a **first-to-ahead-by-K voting** system:

```
Prompt
  │
  ▼
┌─────────────────────────────────────────────────────┐
│  Phase 1 — Planning                                 │
│                                                     │
│  Planning Client proposes steps (batch by batch)    │
│  Plan Voting Client votes Yes / No / End            │
│  Rejection reasons feed back into next proposal     │
│  Continues until an "End" step is accepted          │
└─────────────────────────────────────────────────────┘
  │
  ▼ List<Step>
┌─────────────────────────────────────────────────────┐
│  Phase 2 — Execution                                │
│                                                     │
│  Execution Client runs each batch of steps          │
│  Execution Voting Client validates the output       │
│  Rejection reasons trigger a retry with feedback    │
│  State is carried forward across batches            │
└─────────────────────────────────────────────────────┘
  │
  ▼ Final result (string)
```

A decision is accepted only when **|Yes − No| ≥ K**, ensuring high-confidence consensus before moving forward.

---

## Requirements

- **Node.js ≥ 18**
- At least one AI provider API key:
  - [OpenAI](https://platform.openai.com/api-keys)
  - [Anthropic](https://console.anthropic.com/settings/keys)
  - [Google AI](https://aistudio.google.com/app/apikey)

The native binary (~50 MB) is downloaded and cached on first run. No .NET installation required.

---

## Prerequisites — GitHub Packages Registry

This package is published to **GitHub Packages**, not npmjs.com. Before installing, configure npm to fetch `@igniteui` packages from GitHub Packages:

```bash
npm config set @igniteui:registry https://npm.pkg.github.com
```

Then authenticate with a **GitHub Personal Access Token (PAT)** that has the `read:packages` scope:

```bash
npm config set //npm.pkg.github.com/:_authToken YOUR_GITHUB_PAT
```

> **Tip:** You can also place these settings in a project-level `.npmrc` file:
>
> ```ini
> @igniteui:registry=https://npm.pkg.github.com
> //npm.pkg.github.com/:_authToken=${GITHUB_TOKEN}
> ```

---

## Installation

### Claude Desktop

1. Open your Claude Desktop config file:
   - **Windows**: `%APPDATA%\Claude\claude_desktop_config.json`
   - **macOS**: `~/Library/Application Support/Claude/claude_desktop_config.json`

2. Add the `maker` server to the `mcpServers` block:

```json
{
  "mcpServers": {
    "maker": {
      "command": "npx",
      "args": ["-y", "@igniteui/maker-mcp", "--stdio"],
      "env": {
        "Executor__AIProviderKeys__OpenAI": "<your-openai-key>"
      }
    }
  }
}
```

3. Restart Claude Desktop. The first startup downloads the binary for your platform (~30 s on a typical connection).

### VS Code (GitHub Copilot)

Add to your `.vscode/mcp.json` (workspace) or user MCP settings:

```json
{
  "servers": {
    "maker": {
      "command": "npx",
      "args": ["-y", "@igniteui/maker-mcp", "--stdio"],
      "env": {
        "Executor__AIProviderKeys__OpenAI": "<your-openai-key>"
      }
    }
  }
}
```

### Other MCP Clients

Any client that supports the MCP STDIO transport can use:

```bash
npx -y @igniteui/maker-mcp --stdio
```

Pass API keys as environment variables (see [Configuration](#configuration)).

---

## Configuration

All configuration is supplied through **environment variables**. The naming convention uses double-underscore (`__`) as the section separator.

### AI Provider Keys

| Environment Variable | Description |
|---|---|
| `Executor__AIProviderKeys__OpenAI` | OpenAI API key |
| `Executor__AIProviderKeys__Anthropic` | Anthropic API key |
| `Executor__AIProviderKeys__Google` | Google AI API key |

You only need to set keys for the providers you actually use.

### Model Selection

Each of the four internal clients can target a different provider and model:

| Environment Variable | Default | Description |
|---|---|---|
| `Executor__Clients__Planning__Provider` | `OpenAI` | Provider for step proposal |
| `Executor__Clients__Planning__Model` | `gpt-5.1` | Model for step proposal |
| `Executor__Clients__PlanVoting__Provider` | `OpenAI` | Provider for plan voting |
| `Executor__Clients__PlanVoting__Model` | `gpt-5.1` | Model for plan voting |
| `Executor__Clients__Execution__Provider` | `OpenAI` | Provider for step execution |
| `Executor__Clients__Execution__Model` | `gpt-5.1` | Model for step execution |
| `Executor__Clients__ExecutionVoting__Provider` | `OpenAI` | Provider for execution voting |
| `Executor__Clients__ExecutionVoting__Model` | `gpt-5.1` | Model for execution voting |

Valid `Provider` values: `OpenAI`, `Anthropic`, `Google`.

**Example — mix providers to balance cost and quality:**

```json
"env": {
  "Executor__AIProviderKeys__OpenAI":    "<openai-key>",
  "Executor__AIProviderKeys__Anthropic": "<anthropic-key>",
  "Executor__Clients__Planning__Provider":        "Anthropic",
  "Executor__Clients__Planning__Model":           "claude-opus-4-5",
  "Executor__Clients__PlanVoting__Provider":      "OpenAI",
  "Executor__Clients__PlanVoting__Model":         "gpt-4.1-mini",
  "Executor__Clients__Execution__Provider":       "Anthropic",
  "Executor__Clients__Execution__Model":          "claude-opus-4-5",
  "Executor__Clients__ExecutionVoting__Provider": "OpenAI",
  "Executor__Clients__ExecutionVoting__Model":    "gpt-4.1-mini"
}
```

### Tuning Parameters

The tools expose two parameters you can adjust per call:

| Parameter | Default | Description |
|---|---|---|
| `batchSize` | `2` | Steps proposed / executed per round. Higher values mean fewer rounds but larger prompts. |
| `k` | `10` | Voting margin required for consensus. Higher values are more conservative and use more tokens. |

---

## MCP Tools

### `maker_plan`

Decomposes a task into a validated, ordered list of steps without executing them. Returns a JSON array of `Step` objects.

**Parameters**

| Name | Type | Default | Description |
|---|---|---|---|
| `prompt` | `string` | — | The task or goal to plan |
| `batchSize` | `integer` | `2` | Steps proposed per voting round |
| `k` | `integer` | `10` | Consensus threshold |

**Returns** — a JSON array:

```json
[
  {
    "task": "Analyse the existing component API surface",
    "requiredSteps": [],
    "requiresFormat": false,
    "extraContext": ""
  },
  {
    "task": "Identify breaking changes relative to the previous major version",
    "requiredSteps": [0],
    "requiresFormat": false,
    "extraContext": ""
  }
]
```

---

### `maker_execute`

Executes a step list produced by `maker_plan`. Each batch is validated by the voting system before the state is advanced.

**Parameters**

| Name | Type | Default | Description |
|---|---|---|---|
| `stepsJson` | `string` | — | JSON array from `maker_plan` |
| `prompt` | `string` | — | The original task (provides context) |
| `batchSize` | `integer` | `2` | Steps executed per round |
| `k` | `integer` | `10` | Consensus threshold |

**Returns** — the final accumulated result as a string.

---

### `maker_plan_and_execute`

Convenience tool that runs both phases in a single call. Streaming progress events are sent between phases.

**Parameters**

| Name | Type | Default | Description |
|---|---|---|---|
| `prompt` | `string` | — | The task or goal |
| `batchSize` | `integer` | `2` | Steps per round (planning and execution) |
| `k` | `integer` | `10` | Consensus threshold |

**Returns** — the final accumulated result as a string.

---

## Usage Examples

### Simple one-shot task

Ask your MCP client (e.g. Claude):

> Use maker_plan_and_execute to write a detailed comparison of REST vs GraphQL for a technical blog post.

### Inspect the plan before executing

> 1. Use maker_plan to create a plan for migrating a PostgreSQL schema to a multi-tenant design.
> 2. Show me the steps.
> 3. Use maker_execute with those steps to carry out the migration script.

### Reduce cost with a smaller K

For exploratory or low-stakes tasks you can lower `k` to reduce token usage:

> Use maker_plan_and_execute with k=3 to draft a project README for a Node.js CLI tool.

### Use Anthropic for planning, OpenAI for voting

Set in your MCP config env (see [Model Selection](#model-selection)), then call normally — the client selection is transparent to the tool caller.

---

## Caching

The native binary is cached after the first download so subsequent starts are instant.

| Platform | Default cache location |
|---|---|
| Windows | `%LOCALAPPDATA%\maker-mcp\{version}\{rid}\` |
| macOS / Linux | `~/.cache/maker-mcp/{version}/{rid}/` |

Override with the `MAKER_MCP_CACHE` environment variable:

```json
"env": {
  "MAKER_MCP_CACHE": "/opt/maker-mcp-cache"
}
```

To force a re-download, delete the cache directory and restart your MCP client.

---

## Supported Platforms

| Platform | Architecture | RID |
|---|---|---|
| Windows | x64 | `win-x64` |
| macOS | x64 (Intel) | `osx-x64` |
| macOS | arm64 (Apple Silicon) | `osx-arm64` |
| Linux | x64 | `linux-x64` |

---

## Troubleshooting

**macOS quarantine warning**

If macOS blocks the binary after download, remove the quarantine attribute:

```bash
xattr -d com.apple.quarantine ~/.cache/maker-mcp/<version>/osx-arm64/maker-mcp
```

**Binary not found / HTTP error on first run**

The bootstrap script downloads from GitHub Releases. Make sure the version in `package.json` has a corresponding GitHub Release with the platform tarballs attached. Check your network/proxy settings if the download fails.

**All votes reject every proposal**

- Verify your API key is valid and has sufficient quota.
- Confirm the chosen model name is correct for the selected provider.
- Try lowering `k` to `3` to make consensus easier to reach while debugging.

**`Unsupported platform` error**

Only the four RIDs listed above are supported. ARM Linux is not currently packaged. Open an issue at [github.com/IgniteUI/MAKER](https://github.com/IgniteUI/MAKER) to request additional platforms.
