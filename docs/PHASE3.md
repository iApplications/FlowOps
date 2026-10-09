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
PUT  /api/workflows/{id}/draft    Save the draft graph (draftRevision required)
```

Audit events: `workflow.created`, `workflow.updated`, `workflow.draft_saved`.

## Delivery sequence

1. **Foundation:** model and `WorkflowDesigner` migration, the Published-version guard, list/create/detail/rename API, the Studio Workflows list and navigation, audit events, tests.
2. **Canvas:** React Flow designer at `/workflows/{id}`, saving the graph with revision checks, structural validation, a configuration panel for all four node types (including the Start form trigger), an unsaved-changes guard, tests.
3. **Validation:** the expression parser, completeness issues (including a Start form that has no published version yet), an issues panel with node highlighting, tests.
4. **Acceptance:**
   - this document's checklists and the API acceptance script extended to workflows
   - ARCHITECTURE notes on the graph format and expression grammar
   - PROGRESS/README updates
   - browser acceptance, then the merge to `main`

## Current implementation

Slice 1 (foundation) was merged into `dev/phase3_main` (#14). It includes:

- the `WorkflowDesigner` migration
- the Published-version guard
- `GET/POST /api/workflows` and `GET/PUT /api/workflows/{id}`
- the Studio **Workflows** list and navigation
- audit events

Slice 2 (canvas) was merged into `dev/phase3_main` (#15). It includes:

- `PUT /api/workflows/{id}/draft` with structural validation and revision checks
- the React Flow designer at `/workflows/{id}`, opened from the Workflows list
- node settings for all four types, including the Start form trigger
- an unsaved-changes guard
- refinements from the browser check:
  - dark, white-outlined canvas cursors, because the system grab hands were hard to see on the light canvas
  - a framed node settings panel with a type badge, description and footer

React Flow (`@xyflow/react` 12.12.0, MIT, pinned) has its own entry point, `@flowops/ui/workflow-designer`, so it is bundled only into Studio's designer page; Portal does not include it. Saving a graph changes only the draft version, so a rename in another tab never blocks a graph save, and vice versa.

Slice 3 (validation) was merged into `dev/phase3_main` (#16). All three slices are implemented; see [final Phase 3 acceptance](#final-phase-3-acceptance). It includes:

- `WorkflowExpression`: the condition grammar parser. It is documented in [Architecture](ARCHITECTURE.md#condition-expression-grammar-phase-3).
- `WorkflowGraphCompleteness`: completeness issues, each with a code and the node it concerns. They are returned with every workflow detail and draft save, and as `issueCount` in the workflow list.
- the designer's issues panel: clicking an issue selects and centres its node. Affected nodes are highlighted with an issue count. While there are unsaved edits, the panel shows the last saved check.
- an issue count, or **Complete**, for each workflow in the Workflows list

See [Progress](PROGRESS.md) for status.

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

## Internal test: workflow canvas

Restart the API and Studio after pulling this slice. It adds a package dependency and a package entry point.

1. Open **Workflows**, select a workflow and click **Open designer**. Draft v1 shows the Start → End starter graph.
2. Add one node of each type by dragging it from the palette, and add another by clicking. A second Start cannot be added while one exists.
3. Connect outputs to inputs. Condition shows `true`/`false` outputs and Approval shows `approved`/`rejected`. The designer refuses to:
   - connect a node to itself
   - connect into Start
   - use the same output twice
4. Select each node and edit its settings:
   - Start: Manual, or **When a form is submitted** with a form
   - Condition: expression
   - Approval: approver role and instructions
   - End: outcome

   The node card updates as you type.
5. Move nodes, delete a node and a connection with Delete/Backspace, then **Save draft**. Refresh: positions, connections, labels and settings persist.
6. Edit without saving and click **Back to Workflows**: the unsaved-changes warning appears, and staying keeps the edits.
7. Open the designer in two tabs. Save in one, then in the other: the second save is rejected with "This draft changed in another session. Reload it before saving."
8. Rename the workflow from the list in another tab while the designer is open. Saving the graph still succeeds.
9. A Portal account cannot open the designer or call the draft API. Another organization's workflow URL shows "This workflow is not in your active organization."

Automated checks cover:

- saving a baseline Expense Approval graph with an unconnected condition
- trimmed labels
- stale and missing draft revisions
- renames that do not block graph saves
- 19 structural rejection cases, with the draft unchanged
- forms from another organization
- Portal denial and `workflow.draft_saved` audit events

## Internal test: workflow validation

Restart the API after pulling this slice.

1. A new workflow (Start → End) shows **No issues: this draft is complete**. The Workflows list shows **Complete**.
2. Add a Condition and an Approval without connecting them, then save. The draft saves. The issues panel lists:
   - the unconnected outputs
   - "cannot be reached from Start"
   - "Enter an expression"

   Each affected node is highlighted with an issue count, and the list shows the number of issues.
3. Click an issue: its node is selected, centred, and its settings open on the right.
4. Enter condition expressions and save after each:
   - `request.amount >= 500 && request.currency == 'MYR'` is accepted.
   - `request.amount = 500`, `File.Delete('x')`, `items[0] == 1` and `'unterminated` each produce an expression issue naming the problem.
5. Connect the nodes into a loop, for example Approval `approved` → an earlier node. A loop issue appears. Remove the loop and the issue clears after saving.
6. Delete the End node and save: "Add at least one End node." Delete the Start node and save: "Add a Start node."
7. Set Start to **When a form is submitted** with a form that has never been published, then save. An issue asks you to publish the form. Publish the form in Forms, reload the designer, and the issue is gone.
8. Edit without saving: the panel notes that it shows the last saved check.
9. Build the baseline Expense Approval flow (Start → Manager approval → Approved/Rejected ends): no issues remain.

Automated checks cover:

- 7 valid expressions, and 17 rejected ones, including function calls, SQL/shell fragments and malformed syntax
- the nesting and size limits
- each completeness rule, with the node it reports
- issues and `issueCount` returned by the API

## Final Phase 3 acceptance

Each slice passed its own automated and browser checks before merging (#14, #15, #16). Before merging `dev/phase3_main` into `main`:

- [x] Backend tests (54), lint, typecheck and production builds pass on the merged code (`6e32790`, 2026-10-10).
- [x] One end-to-end browser pass in Studio over local HTTPS (user-reported, 2026-10-10):
  1. Create "Expense approval" in Workflows.
  2. In the designer, build Start (form trigger with a published form) → Manager approval → Approved/Rejected ends.
  3. Save. The panel shows no issues and the list shows **Complete**.
  4. Refresh, then rename the workflow. Everything persists.
- [x] Spot-check a regression of Phase 2 (passed 2026-10-10): create, publish and submit a form in Portal.
- [x] Review the docs and approve the PR from `dev/phase3_main` into `main` (merged 2026-10-10, #18).

Roadmap deliverables:

| Deliverable | Delivered by |
|---|---|
| Workflow list, create workflow | Studio Workflows list (#14) |
| Drag-and-drop canvas, nodes, connections, node configuration | React Flow designer (#15) |
| Draft saving, draft `WorkflowVersion` identity/state | Draft v1, `PUT …/draft` with revision checks, Published-version guard (#14, #15) |
| Basic workflow validation | Structural save rules (#15), completeness issues and the expression parser (#16) |
| Initial nodes: Start, Condition, Approval, End | All four, with type-specific settings (#15) |
