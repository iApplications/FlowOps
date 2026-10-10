# FlowOps Architecture

## 1. Architecture Goal

FlowOps should support many workflow scenarios through a reusable platform instead of implementing separate hard-coded systems.

The initial architecture should remain simple enough for one developer to maintain.

**Recommended approach: Modular Monolith.**

Do not introduce microservices unless there is a proven technical need.

---

## 2. High-Level Architecture

FlowOps has two primary web experiences. Both use the same backend platform, authorization model, workflow engine, and organization data.

```text
┌──────────────────────────────┐       ┌──────────────────────────────┐
│      FlowOps Studio          │       │       FlowOps Portal         │
│         Next.js              │       │          Next.js             │
│                              │       │                              │
│ Workflow / Form Builder      │       │ Forms / Requests             │
│ Documents                    │       │ Tasks / Approvals            │
│ Reports / Audit              │       │ Status Tracking              │
│ Integrations                 │       │ Documents / Notifications    │
│ Developer Tools / Debugger   │       │ Guest/Public Experiences     │
└──────────────┬───────────────┘       └──────────────┬───────────────┘
               │ HTTPS / REST                         │
               └──────────────────┬───────────────────┘
                                  ▼
                    ┌──────────────────────────────┐
                    │      ASP.NET Core API        │
                    │                              │
                    │ Auth / Organizations         │
                    │ Forms / Workflows            │
                    │ Workflow Runtime             │
                    │ Approvals / Documents        │
                    │ Notifications / Integrations │
                    │ Audit / Observability        │
                    │ AI                           │
                    └───────┬────────┬─────────────┘
                            │        │
                            │        └──────────────► Redis
                            │
                            ├───────────────────────► PostgreSQL
                            │
                            └───────────────────────► RabbitMQ
                                                          │
                                                          ▼
                                                  Background Workers
```

### Surface Responsibilities

**FlowOps Studio** is the administrative and builder experience. It owns configuration-heavy capabilities such as workflow design, integrations, reporting, audit review, and debugging.

**FlowOps Portal** is the participant experience. It should expose only the forms, tasks, approvals, documents, and status information the current user or guest is authorized to access.

The two surfaces must not implement separate business rules. Authoritative validation, permissions, workflow transitions, and data access remain in the shared backend.

---

## 3. Recommended Solution Structure

```text
flowops/
│
├── apps/
│   ├── studio-web/
│   └── portal-web/
│
├── backend/
│   ├── FlowOps.Api/
│   ├── FlowOps.Domain/
│   ├── FlowOps.Application/
│   ├── FlowOps.Infrastructure/
│   ├── FlowOps.Workflows/
│   ├── FlowOps.Documents/
│   ├── FlowOps.Notifications/
│   ├── FlowOps.Observability/
│   └── FlowOps.AI/
│
├── workers/
│   └── FlowOps.Worker/
│
├── packages/
│   ├── ui/
│   ├── types/
│   └── api-client/
│
├── docs/
│
├── docker-compose.yml
└── README.md
```

The exact repository structure can be adjusted once implementation begins.

---

## 4. Major Domain Concepts

### Organization

Represents a workspace/company using FlowOps.

### User

A person who belongs to one or more organizations.

### Role

Controls what users are allowed to do.

### FormDefinition

Describes a configurable form.

### FormSubmission

Stores submitted form data.

### WorkflowDefinition

Logical workflow identity.

Example:

```text
Expense Approval
```

### WorkflowVersion

A versioned snapshot of a workflow definition. A version may be edited while it is in **Draft** state. Publishing freezes that exact snapshot permanently; a **Published** version is immutable and receives a stable version identity used by runtime instances. Further changes are made in a new draft version.

Only published versions may start workflow instances.

Example:

```text
Expense Approval v1  Published / immutable
Expense Approval v2  Draft / editable
```

### WorkflowNode

A step in the workflow.

Examples:

- Trigger
- Condition
- Approval
- Calculation
- Document generation
- Notification

### WorkflowConnection

Defines how nodes are connected.

### WorkflowInstance

The durable business-process instance created from one exact published `WorkflowVersion`. It owns the long-lived process state, workflow context/data, current status, waiting tasks, and final outcome.

Examples:

```text
Expense Claim EXP-000123
Employee Onboarding ONB-000045
```

A `WorkflowInstance` may remain alive for minutes, days, or weeks while waiting for approvals, timers, webhooks, or other external events. It is created once and remains pinned to the same `WorkflowVersionId` for its entire lifetime.

