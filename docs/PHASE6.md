# Phase 6: Conditional Routing & Advanced Logic

## Branches

Phase 6 integration branch: `dev/phase6_main`, created from `main` (`e24296c`) after Phase 5 was merged. Create work branches from it and target their PRs back to it. Merge to `main` after Phase 6 implementation, internal testing and approval. Do not commit automatically.

Work branches:

1. `feat/phase6-roles`
2. `feat/phase6-variables-calculation`
3. `feat/phase6-switch-routing`
4. `feat/phase6-wait-state`
5. `feat/phase6-acceptance`

## Scope

In scope ([Roadmap](ROADMAP.md#phase-6--conditional-routing--advanced-logic)):

- If / Else and multiple conditions (already delivered by the Phase 3 Condition node and `&&` / `||`; Phase 6 adds typed validation)
- Switch node (multi-way routing)
- Calculation node
- Data mapping
- Workflow variables with declared types
- Wait state
- Parallel branch research/prototype (document only, no engine change)
- Organization role management, so Finance and Director are real roles
- Expense Approval upgraded with amount-based escalation

Out of scope: notifications, reminders, SLA (Phase 8); version history UI (Phase 7); step debugger and retry (Phase 9); any user-supplied code, functions or SQL (PROJECT_RULES 14).

## Decisions (approved 2026-10-10)

1. **Variables:** a workflow graph declares variables `{ name, type: Number | Text | Boolean, initialValue? }`. They live under `vars.<name>` in the run context next to `request.*` and `submission.*`. Names are unique, `[A-Za-z_][A-Za-z0-9_]*`, max 20. Declarations are part of the published snapshot, so versions stay immutable.
2. **Calculation node:** an ordered list of assignments `{ variable, expression }`, max 10. Expressions extend the existing grammar with `+ - * /` and parentheses over numbers and paths; still no functions, indexing or statements. A result must match the variable's declared type, otherwise the run fails with a structured error (divide by zero, missing value, type mismatch). Output handle: `next`.
3. **Data mapping:** a Calculation assignment such as `vars.amountMyr = request.amount` is the mapping mechanism. No separate mapping node.
4. **Switch node:** up to 8 ordered cases, each `{ id, label, expression }`, plus a mandatory `default`. The first true case wins. Output handles are the case IDs and `default`, so `OutputHandles` becomes config-aware. A case with an error fails the run like a Condition does.
5. **Typed validation:** at save and publish, expressions are checked against declared variable types where the type is known (`vars.*`, form field keys from the Start form). Unknown types are not rejected, because runtime still validates.
6. **Wait node:** waits for a fixed duration (1 minute to 30 days) and then continues from `next`. The instance parks as Waiting with `ResumeAt` in our own table, which stays the source of truth. Hangfire (`Hangfire.PostgreSql`, dashboard restricted to Owner) runs one recurring job every minute that resumes due instances through `WorkflowEngine.ResumeAsync`; the run's `Revision` check prevents double resume. No per-wait Hangfire jobs. A manual "Resume due runs" action remains for testing. PROJECT_RULES 18 is amended to allow this single scheduled job; all workflow logic still runs inside `WorkflowEngine`.
7. **Parallel research:** `docs/PARALLEL_RESEARCH.md` compares fork/join models against the engine's single-cursor design and recommends an approach for a later phase. No code.
8. **Expense Approval:** published as new versions. Amount below 500 goes to Manager; 500 to 5000 goes Manager then Finance; above 5000 goes Manager, Finance, then Director, using the roles from decision 9.
9. **Roles:** Owner is the master role and the only one who manages roles. Each user has exactly one role per organization. Owner, Admin and Member are built-in and cannot be deleted; the Owner can add custom roles (for example Manager, Finance, Director), assign them to accounts, rename or deactivate them. Approval nodes choose from the organization's role list and tasks snapshot the role. A role used by a published workflow cannot be hard-deleted. Role changes are audited, and the requester can still never decide their own task.

## Slice 2: variables and Calculation

- **Declaration:** the draft graph carries `variables: [{ name, type, initialValue }]` (max 20; names `[A-Za-z_][A-Za-z0-9_]{0,31}`, unique; types Number, Text, Boolean). A bad declaration rejects the save. Existing graphs have none.
- **Run context:** a run starts with `vars` holding each variable's starting value (Number 0, Text empty, Boolean false unless set). Conditions and Calculations read them as `vars.<name>`.
- **Calculation node:** one `next` output and up to 10 ordered assignments `{ variable, expression }`. Grammar: `+ - * /`, brackets, numbers, text, true/false and paths; a lone value is copied (data mapping). Arithmetic is exact decimal. Later assignments see earlier results.
- **Run-time errors** fail the run with a structured code: `division_by_zero`, `type_mismatch` (including a result that does not match the variable's type), `variable_missing`, `variable_undeclared`, `calculation_overflow`, `expression_invalid`.
- **Completeness issues** (block publishing, not saving): `variable_undeclared` (assigning to or reading an undeclared `vars.*`), `type_mismatch` (statically known result type differs from the variable's), `calculation_empty`, `expression_invalid`. Values only known at run time (`request.*`) are not type-checked until the run.
- **Designer:** Variables panel above the canvas; Calculation node with an assignments editor.

## Data model

Migration `OrganizationRoles` adds the role table and the membership role reference (slice 1). Migration `WorkflowLogic` adds `WorkflowInstance.ResumeAt`, indexed with status (slice 4). Variables, Calculation and Switch live in the versioned graph JSON, so they need no table changes.

## Delivery sequence

1. **Roles:** role table and membership reference, Owner-only role API and Studio page, Approval node role picker, tests.
2. **Variables and Calculation:** graph variable declarations, arithmetic grammar, Calculation handler, designer panel, typed validation, tests.
3. **Switch routing:** config-aware output handles, Switch handler, designer case editor, completeness checks, tests.
4. **Wait state:** Wait node, `ResumeAt` migration, Hangfire recurring resume job, manual resume action, run detail UI, tests.
5. **Acceptance:** upgraded Expense Approval end to end (three amount bands), parallel research doc, ARCHITECTURE/PROGRESS/README updates, browser pass, then merge to `main`.
