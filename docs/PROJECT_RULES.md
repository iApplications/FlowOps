# FlowOps Project Rules

These rules should guide implementation decisions throughout the project.

---

## 1. RM0 Is a Hard Development Constraint

Every core feature must have a free local development path.

Paid services may be supported only as optional providers.

Examples:

```text
AI
✅ Ollama
⚪ OpenAI optional
⚪ Gemini optional

Email
✅ Mailpit
⚪ SMTP optional
⚪ SendGrid optional

Storage
✅ Local storage
⚪ S3 optional
⚪ Azure Blob optional
```

Do not introduce a mandatory paid dependency.

---

## 2. AI Is Optional Infrastructure

The core application must function without AI.

AI may assist with:

- Workflow generation
- Document extraction
- Classification
- Summarization
- Explanations

AI should not be required to:

- Calculate monetary totals
- Evaluate security permissions
- Determine organization access
- Store core workflow state
- Guarantee deterministic workflow transitions

---

## 3. AI Output Must Be Treated as Untrusted

AI-generated workflow definitions must be validated before use.

AI-generated workflows should initially be created as drafts.

Publishing remains a deliberate user action.

---

## 4. Generic Before Specific

Avoid implementing one-off business modules when a reusable platform capability can solve the problem.

Example:

Do not build:

```text
RestaurantReceiptModule
```

Build:

```text
DocumentTemplate
GenerateDocumentNode
Restaurant Receipt Template
```

---

## 5. Start With a Modular Monolith

Do not create microservices simply because the application contains multiple modules.

Start with one deployable backend with clear module boundaries.

Extract services later only when justified by:

- Independent scaling
- Reliability requirements
- Team ownership
- Performance isolation
- Deployment requirements

---

## 6. Published Workflow Versions Are Immutable

Version identity must exist from the first phase that supports publishing. A workflow may be edited while its version is Draft, but publishing freezes that exact `WorkflowVersion` permanently.

Changes after publication create a new Draft version. Only Published versions may start workflow instances.

Existing workflow instances remain pinned to their original `WorkflowVersionId` for their entire lifetime. Phase 7 may improve version-management UX, but it must not be required to make Phase 4 publishing safe.

---

## 7. Studio and Portal Are Separate Surfaces, Not Separate Platforms

FlowOps Studio and FlowOps Portal should share the same backend, domain model, workflow engine, organization model, and authorization rules.

Do not duplicate business logic between the two frontends.

Studio is configuration-heavy and intended for authorized business/admin users. Portal is task- and request-focused and should expose only what a participant or guest needs.

---

## 8. Every Workflow Execution Must Be Traceable and Debuggable

Store enough structured information to answer:

- Who or what started it?
- Which workflow version ran?
- Which nodes executed?
- What input did each node receive?
- What output did each node produce?
- What condition values were evaluated?
- What decisions were made?
- Who approved/rejected?
- What failed?
- How many retries occurred?
- How long did each step take?
- When did it happen?

Execution tracing should be part of the workflow runtime design from the beginning.

---

## 9. Debug Data and Audit Data Are Different

Audit data records important actor/business events such as publishing a workflow or approving a request.

Debug data explains system behavior such as node inputs/outputs, durations, retry attempts, and integration failures.

Do not rely on transient debug logs as the authoritative audit trail.

---

## 10. Debug Output Must Protect Secrets

The debugger, API inspector, webhook inspector, and logs must mask sensitive values including:

- Passwords
- API keys
- Access/refresh tokens
- Authorization headers
- Connection strings
- Signing secrets

Redaction must happen on the server before values are returned to the browser.

---

## 11. Dry Runs Must Not Cause Unintended Side Effects

A dry run or sandbox execution must be clearly marked and must not silently send real emails, mutate production records, invoke destructive webhooks, charge payments, or create duplicate documents.

Where simulation is not possible, the action must require an explicit test configuration.