### WorkflowExecution

One runtime activation of a `WorkflowInstance`. An execution begins when the instance starts or resumes and processes nodes until it completes, fails, is cancelled, or reaches a durable wait state.

One instance may therefore have **multiple executions**. For example, the first execution may stop at an Approval node; approving the task later starts another execution for the same instance. Technical retries inside the same activation are represented by execution-step attempt information rather than creating a new business instance.

### WorkflowExecutionStep

Represents execution of one specific node within a `WorkflowExecution`. It stores the node input, output, timing, attempt count, status, and error information required for debugging.

The intended relationship is:

```text
WorkflowDefinition
      1
      │
      N
WorkflowVersion
      1
      │
      N
WorkflowInstance
      1
      │
      N
WorkflowExecution
      1
      │
      N
WorkflowExecutionStep
```

`WorkflowInstance` and `WorkflowExecution` must not be used as synonyms in code, APIs, database names, or documentation.

### ApprovalTask

Represents one approval request raised by one Approval node activation (Phase 5). The task snapshots the node's assignee role and instructions from the pinned version, is visible to organization members whose role satisfies that role, and is decided once (Pending → Approved, Rejected or ChangesRequested). Every step is kept in the append-only `ApprovalTaskEvent` history. See [Approval tasks (Phase 5)](#approval-tasks-phase-5).

### AuditEvent

Immutable, append-only record of an important actor, security, or business action. Basic durable `AuditEvent` persistence is part of the Phase 1 platform foundation so later capabilities can emit audit records from the moment they are introduced. Phase 10 adds richer audit review/reporting; it does not introduce audit persistence for the first time.

### DocumentTemplate

Reusable document definition.

### GeneratedDocument

Output generated from a template and workflow data.

---

## 5. Authentication & Authorization Architecture

FlowOps must use standards-based authentication and centralized server-side authorization. Authentication logic must not be reimplemented independently in Studio and Portal.

### Browser Authentication

Initial browser authentication should use ASP.NET Core Identity with secure server-managed cookie sessions.

```text
FlowOps Studio / Portal
        │
        │ HTTPS
        ▼
ASP.NET Core Identity
        │
        ├── Password authentication
        ├── TOTP MFA + recovery codes
        ├── Passkeys / WebAuthn (planned)
        └── OIDC / enterprise SSO (planned)
        │
        ▼
Secure authentication session
```

Browser authentication cookies should be configured with secure defaults such as:

```text
HttpOnly = true
Secure   = true in production
SameSite = appropriate for the deployment model
```

State-changing browser requests must use CSRF/antiforgery protection. Authentication or refresh credentials must not be stored in browser `localStorage`.

### MFA and Privileged Access

Privileged Studio roles such as Owner and Admin should be subject to stronger authentication requirements. The platform should support organization security policies and require MFA for privileged users.

Sensitive operations should support step-up reauthentication, for example:

- Changing another user's roles or security settings
- Regenerating API credentials
- Disabling MFA
- Changing integration secrets
- Destructive organization actions

### Authorization Model

Authentication and authorization are separate concerns.

```text
Authenticated?
      ↓
Member of active organization?
      ↓
Has required permission/policy?
      ↓
Has access to target resource?
      ↓
ALLOW
```

FlowOps should combine coarse roles with explicit permissions/policies. Example permissions:

```text
workflow.read
workflow.create
workflow.publish
workflow.debug
request.approve
audit.read
integration.manage
user.manage
```

The frontend may hide unavailable actions for usability, but the backend is authoritative and must independently enforce every protected operation.

### Tenant Isolation

Most business records must carry an `OrganizationId`. Every tenant-scoped query must be filtered by the active organization before resource-level authorization is evaluated. A valid resource ID must never be enough to cross tenant boundaries.

### Guest / Public Access

Selected Portal scenarios may allow users without FlowOps accounts, such as viewing a restaurant receipt or submitting a public application form. These flows should use unguessable, scoped, revocable access tokens rather than sequential public IDs.

Guest tokens should be restricted by:

- Resource or workflow scope
- Allowed action
- Expiry where appropriate
- Revocation status
- Optional verification for sensitive content

Sensitive documents may require login or additional email verification instead of anonymous token access.

### API Authentication

Machine-to-machine integrations must use separate scoped credentials and must not impersonate normal browser sessions. Initial API credentials should be generated securely, shown once, and stored only as a one-way hash or equivalent verifier.

Example scopes:

