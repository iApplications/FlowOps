# FlowOps

**AI-Powered Business Automation Platform**

> Describe it → Build it → Run it → Track it.

FlowOps is a web-based workflow automation platform that allows individuals and businesses to create, run, and monitor business processes using a visual workflow builder and AI-assisted configuration.

Instead of hard-coding separate applications for expense approval, restaurant receipts, employee onboarding, inventory management, invoicing, or service requests, FlowOps provides reusable workflow building blocks that can support many business scenarios.

---

## Why FlowOps?

Many small and medium businesses still manage processes using a mixture of spreadsheets, WhatsApp, email, paper forms, and manually generated documents.

Typical examples include:

- Expense claims
- Purchase requests
- Leave applications
- Employee onboarding
- Restaurant receipts
- Invoice approval
- Inventory restocking
- Maintenance requests
- Customer onboarding
- Quotations and purchase orders

FlowOps aims to turn these processes into structured, trackable workflows without requiring businesses to build a separate custom system for each use case.

---

## Core Concept

A workflow is made from reusable nodes:

```text
Trigger
  ↓
Action
  ↓
Condition
  ↓
Action
  ↓
Document
  ↓
Notification
```

Example:

```text
Payment Received
      ↓
Generate Receipt
      ↓
Email Customer
      ↓
Amount > RM1,000?
    ↙          ↘
   No          Yes
   ↓            ↓
 Finish    Notify Manager
```

The same workflow engine can support many industries and scenarios.

---

## Product Surfaces

FlowOps should provide two primary web experiences backed by the same platform and workflow engine.

### FlowOps Studio

For organization owners, administrators, workflow builders, managers, and operational teams.

Typical capabilities:

- Dashboard
- Workflow Builder
- Form Builder
- Document Builder
- Requests and approvals
- Users, roles, and permissions
- Integrations
- Reports
- Audit logs
- Developer Tools / Debugger
- Organization settings

### FlowOps Portal

For employees, approvers, requesters, customers, vendors, candidates, and other workflow participants.

Typical capabilities:

- Submit forms and requests
- View assigned tasks
- Approve or reject when authorized
- Track request status
- View generated documents
- View notifications
- Access public or guest workflow pages when allowed

The Portal should intentionally expose less configuration than Studio. Public users may be allowed to interact with selected workflows without receiving a full FlowOps account.

```text
FlowOps Studio ─────┐
                    ├── Shared FlowOps API / Platform
FlowOps Portal ─────┘
                         │
                         ├── Workflow Engine
                         ├── Form Engine
                         ├── Document Engine
                         ├── Integration Engine
                         ├── Notification Engine
                         ├── AI Engine
                         └── Observability Engine
```

Studio and Portal are separate user experiences, **not separate backends or duplicated products**.

### Accounts and organization access

Studio and Portal use independent accounts on the shared backend. The same email can register once on each site, with separate passwords, email confirmation, password resets, MFA, organization memberships, and browser sessions.

- **Studio:** Business owners and administrators create organizations and manage people. After confirming email, a new Studio account creates its workspace as Owner and sets up MFA before entering management features.
- **Portal:** Customers and participants use organizations assigned through Studio. Portal does not provide organization creation or member management.
- **Assignments:** Studio adds a confirmed Portal account as **Member (Portal)** or a confirmed Studio account as **Admin (Studio)**. Assigning a role does not convert an account between sites.
- **Organization isolation:** Creating an organization selects it and adds only its creator as Owner. People from another organization are not copied. Portal lists only organizations assigned to its signed-in account.
- **Removal:** Owners can remove Admins and Members; Admins can remove Members. The Owner cannot be removed. Removing membership removes that organization's access.

See [Local development](docs/DEVELOPMENT.md) for startup commands, database migrations, and testing.

---

## AI-Assisted Workflow Creation

Users can describe a workflow in natural language:

> When a restaurant receives payment, generate a receipt and email it to the customer. If the transaction is above RM1,000, notify the manager.

FlowOps converts the request into a proposed workflow that the user can review and edit visually.

AI is used to assist configuration, extraction, classification, summarization, and workflow generation.

AI should **not** silently control critical business logic. Deterministic operations such as calculations, approvals, permissions, totals, taxes, and workflow transitions should be handled by the workflow engine.

---

## Example Use Cases

### Expense Approval

```text
Employee submits claim
        ↓
Manager approval
        ↓
Amount >= RM500?
     ↙        ↘
   No          Yes
   ↓            ↓
Approve       Finance
                ↓
        Amount > RM5,000?
             ↙      ↘
            No      Yes
            ↓        ↓
         Approve   Director
```

### Restaurant Receipt

```text
Order Completed
      ↓
Payment Confirmed
      ↓
Calculate Total
      ↓
Generate Receipt
      ↓
Store Transaction
      ↓
Email / Download Receipt
```

### Employee Onboarding

```text
Offer Accepted
      ↓
Create Employee Record
      ↓
Create Onboarding Tasks
      ↓
Request Equipment
      ↓
Notify HR
      ↓
Schedule Orientation
```

