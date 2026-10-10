# Phase 5: Approval Inbox

## Branches

Phase 5 integration branch: `dev/phase5_main`, created from `main` (`69ae568`) after Phase 4 was merged. Create work branches from it and target their PRs back to it. Merge to `main` after Phase 5 implementation, internal testing and approval. Do not commit automatically.

Work branches:

1. `feat/phase5-approval-tasks`
2. `feat/phase5-portal-inbox`
3. `feat/phase5-requester-status`
4. `feat/phase5-acceptance`

## Scope

In scope ([Roadmap](ROADMAP.md#phase-5--approval-inbox)):

- an `ApprovalTask` created when an instance reaches an Approval node
- My Tasks and pending approvals in Portal
- Approve, Reject and Request changes, with comments
- approval history as durable business history, with matching audit events
- role-based assignment from the node's `assigneeRole`
- requester status tracking in Portal
- the baseline Expense Approval workflow end to end (single manager, no escalation)

Out of scope, by later phase:

| Not in Phase 5 | Phase |
|---|---|
| Amount-based escalation, switch/calculation nodes, timed waits | 6 |
| Version history and comparison UI | 7 |
| Notifications, reminders, SLA, overdue detection, background workers | 8 |
| Step debugger, retry from failed step | 9 |

## Decisions (approved 2026-10-10)

1. **Request changes:** a resubmit loop. The task moves to `ChangesRequested` and the instance stays Waiting at the same node. The requester edits their values in Portal and resubmits; the task reopens as Pending. No new node outputs and no graph change, so published workflows are unaffected.
2. **Assignment:** role claim. Any member of the instance's organization whose role satisfies the node's `assigneeRole` can see and decide the task. The first decision wins (task revision token). The requester can never decide their own task.
3. **Surface (revised 2026-10-10):** Portal accounts are always organization Members and Admin/Owner are Studio accounts, so one inbox cannot serve every role. The same task API is exposed on both surfaces: `/api/portal/tasks` (Portal Members, Member-role tasks) and `/api/tasks` (Studio Owner/Admin with the MFA policy, Owner/Admin-role tasks and any Member-role ones). Requester status stays in Portal.
4. **Resume:** a decision calls the existing `WorkflowEngine.ResumeAsync` with `approved` or `rejected` in the same transaction as the task update.
5. **Execution model:** still synchronous and in-process (PROJECT_RULES 18). No queue or worker.

## Data model

Migration `ApprovalInbox`:

```text
ApprovalTask              one approval request raised by one Approval node activation
- Id, OrganizationId, WorkflowInstanceId, WorkflowVersionId, NodeId
- AssigneeRole, Instructions (snapshot from the pinned version)
- Status: Pending | Approved | Rejected | ChangesRequested | Cancelled
- RequesterUserId, DecidedByUserId, DecidedAt, Revision (concurrency token)
- CorrelationId, CreatedAt

ApprovalTaskEvent         append-only history for a task
- Id, TaskId, OrganizationId, Kind: Created | Approved | Rejected | ChangesRequested | Resubmitted | Commented
- ActorUserId, Comment, CreatedAt
```

- The task snapshots assignee role and instructions; later drafts never change it.
- At most one open task per (instance, node); a unique index enforces it.
- `ApprovalTaskEvent` rows are immutable, like audit events.

## Behavior

- **Create:** reaching an Approval node writes the task and its Created event in the same save as the Waiting instance.
- **List:** `GET /api/tasks` returns the caller's pending tasks (organization-scoped, role-filtered); `GET /api/tasks/{id}` returns detail, the request values and history.
- **Decide:** `POST /api/tasks/{id}/decision` with `{ decision, comment, revision }`. Approve and Reject resume the instance; Request changes requires a comment. A stale revision returns 409.
- **Resubmit:** `POST /api/tasks/{id}/resubmit` (and `/api/portal/tasks/{id}/resubmit`) by the requester only, with `{ values, comment, revision }` where `values` is keyed by form field key. Values are validated against the form version of the original submission; files carry over unchanged and cannot be replaced. Only runs started from a form submission can be resubmitted. The run request values are replaced, the task reopens as Pending and the instance stays Waiting.
- **Comments:** `POST /api/tasks/{id}/comments` for assignees and the requester.
- **Requester status:** `GET /api/portal/my-requests` and `/{instanceId}` (Portal only) show instance status, a plain-language headline, current step, request values and approval history for the requester's own runs; a link opens the open task for resubmitting.
- **Authorization:** server-side tenant isolation on every endpoint; requester self-approval refused (403); Portal sessions only reach task endpoints for their active organization.
- **Audit:** `approval.task_created`, `approval.approved`, `approval.rejected`, `approval.changes_requested`, `approval.resubmitted`, `approval.commented`.

## Delivery sequence

1. **Approval tasks:** `ApprovalInbox` migration, task creation in the engine, decision/resume service, API, audit events, tests.
2. **Inbox UIs:** Portal My Tasks and Studio Tasks (task detail, decision form, comments), plus task history on the Studio run detail.
3. **Requester status and resubmit:** My Requests, resubmit loop, tests.
4. **Acceptance:** baseline Expense Approval end to end, docs/ARCHITECTURE updates, PROGRESS/README, browser pass, then the merge to `main`.

## Acceptance checklist

Automated checks (62 backend tests, Debug build, 2026-10-10) cover every item. The browser pass is still to be run.

- [x] Expense form submission starts a run that waits on a Manager-role task
- [x] Member with the role sees the task; other role and other organization do not
- [x] Approve and Reject complete the run with the right outcome
- [x] Requester cannot decide their own task
- [x] Two concurrent decisions: one wins, one gets 409
- [x] Request changes, resubmit and a second decision work; history shows every step
- [x] Audit events exist for each action; the run stays pinned to its version after republishing

## Internal test: baseline Expense Approval

1. In Studio (Owner), publish an Expense form with an `amount` field and a workflow Start (form trigger) → Approval (Admin) → End Approved / End Rejected.
2. Submit the form from a Portal account (Member). **My Requests** shows "Waiting for Admin approval".
3. In Studio **Tasks** (Owner/Admin), open the task: it shows the request values and history. Approve it; the run completes as Approved and My Requests shows the outcome.
4. Repeat and Reject; repeat and Request changes with a comment. The requester sees "Changes requested", edits the amount, resubmits, and the task is Pending again; approve it.
5. The requester cannot decide their own task; another organization sees nothing.
6. Republish the workflow, then decide an older waiting task: the run keeps its original version.
