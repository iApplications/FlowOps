# Phase 3: Workflow Designer

## Branches

Phase 3 integration branch: `dev/phase3_main`, created from `main` after Phase 2 was merged. Create work branches from it and target their PRs back to it. Merge to `main` after Phase 3 implementation, internal testing and approval. Do not commit automatically.

Work branches:

1. `feat/phase3-workflow-foundation`
2. `feat/phase3-workflow-canvas`
3. `feat/phase3-workflow-validation`

## Scope

In scope ([Roadmap](ROADMAP.md#phase-3--workflow-designer)): a workflow list, creating workflows and editing their name and description, a drag-and-drop canvas, Start/Condition/Approval/End nodes, connections, node configuration, draft saving with a draft `WorkflowVersion`, and basic workflow validation.

Out of scope, by later phase:

| Not in Phase 3 | Phase |
|---|---|
| Publishing, frozen versions in use, workflow instances and executions | 4 |
| Approval tasks, inbox, request changes | 5 |
| Evaluating conditions, switch/calculation nodes, wait states | 6 |
| Version history, comparison and archive UI | 7 |
| Portal changes, deleting workflows, new permission roles | Not planned for Phase 3 |

## Decisions (approved 2026-10-08)

- **Canvas:** React Flow (`@xyflow/react`, MIT), pinned to an exact version.
- **Approval node:** an assignee role (Owner, Admin or Member), a label and optional instructions, with `approved` and `rejected` outputs. Phase 5 adds request changes.
- **Start trigger:** `Manual`, or `FormSubmitted` with a form from the same organization. Phase 3 stores and validates the trigger; Phase 4 executes it.
- **Incomplete drafts:** they can be saved; completeness issues are listed and the affected nodes highlighted. Phase 4 publishing will require zero issues.
- **Cycles:** not allowed in Phase 3. Revisit with request changes and wait states in Phases 5–6.

## Data model

```text
WorkflowDefinition                 WorkflowVersion
- Id, OrganizationId               - Id, WorkflowDefinitionId, OrganizationId
- Name (≤120), Description (≤2000) - VersionNumber (unique per definition)
- Revision (optimistic concurrency) - Status: Draft | Published
- CreatedAt, UpdatedAt
                                   - DraftRevision (optimistic concurrency)
                                   - GraphJson (nodes, connections, layout)
                                   - CreatedAt, UpdatedAt, PublishedAt
```

- Name and description belong to the definition and are not versioned. The definition's `Revision` guards metadata edits, and the version's `DraftRevision` guards graph saves, so the two kinds of edit never conflict with each other.
- Creating a workflow creates **Draft v1** with a connected Start → End graph. A definition has at most one Draft version, enforced by a filtered unique index.
- Phase 4 publishes the draft by setting it to Published, which freezes it, and creates the next Draft version for later edits. No model change is needed.
- `FlowOpsDbContext` refuses to modify or delete a version that was stored as Published. The guard exists from Phase 3 so Phase 4 publishing is safe from its first release.
- The graph is one JSON document per version, with a `schemaVersion`:
  - `WorkflowNode`: `id`, `type`, `label`, `position`, type-specific `config`
  - `WorkflowConnection`: `id`, `source`, `sourceHandle`, `target`
- Runtime terms (`WorkflowInstance`, `WorkflowExecution`, `WorkflowExecutionStep`) belong to Phase 4 and are not used in Phase 3.

## Node types

| Node | Configuration | Outputs |
|---|---|---|
| Start | Trigger `Manual`, or `FormSubmitted` + a form in the same organization | `next` |
| Condition | `expression`: a controlled expression such as `request.amount >= 500` | `true`, `false` |
| Approval | Label, `assigneeRole` (Owner/Admin/Member), optional instructions | `approved`, `rejected` |
| End | Outcome label, for example "Approved", "Rejected" or "Completed" | None |

Condition expressions are parsed on the server by a purpose-built parser. The grammar allows:

- comparisons (`== != < <= > >=`)
- `&& || !`
- parentheses
- numbers, quoted strings, `true` and `false`
- dotted variable paths

Function calls, member invocation, statements and any code, SQL or shell syntax are rejected, and nothing is executed. Phase 3 validates syntax only; Phase 6 adds evaluation and variable typing.

## Validation

1. **Structural (rejects the save):**
   - known node types, unique IDs, and at most 100 nodes and 200 connections
   - label, instruction and expression length limits
   - connections reference existing nodes and valid output handles
   - no self-connections, and at most one connection per output handle
   - configuration shape matches the node type
   - a `FormSubmitted` form exists in the active organization
2. **Completeness (reported; saving is allowed):**
   - exactly one Start and at least one End
   - Start has no incoming connections; End has no outgoing connections
   - every output handle is connected
   - every node is reachable from Start and can reach an End
   - no cycles
   - condition expressions parse

## API

Studio only: Owner/Admin in the active organization, the MFA policy, CSRF on mutations. Portal accounts are denied.

```text
GET  /api/workflows               List definitions in the active organization
POST /api/workflows               Create a definition and Draft v1
GET  /api/workflows/{id}          Definition, draft version, graph, revision, issues
PUT  /api/workflows/{id}          Edit name/description (revision required)
PUT  /api/workflows/{id}/draft    Save the draft graph (draftRevision required)   [slice 2]
```

Audit events: `workflow.created`, `workflow.updated`, `workflow.draft_saved`.

## Delivery sequence

1. **Foundation:** model and `WorkflowDesigner` migration, the Published-version guard, list/create/detail/rename API, the Studio Workflows list and navigation, audit events, tests.
2. **Canvas:** React Flow designer at `/workflows/{id}`, saving the graph with revision checks, structural validation, a configuration panel for all four node types, an unsaved-changes guard, tests.
3. **Validation:** the expression parser, completeness issues, an issues panel with node highlighting, the Start form trigger, tests.
4. **Acceptance:**
   - this document's checklists and the API acceptance script extended to workflows
   - ARCHITECTURE notes on the graph format and expression grammar
   - PROGRESS/README updates
   - browser acceptance, then the merge to `main`

## Current implementation

Slice 1 (foundation) is implemented on `feat/phase3-workflow-foundation`; automated and browser checks passed on 2026-10-08. It includes:

- the `WorkflowDesigner` migration (applied to the local development database)
- the Published-version guard
- `GET/POST /api/workflows` and `GET/PUT /api/workflows/{id}`
- the Studio **Workflows** list and navigation
- audit events

The canvas and validation follow in slices 2 and 3. See [Progress](PROGRESS.md) for status.

## Internal test: workflow foundation

Apply the `WorkflowDesigner` migration and restart the API.

1. Sign in to Studio as an Owner/Admin with MFA satisfied. Open **Workflows** in the sidebar (`/workflows`).
2. Create "Expense approval" with a description. The list shows it with **Draft v1**.
3. Select it, change the name and description, save, and refresh: the changes persist.
4. Open the same workflow's details in two tabs and save from both. The second save is rejected with "This workflow changed. Reload it before saving."
5. Empty names and names over 120 characters are rejected.
6. Switch to another organization: the workflow is not listed, and its API URL returns 404.
7. A Studio account without MFA satisfied, and any Portal account, cannot reach the Workflows page or `/api/workflows`.

Automated checks cover:

- MFA and Portal denial, anonymous access and CSRF
- the Start → End starter graph
- revision conflicts that change nothing
- tenant isolation and audit events
- the database guards: Published versions cannot be modified or deleted, and a second Draft per workflow is rejected
