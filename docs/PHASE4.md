# Phase 4: Workflow Execution Engine + Core Version Identity

## Branches

Phase 4 integration branch: `dev/phase4_main`, created from `main` (`3c29a8e`) after Phase 3 was merged. Create work branches from it and target their PRs back to it. Merge to `main` after Phase 4 implementation, internal testing and approval. Do not commit automatically.

Work branches:

1. `feat/phase4-workflow-publishing`
2. `feat/phase4-workflow-runtime`
3. `feat/phase4-form-trigger`
4. `feat/phase4-acceptance`

## Scope

In scope ([Roadmap](ROADMAP.md#phase-4--workflow-execution-engine--core-version-identity)):

- publishing a Draft `WorkflowVersion` as an immutable Published version, and creating the next Draft
- workflow instances pinned to one exact `WorkflowVersionId`
- distinct `WorkflowInstance`, `WorkflowExecution` and `WorkflowExecutionStep` persistence
- node execution, deterministic routing, workflow variables (context)
- instance/execution history, correlation IDs, structured failures and status tracking

Out of scope, by later phase:

| Not in Phase 4 | Phase |
|---|---|
| Approval tasks, inbox, approve/reject UI, Portal requester status | 5 |
| Switch/calculation nodes, data mapping, timed waits, parallel branches | 6 |
| Version history, comparison, restore and archive UI | 7 |
| Notifications, SLA, background workers | 8 |
| Visual step debugger, dry runs, node testing, retry from failed step | 9 |

## Decisions (approved 2026-10-10)

1. **Condition evaluation:** evaluate Condition expressions in Phase 4 with the Phase 3 grammar, so published workflows with conditions can run. A missing variable or a type mismatch fails the run with a structured error. Phase 6 still adds switch/calculation nodes and variable typing.
2. **Form field keys:** add a short `key` to form fields, generated from the label and editable, so that conditions read `request.amount` instead of a field GUID. Published versions without keys derive them from labels.
3. **Execution model:** synchronous, in-process during the triggering request. No queue or background worker yet (PROJECT_RULES 18).
4. **Approval nodes:** the instance enters a durable Waiting state at the node. Resuming (`approved`/`rejected`) is an internal service in Phase 4, covered by tests; Phase 5 adds approval tasks and the UI that calls it.
5. **Manual runs and run history:** Studio Owner/Admin in the active organization, MFA policy applied.
6. **Identical republish:** a draft whose graph matches the latest Published version cannot be published again (409 `no_changes`), as with forms.

## Data model

Slice 1 uses the existing `WorkflowVersion` (Status, `PublishedAt`). Slice 2 adds the `WorkflowRuntime` migration:

```text
WorkflowInstance          one business run, pinned to WorkflowVersionId for life
- Id, OrganizationId, WorkflowDefinitionId, WorkflowVersionId
- Status: Running | Waiting | Completed | Failed | Cancelled
- ContextJson, WaitingNodeId, Outcome
- Trigger (Manual | FormSubmitted), StartedByUserId, SourceSubmissionId
- CorrelationId, StartedAt, CompletedAt

WorkflowExecution         one start/resume activation (1 instance → N executions)
- Id, InstanceId, OrganizationId, Sequence, Reason (Start | Resume), Status
- TriggeredByUserId, CorrelationId, StartedAt, CompletedAt, ErrorCode, ErrorMessage

WorkflowExecutionStep     one node execution within an execution
- Id, ExecutionId, InstanceId, OrganizationId, Sequence, NodeId, NodeType, Status, Attempt
- InputJson, OutputJson, DurationMs, ErrorCode, ErrorMessage, StartedAt, CompletedAt
```

- An instance never changes its `WorkflowVersionId`. Completed steps and finished executions are immutable (DbContext guard).
- One instance per (workflow version, form submission), enforced by a unique index for idempotent form triggers.
- `WorkflowInstance` and `WorkflowExecution` are never used as synonyms (PROJECT_RULES 29).

## Publishing

`POST /api/workflows/{id}/publish` with `draftRevision`:

- **Checks:** the revision is current, structural validation passes, there are zero completeness issues, and the draft differs from the latest Published graph.
- **In one transaction:**
  - the draft becomes Published with `PublishedAt`
  - Draft v+1 is created with a copy of the graph
  - a `workflow.published` audit event is written
- **Concurrency:** a concurrent publish loses with 409, via the draft revision token and the unique version index.
- **Studio:** a Publish button in the designer, disabled while there are issues, unsaved changes or no changes since the last version. The Workflows list shows published versions.

## Engine

- `IWorkflowNodeHandler` per node type (ARCHITECTURE §8):
  - Start initializes the context.
  - Condition evaluates its expression and routes `true`/`false`.
  - Approval sets the instance to Waiting.
  - End completes the instance with its outcome.
- **Execution loop:** follow connections from the current node and persist a `WorkflowExecutionStep` per node, recording input, output, status and duration. It stops at End, a durable wait or a failure, with a guard of 200 steps per execution.
- **Resume:** `ResumeAsync(instance, node, output)` creates a new `WorkflowExecution` with Reason Resume for the same instance and continues from the waiting node's chosen output.
- **Failures:** structured `ErrorCode`/`ErrorMessage` on the step, the execution and the instance (Failed). There are no silent retries.
- **Correlation ID:** taken from the request (or generated) and stored on the instance, executions and steps.
- **Audit:** `workflow.instance_started`, `workflow.instance_completed`, `workflow.instance_failed`. Debug data stays in steps, separate from audit (PROJECT_RULES 9).

## Starting runs and history

- **Manual:** `POST /api/workflows/{id}/runs` starts a run on the latest Published version. It supports an `Idempotency-Key` header and is Studio Owner/Admin only.
- **Form submitted (slice 3):** saving a form submission starts one run for each workflow whose latest Published version starts from that form. The context is `request.<fieldKey>`, plus submission metadata.
- **History:** `GET /api/workflows/{id}/runs` and `GET /api/workflow-instances/{id}` (with executions and steps). Studio shows a Runs view per workflow and a run detail page.

## Delivery sequence

1. **Publishing and version identity:** publish API, immutability and next draft, Studio Publish button and version display, audit events, tests.
2. **Runtime and engine:**
   - the `WorkflowRuntime` migration and the node handlers
   - Condition evaluation, durable waits and internal resume
   - manual runs with idempotency, and the run history API and Studio UI
   - tests
3. **Form trigger:** form field keys, starting runs from submissions (idempotent per submission), context mapping, tests.
4. **Acceptance:**
   - this document's checklists and ARCHITECTURE notes (runtime model, expression evaluation)
   - PROGRESS/README updates
   - browser acceptance, then the merge to `main`

## Current implementation

Slice 1 (publishing) is implemented on `feat/phase4-workflow-publishing`; automated and browser checks passed on 2026-10-10. It includes:

- `POST /api/workflows/{id}/publish`
- the draft becomes an immutable Published version, and the next draft opens with a copy of its graph. Both steps run in one transaction, as two ordered saves so that the one-Draft index holds.
- `latestPublished` and `draft.hasUnpublishedChanges` in workflow details, and `latestPublishedVersionNumber` in the list
- a **Publish version N** button in the designer
- `workflow.published` audit events

No migration is needed; publishing uses the Phase 3 `WorkflowVersion` columns.

## Slice 2: workflow runtime

Implemented on `feat/phase4-workflow-runtime`; automated and browser checks passed on 2026-10-10. It includes:

- the `WorkflowRuntime` migration: `WorkflowInstances`, `WorkflowExecutions` and `WorkflowExecutionSteps`, with the unique (version, form submission) index for slice 3 and a unique (workflow, user, Idempotency-Key) index for manual runs
- `WorkflowEngine` with one `IWorkflowNodeHandler` per node type. Each activation is saved in one write: the instance state, the execution, every step and the audit events. Finished executions and steps are immutable, and an instance never changes its version or is deleted (DbContext guard).
- Condition evaluation (`WorkflowExpression.Evaluate`): numbers, text, true/false and empty values. `&&` and `||` short-circuit. A missing variable (`variable_missing`), comparing different types or a non-true/false result (`type_mismatch`) fails the run.
- Approval nodes leave the instance Waiting with `WaitingNodeId`. `WorkflowEngine.ResumeAsync` (internal, tested) adds a Resume execution that continues from the chosen output; the instance revision is a concurrency token.
- `POST /api/workflows/{id}/runs` with `{ "request": { ... } }` and an optional `Idempotency-Key`. Only workflows whose latest Published version has a Manual trigger can be run manually (409 `trigger_not_manual`). A retry with the same key and values returns the same run (200); different values return 409 `idempotency_key_reused`.
- `X-Correlation-ID` is taken from the request when well formed (otherwise generated), echoed on the response and stored on the instance, executions and steps.
- `GET /api/workflows/{id}/runs` (25 per page, newest first) and `GET /api/workflow-instances/{id}` (pinned version graph, variables, executions and steps)
- Studio: **Runs** (`/workflows/{id}/runs`) with a Start run form and history, and a run detail page with each execution’s steps, input/output and errors
- audit events `workflow.instance_started`, `workflow.instance_completed` and `workflow.instance_failed`

Run variables are `request.*` (the values sent with the run) and `trigger.type`/`trigger.startedAt`.

## Slice 3: form trigger

Implemented on `feat/phase4-form-trigger`; automated and browser checks passed on 2026-10-10. No migration: keys are stored in the form's field JSON, and the slice 2 unique (version, form submission) index is used. It includes:

- **Field keys:** each form field has a `key` (letters, digits and underscores, starting with a letter or underscore, at most 40 characters, not `true`/`false`, unique per form ignoring case). Saving fields generates missing keys from labels ("Amount (USD)" → `amountUsd`, with `2`, `3`… for repeats) and keeps edited ones. Versions published before keys existed derive them from their labels the same way.
- **Starting runs:** a new Portal submission (with or without files) starts one run for each workflow whose latest version published at the time of the submission starts with "When a form is submitted" for that form. Runs are started after the submission is saved, by the submitter, with the request's correlation ID.
- **Idempotency:** a submission retry starts only runs that are still missing (for example after a crash) and never one for a workflow published after the submission. The unique index stops concurrent retries from starting a run twice.
- **Failures:** a run that fails (for example `variable_missing`) is recorded as a Failed run; the submission still succeeds.
- **Variables:** `request.<key>` for every field of the submitted form version (numbers, text, dates and options as text, checkboxes as true/false, files as the attachment ID, unanswered fields as empty values), `submission.id`, `submission.formId`, `submission.formVersion`, `submission.submittedAt`, and `trigger.type` = `FormSubmitted`.
- **Studio:** a **Workflow key** setting per field in the form designer; the Condition settings list the trigger form's `request.*` names; the run detail shows the source submission ID.

## Slice 4: acceptance

On `feat/phase4-acceptance`. No code changes. It includes:

- the runtime model and expression evaluation in [Architecture](ARCHITECTURE.md#7-workflow-runtime)
- the final checklist below, PROGRESS and README updates
- repaired character encoding in this document

## Final Phase 4 acceptance

Each slice passed its own automated and browser checks before merging (#20, #21, #22). Before merging `dev/phase4_main` into `main`:

- [x] Backend tests (60), lint, typecheck and production builds pass on the merged code (`654fd07`, 2026-10-10).
- [x] One end-to-end browser pass over local HTTPS (user-reported, 2026-10-10; restart the API, Studio and Portal first):
  1. In Studio, publish a form with Currency "Amount" (key `amount`).
  2. Design "Expense approval": Start (When a form is submitted, that form) → Condition `request.amount > 1000`; true → Approval → End “Approved” / End “Rejected”; false → End “Auto-approved”. Save with no issues and publish version 1.
  3. In the Portal, submit amount 250: the workflow's Runs show a Completed “Auto-approved” run on Version 1.
  4. Submit amount 5000: the run is **Waiting** at the approval node, and its detail shows each step's input and output.
  5. Change a label, publish version 2 and submit again: the new run shows Version 2 and the earlier runs keep Version 1.
- [x] Spot-check regressions of Phases 2 and 3 (passed 2026-10-10): a form used by no workflow still submits; a draft-only workflow still saves and shows its issues.
- [ ] Review the docs and approve the PR from `dev/phase4_main` into `main`.

Roadmap deliverables:

| Deliverable | Delivered by |
|---|---|
| Publish a Draft as an immutable version and open the next Draft | Publish API and Studio Publish button (#20) |
| Instances pinned to one `WorkflowVersionId` | `WorkflowInstance` with the DbContext version guard (#21) |
| Separate instance, execution and step persistence | `WorkflowRuntime` migration (#21) |
| Node execution, deterministic routing, workflow variables | `WorkflowEngine`, node handlers, Condition evaluation, `request.*`/`submission.*` context (#21, #22) |
| History, correlation IDs, structured failures, status tracking | Run history API and Studio Runs pages, `X-Correlation-ID`, error codes on steps, executions and instances (#21) |
| Form-submitted trigger | Field keys and idempotent runs per submission (#22) |

## Internal test: form trigger

Restart the API, Studio and Portal first.

1. In Studio, open a form and add Currency "Amount" and Checkbox "Urgent". The **Workflow key** placeholders read `amount` and `urgent`. Change Urgent's key to `isUrgent`, save and publish.
2. Design a workflow: Start (When a form is submitted, that form) → Condition. Its settings list `request.amount` and `request.isUrgent`. Use `request.amount > 1000`; connect true → Approval → End “Approved” / End “Rejected”, and false → End “Auto-approved”. Save and publish.
3. **Runs** for this workflow: Start run is refused ("starts when its form is submitted").
4. In the Portal, submit the form with amount 250. The workflow's Runs show a Completed “Auto-approved” run, trigger "Form submitted", started by the Portal user. The run detail shows the submission ID and `request.amount`, `request.isUrgent` and `submission.*` variables.
5. Submit with amount 5000: the run is **Waiting** at the approval node.
6. Publish a second workflow on the same form whose condition uses `request.total > 1`. Submit again: the submission succeeds, the first workflow runs, and the second run **Failed** with `variable_missing`.
7. Forms not used by a published workflow, draft-only workflows and Manual workflows start no runs.

Automated checks cover key generation and validation, keys assigned on save, run start and variables, correlation IDs, failing workflows not failing the submission, draft-only and Manual workflows ignored, retries not starting duplicates or later-published workflows, and repeated triggering of one submission.

## Internal test: workflow runtime

Apply the migration (`dotnet ef database update -p backend/FlowOps.Api`) and restart the API and Studio first.

1. Design a workflow: Start (Manual) → Condition `request.amount > 1000`. Connect true → Approval → End “Approved” / End “Rejected”, and false → End “Auto-approved”. Save and publish.
2. Open **Runs** from the designer or the Workflows list (**View runs**).
3. Start a run with `{ "amount": 250 }`: “Run completed: Auto-approved.” The run detail shows Start → Condition (output `result: false`) → End, all Completed.
4. Start a run with `{ "amount": 5000 }`: the run is **Waiting** at the approval node.
5. Start a run with `{ "total": 5 }`: the run **Failed** with `variable_missing`; with `{ "amount": "lots" }` it fails with `type_mismatch`. The failed step shows the error.
6. Edit the draft, publish version 2, and start another run: it shows Version 2, while the earlier runs keep Version 1.
7. Runs and run details are not visible from another organization, and a Portal account cannot start runs.

Automated checks cover the expression evaluator, routing, the durable wait and resume (including wrong node/output and a second resume), structured failures, idempotent retries, version pinning across republishing, history paging data, organization and Portal isolation, audit events, the 200-step guard and runtime immutability.

## Internal test: workflow publishing

1. Open a workflow's designer.
   - The header shows **Draft version 1 · Not published yet**.
   - **Publish version 1** is enabled only when there are no unsaved changes and no issues.
2. Make the draft incomplete, for example by disconnecting an output, and save. Publish is disabled with "Resolve the issues before publishing."
3. Complete the draft, save, and publish. After you confirm:
   - the message reads "Version 1 published. Further edits go into draft version 2"
   - the header shows **Draft version 2 · Published version 1**
4. Publish is now disabled ("No changes since version 1"). Change a label, save, and publish version 2.
5. The Workflows list shows **Published v2 · Draft v3**.
6. Publish from a stale tab: it is rejected with "This draft changed in another session. Reload it before publishing."
7. A Portal account cannot publish.

Automated checks cover:

- missing and stale revisions
- incomplete drafts refused with their issue list
- v1 published and Draft v2 opened with the same graph
- an unchanged republish refused, and v2 published after an edit
- v1 keeping its original graph
- Portal denial and `workflow.published` audit events