### Inventory Restocking

```text
Stock Level Updated
      ↓
Stock < Minimum Level?
     ↙             ↘
   No              Yes
   ↓                ↓
 Finish      Create Restock Request
                    ↓
              Manager Approval
```

---

## Core Modules

### 1. Authentication, Organizations & Security

FlowOps uses a standards-based authentication model built on ASP.NET Core Identity. Browser users should authenticate with secure server-managed sessions rather than storing bearer tokens in browser local storage.

Initial capabilities:

- User registration, login, logout, and profile management
- ASP.NET Core Identity password hashing and account management
- Secure `HttpOnly` + `Secure` authentication cookies
- CSRF/antiforgery protection for state-changing browser requests
- Login rate limiting and account lockout
- TOTP authenticator MFA and recovery codes
- Mandatory MFA policy for privileged Studio users
- Organization/workspace support
- Organization membership and tenant isolation
- Roles plus policy/permission-based authorization
- Server-side authorization on every protected operation
- Session expiration and revocation
- Security-sensitive audit events

API security is enforced independently of the web UI. The platform design includes tenant/resource checks on every protected endpoint, endpoint-specific rate limiting, restrictive CORS, CSRF protection for cookie-authenticated mutations, scoped/rotatable machine credentials, idempotency for duplicate-sensitive operations, signed webhook verification/replay detection, SSRF protection for outbound HTTP nodes, safe request limits/errors, and server-side secret redaction in logs/debug tools.

Planned extensions:

- Passkeys / WebAuthn
- Step-up authentication for high-risk actions
- Microsoft / Google / enterprise OpenID Connect SSO
- Device and active-session management
- Scoped API credentials and OAuth/OIDC for integrations
- Secure guest/public links with limited, revocable access

Authentication answers **who the user is**. Authorization separately determines **what that user may do inside the active organization**. A valid login must never bypass organization, permission, ownership, or resource-scope checks.

### 2. Form Builder

Build configurable forms without changing code.

Supported fields may include:

- Text
- Number
- Currency
- Date
- Dropdown
- Checkbox
- Radio
- File upload
- User selection
- Repeating items

### 3. Workflow Designer

Visual drag-and-drop workflow editor.

Node categories:

#### Triggers

- Form submitted
- Record created
- Payment received
- Webhook received
- Scheduled time
- Manual trigger
- API call

#### Logic

- If / Else
- Switch
- Wait
- Approval
- Parallel branch
- Loop

#### Data

- Create record
- Update record
- Lookup record
- Calculate
- Transform data
- Call API

#### Documents

- Generate receipt
- Generate invoice
- Generate quotation
- Generate purchase order
- Generate PDF
- Generate certificate

#### Communication

- In-app notification
- Email
- Webhook
- HTTP request

#### AI

- Extract information
- Classify
- Summarize
- Generate text
- Analyze document
- Suggest workflow

### 4. Workflow Execution Engine

Responsible for:

- Starting workflow instances
- Executing nodes
- Evaluating conditions
- Tracking workflow state
- Handling approvals
- Recording failures
- Retrying safe operations
- Maintaining execution history

### 5. Approval Inbox

Users can see requests requiring action.

Features:

- Approve
- Reject
- Request changes
- Add comments
- View workflow history
- View attachments

### 6. Document Engine

Reusable templates can generate business documents using workflow data.

Examples:

- Receipts
- Invoices
- Quotations
- Purchase orders
- Offer letters
- Certificates
- Service reports

Example template variables:

```text
{{receiptNumber}}
{{date}}
{{customer.name}}
{{subtotal}}
{{tax}}
{{total}}
```

### 7. Notification Engine

Initial supported channels:

- In-app notifications
- Local development email

Future optional channels:

- SMTP
- SendGrid
- Slack
- Microsoft Teams
- WhatsApp
- SMS

### 8. Audit Trail

Every important action should be traceable.

Example:

```text
REQ-2026-01882

10:32 Yap submitted request
11:14 Manager approved
14:23 Finance requested clarification
15:02 Request updated
16:45 Finance approved
```

### 9. Reporting

Possible reports:

- Workflow volume
- Approval time
- Pending tasks
- SLA breaches
- Failure rates
- Workflow bottlenecks

### 10. AI Assistant

Possible capabilities:

- Generate workflow drafts from natural language
- Explain workflows
- Suggest missing workflow steps
- Summarize workflow history
- Extract fields from uploaded documents
- Classify requests

### 11. Developer Tools & Workflow Debugger

FlowOps Studio should include a dedicated area for debugging workflow executions.

Core capabilities:

- Execution history
- Visual step-by-step execution trace
- Node input and output inspection
- Workflow variables
- Condition evaluation details
- Error messages and retry attempts
- Node duration and timing
- Dry-run / sandbox execution
- Test an individual node
- API request/response inspection
- Webhook inspection and replay
- Retry from failed step where safe
- System health and structured logs

Example execution trace:

