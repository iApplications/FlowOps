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

## Decisions (proposed 2026-10-10, pending approval of this plan)

1. **Request changes:** a resubmit loop. The task moves to `ChangesRequested` and the instance stays Waiting at the same node. The requester edits their values in Portal and resubmits; the task reopens as Pending. No new node outputs and no graph change, so published workflows are unaffected.
2. **Assignment:** role claim. Any member of the instance's organization whose role satisfies the node's `assigneeRole` can see and decide the task. The first decision wins (task revision token). The requester can never decide their own task.
3. **Surface:** Portal hosts My Tasks, decisions and requester status. Studio run detail shows the task and its history read-only.
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
- **Resubmit:** `POST /api/tasks/{id}/resubmit` by the requester only, with revised `request.*` values validated against the pinned form version; the task reopens.
- **Comments:** `POST /api/tasks/{id}/comments` for assignees and the requester.
- **Requester status:** `GET /api/my-requests` and detail show instance status, current step and approval history for the requester's own runs.
- **Authorization:** server-side tenant isolation on every endpoint; requester self-approval refused (403); Portal sessions only reach task endpoints for their active organization.
- **Audit:** `approval.task_created`, `approval.approved`, `approval.rejected`, `approval.changes_requested`, `approval.resubmitted`, `approval.commented`.

## Delivery sequence

1. **Approval tasks:** `ApprovalInbox` migration, task creation in the engine, decision/resume service, API, audit events, tests.
2. **Portal inbox:** My Tasks, task detail with decision form and comments, Studio read-only task history.
3. **Requester status and resubmit:** My Requests, resubmit loop, tests.
4. **Acceptance:** baseline Expense Approval end to end, docs/ARCHITECTURE updates, PROGRESS/README, browser pass, then the merge to `main`.

## Acceptance checklist

- [ ] Expense form submission starts a run that waits on a Manager-role task
- [ ] Member with the role sees the task; other role and other organization do not
- [ ] Approve and Reject complete the run with the right outcome
- [ ] Requester cannot decide their own task
- [ ] Two concurrent decisions: one wins, one gets 409
- [ ] Request changes, resubmit and a second decision work; history shows every step
- [ ] Audit events exist for each action; the run stays pinned to its version after republishing
