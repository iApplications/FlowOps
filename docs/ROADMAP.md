# FlowOps Roadmap

The roadmap is intentionally designed so every development phase has an **RM0 implementation path**.

The first reference scenario is Expense Approval. Other scenarios should be introduced only after the platform foundation is stable.

Version identity is introduced when publishing first appears in **Phase 4**. Phase 7 expands version-management capabilities; it does not defer runtime version correctness. The Phase 5 Expense Approval milestone is deliberately a single-manager baseline, with threshold-based escalation added in Phase 6.

---

# Phase 0 — Project Foundation

## Goal

Prepare the repository and development environment.

## Deliverables

- Repository structure
- FlowOps Studio Next.js frontend
- FlowOps Portal Next.js frontend
- ASP.NET Core backend
- PostgreSQL
- Docker Compose
- Development configuration
- Logging
- Basic health checks
- Shared frontend packages/API client structure
- Initial documentation, including `docs/SECURITY.md`

## Cost

RM0

---

# Phase 1 — Authentication & Organizations

## Goal

Allow users to securely access FlowOps and work inside organizations.

## Deliverables

- Register
- Login
- Logout
- User profile
- ASP.NET Core Identity integration
- Secure `HttpOnly`/`Secure` cookie sessions
- CSRF/antiforgery protection
- Login rate limiting and account lockout
- Password reset flow
- TOTP authenticator MFA
- Recovery codes
- Require MFA for privileged Studio roles
- Session expiration and revocation
- Organization creation
- Organization members
- Roles
- Policy/permission-based authorization
- Organization switching
- Server-side tenant isolation
- Studio/Portal route protection
- Shared backend authorization rules for both surfaces
- Basic durable `AuditEvent` persistence and append-only audit service
- Security-sensitive audit events written through the durable audit service
- Security tests for common authorization failures

Planned after the initial authentication foundation:

```text
Passkeys / WebAuthn
Step-up authentication
Microsoft / Google / enterprise OIDC SSO
Active session/device management
```

Suggested initial roles:

```text
Owner
Admin
Member
```

## Cost

RM0

---

# Phase 2 — Form Builder

## Goal

Allow users to create configurable business forms.

## Deliverables

- Create form
- Edit form
- Form field designer
- Field validation
- Form preview
- Form submission
- Submission history
- Render published forms in FlowOps Portal

Initial fields:

```text
Text
Textarea
Number
Currency
Date
Dropdown
Checkbox
File
```

## Cost

RM0

---

# Phase 3 — Workflow Designer

## Goal

Allow users to visually design workflows.

## Deliverables

- Workflow list
- Create workflow
- Drag-and-drop canvas
- Nodes
- Connections
- Node configuration
- Draft saving
- Draft `WorkflowVersion` identity/state
- Basic workflow validation

Initial nodes:

```text
Start
Condition
Approval
End
```

## Cost

RM0

---

# Phase 4 — Workflow Execution Engine + Core Version Identity

## Goal

Publish versioned workflow snapshots and execute them reliably.

## Deliverables

- Publish a Draft `WorkflowVersion` as an immutable Published version
- Stable `WorkflowVersionId` and version number from the first publish
- Published-version immutability enforcement
- Create a new Draft version for subsequent edits
- Workflow instance creation pinned to one exact `WorkflowVersionId`
- Distinct `WorkflowInstance` and `WorkflowExecution` persistence
- Node execution
- Basic deterministic routing between connected nodes
- Workflow variables/context
- Basic instance/execution history
- Per-node execution step records
- Correlation IDs
- Structured failure information
- Failure handling
- Workflow status tracking

Phase 4 introduces the **minimum versioning contract required by runtime correctness**. Rich version-history/management UX remains in Phase 7.

## Cost

RM0

---

# Phase 5 — Approval Inbox

## Goal

Support real approval workflows.

## Deliverables

- My Tasks
- Pending approvals
- Approve
- Reject
- Request changes
- Comments
- Approval history
- Approval decisions/comments persisted as durable business history and corresponding audit events
- Role-based assignment
- Portal task/approval experience
- Requester status tracking

At the end of this phase, the first **baseline** Expense Approval workflow should work end to end:

```text
Employee submits expense
        ↓
Manager approval
     ↙      ↘
 Approved  Rejected
```

Amount-based escalation is intentionally **not** required yet; it is added in Phase 6 after the conditional-routing features exist.

## Cost

RM0

---

# Phase 6 — Conditional Routing & Advanced Logic

## Goal

Add data-driven branching and calculations, then upgrade the baseline Expense Approval workflow with amount-based escalation.

## Deliverables

- If / Else
- Multiple conditions
- Switch logic
- Calculation node
- Data mapping
- Workflow variables
- Wait state
- Parallel branch research/prototype

Example:

```text
Amount < 500
→ Manager

Amount >= 500 && Amount <= 5000
→ Manager → Finance

Amount > 5000
→ Manager → Finance → Director
```

## Cost

RM0

---

# Phase 7 — Workflow Version Management

## Goal

Add richer management and visibility around the core version identity/immutability already required in Phase 4.

## Deliverables

- Version history UI
- View old published versions
- Compare versions
- Show which version each workflow instance uses
- Create a new draft from a published version
- Restore an older version by cloning it into a new draft (never mutate history)
- Archive/deprecate versions where appropriate
- Published/draft lifecycle UX and validation

## Cost

RM0

---

# Phase 8 — Notifications & SLA

## Goal

