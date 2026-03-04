# MAKER Framework

## Overview

MAKER stands for **M**aximal **A**gentic decomposition, first-to-ahead-by-**K** **E**rror correction, and **R**ed-flagging. It is an AI orchestration framework designed to enhance the reliability and accuracy of AI systems through consensus-based voting, proactive validation, and intelligent task decomposition.
The framework is based on the paper titled [Solving a million-step LLM task with zero errors](https://arxiv.org/pdf/2511.09030) by Cognizant AI Lab.

## Key Features

- **Dual-Phase Orchestration**: Separates planning and execution into distinct phases, each with its own AI models and voting mechanisms for specialized task handling.

- **Consensus-Based Voting System**: Implements a "first-to-ahead-by-K" voting algorithm where multiple AI agents vote on proposed plans and execution outputs. A decision is accepted only when the margin of agreement exceeds K votes, ensuring high-confidence outcomes.

- **Adaptive Task Decomposition**: Breaks down complex prompts into granular, executable steps with dependency tracking. Steps can specify required prerequisites and format requirements for precise control flow.

- **Real-Time Red-Flag Validation**: Employs extensible validators that automatically retry requests when outputs fail quality checks, preventing invalid responses from propagating through the system.

- **Batch Processing with Configurable Parallelism**: Processes steps in configurable batch sizes with interleaved concurrent voting to optimize throughput while maintaining quality control.

- **Event-Driven Progress Tracking**: Provides rich event hooks for real-time monitoring and visualization of planning and execution progress.

- **Rejection Feedback Loop**: When votes reject a proposal, the system automatically incorporates rejection reasons into the next iteration, enabling continuous refinement.

- **Format-Aware Execution**: Supports custom output formats (JSON, plaintext, etc.) that can be injected into specific steps requiring structured data.

## Working Principle

The MAKER framework operates through a sophisticated dual-phase orchestration system that separates planning and execution, each employing consensus-based voting and error correction mechanisms. Here's how it works:

### Architecture Overview

The framework consists of three main components:

1. **Executor**: The main entry point that coordinates both planning and execution phases
2. **PlanningOrchestrator**: Manages task decomposition and plan validation
3. **ExecutionOrchestrator**: Handles step execution and output verification

Each orchestrator uses dedicated AI clients:
- **Planning Client**: Generates task decomposition proposals
- **Plan Voting Client**: Validates proposed plans through consensus
- **Execution Client**: Executes individual steps
- **Execution Voting Client**: Validates execution outputs

### Phase 1: Planning Phase

**Task Decomposition:**
The planning phase breaks down complex tasks into granular, executable steps through an iterative process:

1. The Planning Client proposes a batch of steps (configurable batch size, default 2)
2. Each proposed step includes:
   - `Task`: Description of the step to perform
   - `RequiredSteps`: Dependencies (indices of prerequisite steps)
   - `RequiresFormat`: Flag indicating if the step needs format information
   - `ExtraContext`: Additional context for step execution

**Consensus Voting (First-to-Ahead-by-K):**
After steps are proposed, the Plan Voting Client evaluates them:

- Multiple voting agents (K agents) simultaneously review the proposal
- Votes can be: "Yes" (approve), "No" (reject with reason), or "End" (task complete)
- The voting continues until one of these conditions is met:
  - **Approval**: `Yes votes >= No votes + K` — The proposal is accepted
  - **Rejection**: `No votes >= Yes votes + K` — The proposal is rejected
  - **Completion**: `End votes = K` — All voters agree the task is complete
  - **Contentious**: `Total votes >= K × 4` without consensus — Voting round fails

**Rejection Feedback Loop:**
When a proposal is rejected:

1. Rejection reasons from "No" votes are collected
2. The framework retries with the rejection feedback incorporated into the next proposal
3. If retries exceed the maximum (default 5), planning restarts from scratch
4. This ensures continuous refinement until a high-quality plan emerges

**Plan Completion:**
The planning phase continues proposing and voting on new steps until a step with `Task = "End"` is accepted, indicating the plan is complete.

### Phase 2: Execution Phase

**Batch Processing:**
Execution processes the planned steps in configurable batches (default 2 steps per batch):

1. Steps are executed in order, respecting `RequiredSteps` dependencies
2. Each batch maintains a cumulative `state` object that evolves as steps complete
3. Format information is injected into prompts when `RequiresFormat = true`

**Step Execution:**
For each batch of steps:

1. The Execution Client receives:
   - The original task description
   - Current batch of steps to execute
   - Previous state (output from prior batches)
   - Output format specification (if `RequiresFormat = true` for any step in the batch)
   - Extra context (if `ExtraContext` is specified for any step in the batch)
   - Rejection feedback (if previous attempt failed)

2. The client produces a new state representing the updated solution

**Context Injection:**
Steps can provide additional information through two specialized fields:

- **Format Context** (`RequiresFormat`): When set to `true`, the global output format specification is injected into the execution prompt, guiding the AI to produce correctly structured output (e.g., JSON schema, specific data formats)
- **Extra Context** (`ExtraContext`): Allows individual steps to carry step-specific instructions, constraints, or reference data that supplement the main task description. This is particularly useful for steps requiring specialized knowledge or particular execution guidelines

Both context types are dynamically inserted into the execution prompt only when present, keeping prompts lean while enabling precise control over complex execution scenarios.

**Output Validation:**
After each execution, the Execution Voting Client validates the output:

- Uses the same first-to-ahead-by-K voting mechanism as planning
- Compares the new state against the previous state and task requirements
- Voters evaluate whether the steps were correctly executed
- Rejection reasons guide retry attempts

**Retry Mechanism:**
If execution is rejected:

1. Rejection reasons are incorporated into the next execution attempt
2. The same batch is re-executed with feedback (up to 5 retries by default)
3. If max retries exceeded, state is reset and one final attempt is made
4. This ensures eventual convergence to valid outputs

### Red-Flag Validation System

Throughout both phases, the framework employs extensible validators that act as quality gates:

**Guarded Requests:**
Every AI client request is wrapped in a "guarded" call that:

1. Sends the prompt to the AI client
2. Extracts JSON from code blocks if present (using regex matching)
3. Runs the response through all configured validators
4. If validation fails (throws `AIRedFlagException`):
   - The failure message is appended to the original prompt
   - The request is automatically retried
   - This loop continues until validation passes

**Built-in Validators:**
- `AIRedFlagMinLengthValidator`: Ensures responses meet minimum length requirements
  - Planning: 100 characters minimum
  - Voting: 2 characters minimum

**Extensibility:**
Custom validators can be added by implementing `IAIRedFlagValidator`, enabling domain-specific quality checks (e.g., JSON schema validation, constraint verification, content safety).

### Interleaved Concurrent Voting

To optimize throughput while maintaining quality, the framework uses interleaved execution:

1. K voting requests are generated simultaneously
2. Results are processed using the `TaskUtils.Interleaved()` utility
3. This allows votes to be evaluated as they complete (not waiting for all K votes)
4. Voting can terminate early once the K-margin threshold is reached
5. Invalid votes trigger immediate replacement vote generation

### State Management

The execution phase maintains a cumulative state:

1. **Initial State**: Empty string
2. **Step Execution**: Each batch transforms the state
3. **State Evolution**: New state is validated against previous state
4. **Format Awareness**: Steps marked with `RequiresFormat` receive the output format specification
5. **Final State**: The last batch's state is the final solution

### Event-Driven Monitoring

The framework provides rich event hooks for real-time observation:

**Planning Events:**
- `OnStepsProposed`: Fired when new steps are proposed
- `OnStepsAdded`: Fired when steps are accepted and added to the plan
- `OnStepsRejected`: Fired when a proposal is rejected
- `OnPlanVoteChanged`: Fired as each vote is received

**Execution Events:**
- `OnExecutionStarted`: Fired when a batch begins execution
- `OnStateChanged`: Fired when state is updated
- `OnExecutionVoteChanged`: Fired as each execution vote is received

These events enable real-time visualization, progress tracking, and debugging without affecting the core orchestration logic.

### Error Correction Mechanisms

The framework employs multiple layers of error correction:

1. **Red-Flag Validators**: Catch malformed responses before they enter the voting system
2. **Consensus Voting**: Reject low-quality proposals through democratic agreement
3. **Rejection Feedback**: Incorporate failure reasons into retry attempts
4. **Retry Limits**: Prevent infinite loops while allowing sufficient correction attempts
5. **State Reset**: Clear corrupted state after exhausting retries
6. **Contentious Detection**: Identify when voters fundamentally disagree and fail gracefully

### Scalability and Performance

**Configurable Parallelism:**
- Batch size determines how many steps are planned/executed at once
- K value controls the consensus threshold (higher K = more agents = higher confidence)
- Interleaved voting processes votes as they arrive, not in rigid batches

**Optimal Settings:**
- Smaller batches (1-2 steps): More granular control, slower execution
- Larger batches (5-10 steps): Faster execution, less quality control
- Higher K values (8-15): Higher confidence, more AI calls
- Lower K values (3-5): Faster decisions, lower confidence

The framework balances speed and reliability through these tunable parameters, adapting to different use cases from high-stakes tasks requiring zero errors to rapid prototyping scenarios.