```text
orders:create
payments:create
workflow:start
workflow:read
```

OAuth/OIDC client credentials can be added later for larger integrations.

### API Security Pipeline

The API itself is a security boundary. Requests should pass through centralized protection before application/domain handlers execute.

```text
Internet / Browser / Integration
            │
           HTTPS
            │
      Rate Limiting
            │
      Authentication
            │
      Authorization
            │
      Tenant Isolation
            │
      Input Validation
            │
       FlowOps API
            │
   Application / Domain
```

Cross-cutting API protections include:

- Deny-by-default authorization
- Organization/resource scoping
- Restrictive CORS for authenticated browser APIs
- CSRF/antiforgery for cookie-authenticated mutations
- Request/body/upload size limits
- Safe production error responses
- Endpoint-specific rate limiting
- Idempotency for retryable side-effecting operations
- Signed/timestamped webhook verification and replay detection
- Outbound HTTP SSRF controls
- Secret redaction in logs and Developer Tools
- Correlation IDs and security/audit events

The API must remain secure when called directly without either frontend.

### Session Security

The authentication subsystem should support:

- Session expiration
- Logout
- Server-side session/security-stamp revocation where applicable
- Password/MFA reset invalidation
- Account lockout and login rate limiting
- Security-sensitive audit events

Detailed requirements are maintained in [`SECURITY.md`](SECURITY.md).

---

## 6. Workflow Versioning

Published workflow versions should be immutable.

Example:

```text
Expense Approval v1
Employee → Manager → Finance

Expense Approval v2
Employee → Manager → Finance → Director if > RM10,000
```

A workflow instance created using v1 must continue using v1 even after v2 is published.

Suggested model:

```text
WorkflowDefinition
       │
       ├── WorkflowVersion 1 [Published, immutable]
       ├── WorkflowVersion 2 [Published, immutable]
       └── WorkflowVersion 3 [Draft, editable]

WorkflowInstance EXP-000123
       ↓ pinned for life
WorkflowVersion 2
       │
       ├── WorkflowExecution 1  (start → waits for approval)
       └── WorkflowExecution 2  (approval received → completes)
```

Version identity is a **Phase 4 runtime requirement** because publishing and instance creation begin in Phase 4. Phase 7 adds richer version-management features and UI; it does not introduce version identity for the first time.

Never silently migrate an in-progress workflow to another version.

---

## 7. Workflow Runtime

Basic execution model:

```text
Trigger
   ↓
Resolve exact Published WorkflowVersion
   ↓
Create WorkflowInstance pinned to WorkflowVersionId
   ↓
Create WorkflowExecution
   ↓
Find/resume current node
   ↓
Execute node + persist WorkflowExecutionStep
   ↓
Continue until complete/failure/durable wait
   ↓
If waiting: keep WorkflowInstance alive
   ↓
Approval / timer / webhook / resume event
   ↓
Create next WorkflowExecution for same WorkflowInstance
```

Every node execution should have a status such as:

```text
Pending
Running
Waiting
Completed
Failed
Cancelled
```

Possible workflow instance statuses:

```text
Running
Waiting
Completed
Failed
Cancelled
```

### Execution Tracing

Execution tracing is part of the runtime contract, not an afterthought. Each run should persist enough structured information to reconstruct what happened.

Conceptual model:

```text
WorkflowInstance
├── WorkflowVersionId
├── Status
├── Context / business data
├── StartedAt
├── CompletedAt
└── Executions[]
    └── WorkflowExecution
        ├── InstanceId
        ├── Status
        ├── ResumeReason / Trigger
        ├── TriggeredBy
        ├── CorrelationId
        ├── StartedAt
        ├── CompletedAt
        └── Steps[]
            ├── NodeId
            ├── Status
            ├── Input
            ├── Output
            ├── Attempt
            ├── DurationMs
            ├── ErrorCode
            └── ErrorMessage
```

Execution inputs and outputs may require redaction before persistence or display. Secrets must never be exposed through the debugger.

### Runtime model (Phase 4)

Phase 4 implements this model in `WorkflowEngine` (`backend/FlowOps.Api/Workflows`):