Notify users when action is required and detect overdue tasks.

## Deliverables

- In-app notifications
- Local development email using Mailpit
- Approval reminders
- SLA configuration
- Overdue task detection
- Background processing

Not included initially:

```text
SMS
WhatsApp
Paid email provider
```

## Cost

RM0

---

# Phase 9 — Developer Tools & Workflow Debugger

## Goal

Make workflow behavior observable and safe to troubleshoot before the platform gains more integrations and AI capabilities.

## Deliverables

- Studio Developer Tools area
- Execution history
- Visual step-by-step execution trace
- Node input/output inspector
- Workflow variable inspector
- Condition evaluation details
- Duration and timing per node
- Retry attempt history
- Structured error display
- Correlation IDs
- Dry-run / sandbox mode
- Test an individual node
- Secret redaction in all debug output
- Retry from failed step where safe

Dry-run executions must never be confused with real production executions.

## Cost

RM0

---

# Phase 10 — Audit UI & Reporting

## Goal

Expand the durable audit foundation established in Phase 1 into operational audit review, timelines, dashboards, and reporting.

## Foundation Already Available

Phase 1 establishes the append-only `AuditEvent` persistence/service required by security-sensitive actions. Later phases add their own events as features are introduced; for example, Phase 5 records approval decisions and comments. Phase 10 must **not** be the first phase that records audit data.

## Deliverables

- Audit explorer/review UI
- Filtering by actor, organization, entity, event type, and date
- Request timeline built from durable business/audit history
- Workflow execution history views
- Dashboard
- Pending tasks report
- Average approval time
- Workflow volume
- Failure report
- SLA breach report
- Export/reporting capabilities where useful

Audit records and debug telemetry should remain separate data concepts. Phase 10 expands audit visibility and reporting; it does not introduce the underlying audit persistence model.

## Cost

RM0

---

# Phase 11 — Document Engine

## Goal

Generate reusable business documents from workflow data.

## Deliverables

- Document templates
- Template variables
- Document preview
- Generate document workflow node
- PDF generation
- Generated document history
- Download document
- Portal document viewing/download where authorized

Initial templates:

```text
Expense Report
Restaurant Receipt
Invoice
```

This phase validates that FlowOps can support scenarios outside approval workflows.

## Cost

RM0

---

# Phase 12 — REST API, Webhooks & Integrations

## Goal

Allow external systems to start and interact with workflows.

## Deliverables

- Scoped API authentication separate from browser sessions
- Secure API credential generation, one-time display, hashed/verifier storage, rotation, and revocation
- API scopes/permissions and organization ownership
- Server-side tenant/resource authorization on every endpoint
- Endpoint-specific rate limiting
- Restrictive production CORS configuration
- Input validation, request/body limits, upload limits, and safe production errors
- Idempotency support for retryable side-effecting operations
- Start workflow API
- Query workflow status
- Webhook trigger
- Signed/timestamped webhook verification where supported
- Webhook duplicate/replay detection
- HTTP request node with SSRF protections
- Outgoing webhook
- Retry handling
- Webhook history
- API request/response inspector in Studio with server-side secret redaction
- Webhook inspector and permission-checked safe replay
- Correlation IDs and structured API/integration failure information
- Negative security tests for cross-tenant access, revoked credentials, replayed webhooks, rate limits, and blocked outbound destinations

Example:

```text
POST /api/workflows/{workflowId}/instances
```

## Cost

RM0

---

# Phase 13 — AI Workflow Generator

## Goal

Allow users to generate workflow drafts from natural language.

Example:

> When payment is received, generate a receipt and send it to the customer. If the total exceeds RM1,000, notify the manager.

Possible result:

```text
Payment Received
      ↓
Generate Receipt
      ↓
Notify Customer
      ↓
Total > 1000?
   ↙        ↘
 No         Yes
 ↓           ↓
End     Notify Manager
```

## Deliverables

- AI provider abstraction
- Ollama provider
- Natural-language workflow request
- Structured workflow generation
- Workflow validation
- AI-generated drafts only
- User review before publish

## Cost

RM0 using local AI

---

# Phase 14 — AI Operations Assistant

## Goal

Allow users to ask questions about workflows, operations, and execution failures.

Example questions:

```text
Why are expense approvals delayed this week?

Show requests pending for more than three days.

Summarize this workflow execution.

Why did this execution fail?
```

## Deliverables

- Natural-language workflow search
- Execution summaries
- Bottleneck analysis
- AI-assisted failure explanation using structured debug data
- Suggested actions
- Explain workflow

Actions that modify data should still require normal permission checks and, where appropriate, user confirmation.

## Cost

RM0 using local AI

---

# Future Templates

Once the platform is stable, create reusable templates instead of hard-coded vertical applications.

Potential templates:

### Finance

- Expense approval
- Purchase request
- Invoice approval

### HR

- Leave application
- Employee onboarding
- Equipment request

### Restaurant

- Order receipt
- Daily sales closing
- Ingredient restocking

### Retail

- Inventory restock
- Return request
- Discount approval

### Operations

- Maintenance request
- Incident report
- Service request

---

# Not Planned for Early Development

The following should not be prioritized during early phases:

- Kubernetes
- Microservices
- Paid cloud infrastructure
- Native mobile applications
- SMS integration
- WhatsApp integration
- Complex marketplace
- Multi-region deployment
- High-scale distributed architecture

The project should earn this complexity through actual requirements rather than adding it for portfolio appearance.