---

## 12. Security Is Server-Side

The frontend is never trusted for authorization.

Every protected backend operation must verify:

- Authentication
- Organization membership
- Role/permission
- Ownership or access scope

---

## 13. Organization Data Must Be Isolated

Users from Organization A must never access Organization B data unless explicitly authorized through a future supported sharing mechanism.

Every relevant query must be organization-scoped.

---

## 14. Do Not Execute Arbitrary User Code

Workflow conditions must use a controlled expression format.

Do not allow users to upload or execute arbitrary:

- C#
- JavaScript
- SQL
- Shell commands

inside workflows.

---

## 15. Financial Calculations Must Be Deterministic

AI must not calculate authoritative totals.

Use application logic for:

- Subtotals
- Taxes
- Discounts
- Service charges
- Currency calculations
- Approval thresholds

AI can explain the result but should not be the source of truth.

---

## 16. Database Migrations Must Be Version Controlled

Every schema change should be represented by a migration committed to source control.

Avoid manual production database changes.

---

## 17. Secrets Must Never Be Committed

Do not commit:

- API keys
- Database passwords
- JWT signing secrets
- SMTP passwords
- OAuth client secrets

Use environment variables or development secret storage.

---

## 18. Avoid Premature Infrastructure

Do not introduce technology without a concrete reason.

Examples:

```text
Need background queue?
Start simple.
Introduce RabbitMQ when required.

Need cache?
Introduce Redis when there is a real caching/session requirement.

Need search engine?
Use PostgreSQL first.
Introduce dedicated search only if needed.
```

---

## 19. Keep the First Reference Scenario Narrow

The first fully working workflow should be Expense Approval.

Do not attempt to implement HR, restaurant, inventory, finance, and property workflows simultaneously.

Build platform capabilities once, then add templates.

---

## 20. Authentication Must Use Standards-Based Components

Do not build custom password hashing, session formats, MFA algorithms, passkey protocols, or SSO protocols.

Initial browser authentication should use ASP.NET Core Identity with secure server-managed cookie sessions.

Authentication or refresh credentials must not be stored in browser `localStorage`.

---

## 21. Privileged Accounts Require Stronger Authentication

Owner/Admin and other privileged Studio users should be subject to mandatory MFA policy.

The platform should support step-up authentication for high-risk actions such as role changes, credential regeneration, MFA changes, and destructive organization operations.

---

## 22. Authorization Must Be Deny-by-Default and Tenant-Aware

A successful login is never sufficient authorization. Protected backend operations must verify:

```text
Authenticated
+ Active organization membership
+ Required permission/policy
+ Resource access scope
```

Never trust organization IDs, roles, or permissions supplied by the browser without validating them against server-side data.

---

## 23. Guest Access Must Be Scoped and Revocable

Public/guest workflow links must use unguessable access tokens, not predictable sequential IDs.

Guest access should be restricted by resource, allowed action, expiry where appropriate, and revocation state. Sensitive documents may require additional verification or authenticated access.

---

## 24. Machine Credentials Are Separate From Human Sessions

External systems must use scoped API credentials or OAuth/OIDC flows rather than reusing human login sessions.

API secrets should be generated securely, shown only when necessary, and never stored in plaintext after creation when a one-way verifier can be used.

---

## 25. The API Is a Security Boundary

Studio and Portal are not security boundaries. Every API endpoint must remain secure when called directly by a browser script, Postman-like client, or external integration.

Protected API operations must enforce authentication, tenant membership, permission/scope, and resource-level access on the server.

---

## 26. API Credentials Must Be Scoped, Rotatable, and Revocable

Machine integrations must use separate credentials from human sessions. API secrets should be generated securely, shown once where possible, stored using a one-way verifier when possible, and support scope, expiration, rotation, revocation, and audit metadata.

---

## 27. Side Effects and Integrations Need Replay Protection