```text
Execution #EXEC-0001832
Workflow: Restaurant Payment v1.4

✓ Payment Received       42ms
✓ Generate Receipt      183ms
✕ Send Email            921ms
○ Update Sales          Not executed
```

Debugging information and audit history serve different purposes:

- **Audit:** who changed or performed what, and when.
- **Debug:** why an execution behaved a certain way or failed.

Sensitive information such as API keys, passwords, tokens, and authorization headers must always be masked in debug output.

---

## Suggested Technology Stack

### Frontend

- Next.js (Studio + Portal)
- TypeScript
- React
- Tailwind CSS
- React Flow

### Backend

- ASP.NET Core Web API
- C#
- Entity Framework Core

### Database

- PostgreSQL

### Supporting Services

- Redis
- RabbitMQ
- Mailpit
- Docker Compose

### AI

Development should support local AI first:

- Ollama
- Local compatible models

AI providers should be implemented through an abstraction so cloud providers can be added later.

```text
IAIProvider
 ├── OllamaProvider
 ├── OpenAIProvider      optional
 ├── GeminiProvider      optional
 └── ClaudeProvider      optional
```

---

## Security Documentation

Detailed authentication, authorization, tenant-isolation, session, guest-access, API-authentication, and security requirements are defined in [`docs/SECURITY.md`](docs/SECURITY.md).

---

## RM0 Development Rule

FlowOps must be fully developable without paid services.

During development:

```text
Frontend     → localhost
Backend      → localhost
PostgreSQL   → Docker
Redis        → Docker
RabbitMQ     → Docker
Email        → Mailpit
File Storage → Local storage
AI           → Ollama
```

Paid services may be introduced only as optional providers.

Examples:

```text
IFileStorage
 ├── LocalFileStorage       RM0
 ├── S3Storage              optional
 └── AzureBlobStorage       optional

INotificationProvider
 ├── InAppProvider          RM0
 ├── LocalEmailProvider     RM0
 ├── SMTPProvider           optional
 └── SendGridProvider       optional
```

---

## Project Principles

1. The workflow engine must work without AI.
2. AI assists users; deterministic business logic remains deterministic.
3. Every feature must have a zero-cost development path.
4. Avoid vendor lock-in.
5. Start as a modular monolith before considering microservices.
6. Studio and Portal must share the same backend platform and domain rules.
7. Every workflow execution must be traceable and debuggable.
8. Audit history and debug telemetry must remain conceptually separate. Basic durable audit recording starts in Phase 1; Phase 10 expands audit UI and reporting.
9. Workflow definitions must support versioning.
10. Running workflows must not break when workflow definitions change.
11. Permissions must be checked server-side.
12. Build generic capabilities before industry-specific features.

---

## Initial Reference Workflow

The first business scenario should be **Expense Approval**, delivered in two milestones so the roadmap does not depend on features that have not been built yet.

**Phase 5 baseline:**

```text
Employee submits expense
        ↓
Manager approval
     ↙      ↘
 Approved  Rejected
```

This validates forms, workflow execution, approval tasks, requester status, roles, audit history, and version-pinned runtime behavior.

**Phase 6 enhancement:** add data-driven conditional routing and amount thresholds, for example Finance approval from RM500 and Director approval above RM5,000.

The threshold-based example shown earlier in this README therefore represents the **Phase 6+ target workflow**, not the Phase 5 minimum.

After the engine is stable, other templates such as Restaurant Receipt, Employee Onboarding, Inventory Restocking, and Invoice Approval can be added without changing the core architecture.

---

## Documentation

The repository uses one canonical documentation layout and files should **not be flattened into the repository root**:

```text
flowops/
├── README.md
└── docs/
    ├── ARCHITECTURE.md
    ├── ROADMAP.md
    ├── PROJECT_RULES.md
    ├── SECURITY.md
    └── DEVELOPMENT.md
```

- [Architecture](docs/ARCHITECTURE.md)
- [Roadmap](docs/ROADMAP.md)
- [Project Rules](docs/PROJECT_RULES.md)
- [Security](docs/SECURITY.md)
- [Local development](docs/DEVELOPMENT.md)
- [Phase 2 delivery and testing](docs/PHASE2.md)

---

## Status

**Current stage:** Phase 1 merged into main; Phase 2 Form Builder in development on `dev/Phase2_Form`.

Implemented features include separate Studio/Portal accounts and sessions, email confirmation and password reset, authenticator MFA and recovery codes, organization creation and switching, membership management, and server-side organization permissions. Studio supports draft forms, field design/preview and immutable publishing; Portal lists and displays published forms for the active assigned organization. Form submissions/history, file storage and workflow execution remain upcoming work.

Local services:

| Service | Address |
|---|---|
| Studio | http://localhost:3000 |
| Portal | http://localhost:3001 |
| API / Swagger | http://localhost:5050/swagger |
| Mailpit inbox | http://localhost:8025 |

The account separation migration preserves known Studio signups and existing Owners/Admins as Studio accounts, classifies remaining legacy accounts as Portal accounts, and requires signing in again. Register independently on the other site if access to both is needed.

No production deployment is required during the early development phases.