- **Synchronous, in-process.** A run executes during the request that triggers it: a manual `POST /api/workflows/{id}/runs` or a saved Portal form submission. There is no queue or worker until Phase 8.
- **Pinned version.** A trigger resolves the latest Published `WorkflowVersion` and creates a `WorkflowInstance` pinned to its `WorkflowVersionId`. The engine executes that version's frozen `GraphJson`, so later publishing never changes a running or waiting instance.
- **Activations.** Each start or resume is one `WorkflowExecution` (Reason `Start` or `Resume`, increasing `Sequence`). The loop runs one `IWorkflowNodeHandler` per node, records a `WorkflowExecutionStep` with input, output, status and duration, and follows the connection for the output the handler chose. It stops at an End node (instance Completed with the End's outcome), a durable wait (Approval: instance Waiting with `WaitingNodeId`), a failure, or after 200 steps.
- **One write per activation.** The instance state, the execution, all its steps and the audit events (`workflow.instance_started`, `…_completed`, `…_failed`) are saved together. The instance revision is a concurrency token, so two resumes cannot both continue.
- **Resume.** `ResumeAsync(instance, nodeId, output)` is internal in Phase 4 (Phase 5 approval tasks call it). It only accepts the waiting node and one of its outputs (`approved`/`rejected`).
- **Failures.** A handler failure sets `ErrorCode`/`ErrorMessage` on the step, the execution and the instance, which becomes Failed. There are no automatic retries; a failed form-triggered run does not fail the submission.
- **Immutability.** Finished executions and steps cannot be modified, and an instance never changes its version or is deleted (DbContext guard).
- **Idempotency.** Manual runs accept an `Idempotency-Key` (unique per workflow and user). Form triggers are unique per (workflow version, form submission), so a retried submission starts only missing runs.
- **Correlation.** A well-formed `X-Correlation-ID` request header is used, otherwise one is generated; it is echoed on the response and stored on the instance, executions and steps.

Instance statuses are Running, Waiting, Completed, Failed and Cancelled (Cancelled is reserved; nothing cancels a run yet). Executions and steps use Running, Waiting, Completed and Failed.

### Approval tasks (Phase 5)

Phase 5 puts people in front of the durable wait (`ApprovalEndpoints`, `MyRequestEndpoints`):

- **Creation.** Reaching an Approval node writes an `ApprovalTask` and its `Created` event in the same save as the Waiting instance. A unique index allows one open task per (instance, node).
- **Assignment.** Role claim: Owner satisfies every role, Admin satisfies Admin and Member, Member satisfies Member. Visibility is the caller's member role in the active organization; the requester never sees or decides their own task (403 `not_assignee`).
- **Two surfaces, one API.** `/api/portal/tasks` serves Portal Members (Member-role tasks); `/api/tasks` serves Studio Owner/Admin under the MFA policy. Requester status (`/api/portal/my-requests`) is Portal only and read-only.
- **Decision.** `POST …/decision` with `approve`, `reject` or `request_changes` and the task `revision`. Approve and Reject call `WorkflowEngine.ResumeAsync` in the same transaction as the task update; a stale revision is 409, so concurrent decisions have exactly one winner. Request changes requires a comment and leaves the instance Waiting.
- **Resubmit.** The requester edits the original form values; they are validated against the pinned form version, the request values are replaced, and the task reopens as Pending at the same node. No graph change, so published versions are unaffected.
- **History and audit.** Created, Approved, Rejected, ChangesRequested, Resubmitted and Commented are immutable `ApprovalTaskEvent` rows with matching `approval.*` audit events.
- **Still synchronous.** Decisions resume the run in the request; there is no queue, notification or SLA until Phase 8.

---

## 8. Node Interface

A generic node execution contract can eventually look conceptually like:

```csharp
public interface IWorkflowNodeHandler
{
    string NodeType { get; }

    Task<NodeExecutionResult> ExecuteAsync(
        WorkflowExecutionContext context,
        CancellationToken cancellationToken);
}
```

Specific node handlers can then implement:

```text
ConditionNodeHandler
ApprovalNodeHandler
CalculationNodeHandler
GenerateDocumentNodeHandler
NotificationNodeHandler
WebhookNodeHandler
```

This allows new capabilities to be added without rewriting the workflow engine.

---

## 9. Workflow Variables

Workflow data should be stored in a structured context.

Example:

```json
{
  "request": {
    "amount": 1200,
    "currency": "MYR",
    "employeeId": "123"
  },
  "approval": {
    "managerApproved": true
  }
}
```

Nodes read from and write to this context.

Avoid allowing arbitrary code execution in workflow expressions.

Use a controlled expression system instead.

Example:

```text
request.amount >= 500
```

### Condition expression grammar (Phase 3)

Condition nodes store an expression that is parsed on the server by `WorkflowExpression`, a purpose-built tokenizer and recursive-descent parser. It is never compiled or evaluated as code.

```text
expression := or
or         := and ( "||" and )*
and        := unary ( "&&" unary )*
unary      := "!" unary | comparison
comparison := operand ( ( "==" | "!=" | "<" | "<=" | ">" | ">=" ) operand )?
operand    := number | "-" number | string | "true" | "false" | path | "(" expression ")"
path       := identifier ( "." identifier )*          identifier := [A-Za-z_][A-Za-z0-9_]*
string     := '...' or "..." with only \\ \' \" escapes and no control characters
```

The parser rejects everything else, including:

- function calls such as `File.Delete(...)`
- indexing (`items[0]`)
- assignment (`=`)
- single `&`/`|`
- chained comparisons (`1 < a < 3`)
- statement separators and shell characters (`;`, `` ` ``, `$`)

Expressions are limited to 500 characters, 200 tokens and 32 levels of nesting.

### Expression evaluation (Phase 4)

`WorkflowExpression.Evaluate` walks the parsed tree against the instance context; nothing is compiled or executed as code.

- **Values:** numbers (decimal), text, `true`/`false` and empty. A path such as `request.amount` reads the context; JSON `null` and unanswered form fields are empty values.
- **Comparisons:** `==` and `!=` work on any two values of the same type, and anything compared with empty. `<`, `<=`, `>`, `>=` need two numbers or two texts (ordinal).
- **Logic:** `!`, `&&` and `||` need true/false; `&&` and `||` short-circuit, so `request.urgent || request.amount > 1` does not read `amount` when `urgent` is true.
- **Errors fail the run:** `variable_missing` for a path that is not in the context, and `type_mismatch` for mixed types, a path that holds a group of values (object or list), or a final result that is not true/false.

Run variables:

| Trigger | Variables |
|---|---|
| Manual | `request.*` (the JSON sent with the run), `trigger.type`, `trigger.startedAt` |
| Form submitted | `request.<fieldKey>` for every field of the submitted form version, `submission.id`, `submission.formId`, `submission.formVersion`, `submission.submittedAt`, `trigger.type` |

Form field keys (`amount`, `isUrgent`) are generated from labels and editable in the form designer. Phase 6 adds declared variable types and the Calculation node (delivered), and the Switch node.

### Workflow graph format (Phase 3)

Each `WorkflowVersion` stores its graph as one JSON document (`GraphJson`), so a Published version freezes nodes, connections and layout together:

```text
WorkflowGraph      { schemaVersion: 1, nodes: WorkflowNode[], connections: WorkflowConnection[] }
WorkflowNode       { id, type: Start|Condition|Approval|End, label, position { x, y }, config }
WorkflowNodeConfig { trigger, formId }            Start: Manual | FormSubmitted + a form of the organization
                   { expression }                 Condition
                   { assigneeRole, instructions } Approval: Owner | Admin | Member
                   { outcome }                    End
WorkflowConnection { id, source, sourceHandle, target }
                   sourceHandle: Start → next; Condition → true/false; Approval → approved/rejected; End has none
```

Validation has two levels:

- **Structural rules reject a save.** They cover limits, IDs, per-type settings, valid outputs, self-connections, and at most one connection per output.
- **Completeness issues are reported but allowed in a draft.** They cover exactly one Start, at least one End, connected outputs, reachability from Start, a path to an End, cycles, expression syntax and published Start forms.

Phase 4 publishing requires zero completeness issues and a graph that differs from the latest Published version.

---

## 10. Document Engine

Documents should be generated from templates and structured workflow data.

Conceptual flow:

```text
Template
   +
Workflow Data
   ↓
Template Renderer
   ↓
HTML
   ↓
PDF
```

Example template:

```html
<h1>{{restaurant.name}}</h1>
<p>Receipt: {{receipt.number}}</p>
<p>Total: RM {{receipt.total}}</p>
```

Document generation should remain generic so the same system can create receipts, invoices, quotations, service reports, and certificates.

---

## 11. AI Architecture

AI must be behind an abstraction.

```text
IAIProvider
      │
      ├── OllamaProvider
      ├── OpenAIProvider
      ├── GeminiProvider
      └── ClaudeProvider
```

Initial development provider:

```text
OllamaProvider
```

Potential AI operations:

```text
GenerateWorkflow
ExplainWorkflow
ExtractDocumentFields
ClassifyRequest
SummarizeExecution
SuggestImprovements
```

AI-generated workflows should be saved as **drafts** until reviewed by the user.

---

## 12. Notification Architecture

```text
INotificationProvider
       │
       ├── InAppNotificationProvider
       ├── LocalEmailProvider
       ├── SmtpProvider
       ├── SlackProvider
       └── TeamsProvider
```

Only the first two are required for initial development.

---

## 13. File Storage Architecture

```text
IFileStorage
      │
      ├── LocalFileStorage
      ├── S3FileStorage
      └── AzureBlobFileStorage
```

Initial implementation:

```text
LocalFileStorage
```

The database should store file metadata rather than relying on physical paths throughout business logic.

---

## 14. Background Processing

Background processing will be required for:

- Email delivery
- Notifications
- SLA checks
- Scheduled workflows
- Webhook retries
- Document generation
- Long-running integrations

Initial implementation can use ASP.NET Core background services.

RabbitMQ can be introduced when asynchronous processing becomes necessary.

Do not introduce messaging infrastructure before the application actually needs it.

---

## 15. Audit Requirements

FlowOps must have a basic durable, append-only audit recording service from Phase 1. Important changes in every later phase must create audit events as the relevant feature is introduced. The richer audit explorer, timelines, dashboards, and reports are Phase 10 concerns; durable audit recording is not.

Important changes must create audit events.

Examples:

```text
WorkflowCreated
WorkflowPublished
WorkflowStarted
ApprovalRequested
ApprovalApproved
ApprovalRejected
DocumentGenerated
WebhookSent
WorkflowCompleted
```

Audit events should include:

- Organization
- User / actor
- Timestamp
- Entity type
- Entity ID
- Event type
- Relevant metadata

Audit history should not be editable by normal users.

---

## 16. Security Principles

- Validate authorization on the server.
- Never trust role information from the browser.
- Scope organization data by organization ID.
- Prevent cross-organization data access.
- Validate uploaded files.
- Do not allow arbitrary server-side code execution from workflow definitions.
- Encrypt secrets used by integrations.
- Avoid placing secrets in source control.
- Record sensitive administrative changes in audit logs.

---

## 17. Multi-Tenancy

Initial implementation should use shared database multi-tenancy.

Most business tables should contain:

```text
OrganizationId
```

Queries must always be scoped to the active organization.

A separate database per tenant is unnecessary for the initial project.

---

## 18. Observability & Developer Tools

FlowOps Studio should provide a dedicated Developer Tools area for administrators and authorized builders.

### Execution Debugger

For each workflow execution, authorized users should be able to inspect:

- Exact workflow version
- Trigger information
- Step-by-step node trace
- Inputs and outputs
- Workflow variables
- Condition expressions and evaluated values
- Duration per node
- Retry attempts
- Structured errors
- Correlation ID

### Dry Run / Sandbox

A workflow should be testable with sample data before publishing or invoking real side effects.

Potentially destructive or external actions should support simulation where practical:

```text
Send Email       → simulated
Call Webhook     → simulated
Update Record    → isolated/test context
Generate Document→ test output
```

Dry run must be clearly distinguishable from a real execution.

### Node Testing

Individual nodes should eventually be testable with supplied sample inputs without running the entire workflow.

### API & Webhook Inspector

Authorized users should be able to inspect request/response metadata, payloads, status codes, duration, and retry history for integrations.

Webhook events should support replay when replaying is safe and explicitly requested.

### Retry From Failed Step

The engine should distinguish retry-safe operations from side-effecting operations. A failed execution may be resumed from a failed step only when doing so cannot silently duplicate completed business actions. Idempotency keys should be used where appropriate.

### Audit vs Debug

Audit and debug data are related but must remain separate concepts:

- **Audit data** answers who did what and when and should be durable/immutable according to policy.
- **Debug telemetry** explains system behavior, failures, timings, retries, and integration details and may have configurable retention.

### Secret Redaction

The following must never be shown in plaintext debug output:

- Passwords
- API keys
- Access tokens
- Refresh tokens
- Authorization headers
- Connection strings
- Private signing secrets

Redaction should occur server-side before values are returned to Studio.

---

## 19. Design Principle

Before implementing a business-specific feature, ask:

> Can this be implemented as a reusable platform capability instead?

Example:

Bad:

```text
RestaurantReceiptService
```

Better:

```text
DocumentTemplate
+
GenerateDocumentNode
+
Restaurant Receipt Template
```

This principle is what allows FlowOps to support many industries without becoming a collection of unrelated systems.