Use idempotency for operations where client retries could create duplicates, including workflow starts, receipts, invoices, payments, and purchase orders.

Inbound webhooks should verify signatures/timestamps where supported and detect duplicate event IDs. Outbound HTTP workflow nodes must implement SSRF protections and must not freely access localhost, private infrastructure, or cloud metadata endpoints.

---

## 28. API Logs Must Be Safe by Default

Do not expose stack traces or infrastructure details in production API responses. Apply request-size and endpoint-specific rate limits. Redact credentials, cookies, API keys, tokens, signing secrets, and other sensitive fields before API traffic is persisted or displayed in Developer Tools.

---

## 29. Workflow Runtime Terms Must Stay Distinct

Use these terms consistently across code, APIs, database schema, UI, and documentation:

```text
WorkflowDefinition → logical workflow identity
WorkflowVersion    → versioned snapshot; immutable once Published
WorkflowInstance   → durable business process pinned to one version
WorkflowExecution  → one start/resume processing activation of an instance
WorkflowExecutionStep → one node execution record within an execution
```

A long-running `WorkflowInstance` may have multiple `WorkflowExecution` records as it waits and resumes. Do not use `WorkflowInstance` and `WorkflowExecution` as synonyms.

---

## 30. Reference Scenario Must Respect Phase Dependencies

Phase 5 proves a baseline Expense Approval flow with one Manager approval and final Approved/Rejected status. Amount thresholds, Finance escalation, and Director escalation belong to Phase 6 after conditional-routing capabilities exist.

Do not declare a phase complete by relying on functionality scheduled for a later phase.


---

## 31. Audit Recording Exists Before Audit Reporting

Basic durable, append-only audit persistence is a Phase 1 platform capability. Security-sensitive actions must use it immediately, and later features must add their relevant events when those features are introduced.

Examples:

```text
Phase 1 → login/security/admin events
Phase 4 → workflow publish/version events
Phase 5 → approval decisions/comments
Phase 11 → document generation events
Phase 12 → integration/API credential/webhook events
```

Phase 10 expands the audit explorer, timelines, dashboards, filtering, and reporting. It must never be treated as the first phase in which audit records are persisted.

## 32. Keep Documentation Updated

When a phase changes architecture or behavior, update the affected documentation in the same change.

The canonical documentation layout is `README.md` at the repository root and the remaining project documents under `docs/`. Do not flatten these files into the root.

At minimum review:

```text
README.md
docs/ARCHITECTURE.md
docs/ROADMAP.md
docs/PROJECT_RULES.md
docs/SECURITY.md
```

---

## 33. Definition of Done

A feature is not complete only because the UI works.

A feature should normally include:

- Backend implementation
- Frontend implementation where applicable
- Validation
- Authorization
- Error handling
- Persistence
- Audit behavior where relevant
- Execution/debug telemetry where relevant
- Secret redaction for debug/integration surfaces
- Tests for important logic
- Documentation updates

---

## 34. Coding Priorities

When deciding between clever code and maintainable code:

**Choose maintainable code.**

Prefer:

- Clear names
- Small focused services
- Explicit domain rules
- Strong typing
- Predictable behavior

Avoid unnecessary abstractions until repetition or architectural pressure justifies them.

---

## 35. API Design

Use consistent REST conventions.

Example:

```text
GET    /api/workflows
POST   /api/workflows
GET    /api/workflows/{id}
PUT    /api/workflows/{id}
POST   /api/workflows/{id}/publish
POST   /api/workflows/{id}/instances
GET    /api/workflow-instances/{id}
```

Return consistent error responses.

---

## 36. Project Success Criteria

FlowOps succeeds when a new business workflow can be added mainly through configuration rather than by creating a new application module.

A useful test is:

> Can Expense Approval, Restaurant Receipt, and Employee Onboarding run on the same workflow engine without special-case architecture?

If yes, the platform is moving in the right direction.
