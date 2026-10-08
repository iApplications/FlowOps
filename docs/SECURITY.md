# FlowOps Security

This document defines the initial security architecture and non-negotiable security requirements for FlowOps.

The objective is to provide a secure RM0 development path without depending on a paid identity provider.

---

## 1. Security Model

FlowOps has two main browser surfaces:

```text
FlowOps Studio  → administrators, owners, builders, managers
FlowOps Portal  → employees, requesters, approvers, customers, guests
```

Both surfaces use the same backend identity, authorization, organization, and security model.

The backend is the security boundary. Frontend visibility rules are only a usability feature and must never be treated as authorization.

---

## 2. Authentication

Initial authentication should use **ASP.NET Core Identity**.

Supported initially:

- Email/username + password
- Secure browser sessions
- TOTP authenticator MFA
- Recovery codes
- Password reset
- Account lockout
- Login rate limiting
- Session expiration and revocation

Planned extensions:

- Passkeys / WebAuthn
- Step-up authentication
- Microsoft / Google sign-in
- Enterprise OpenID Connect SSO
- Active-session/device management

Do not create custom password hashing, MFA algorithms, passkey protocols, or SSO protocols.

---

## 3. Browser Session Strategy

Studio and Portal browser authentication should use secure server-managed cookies.

Required properties in production:

```text
HttpOnly = true
Secure   = true
SameSite = configured appropriately for the deployment topology
```

State-changing browser requests must use CSRF/antiforgery protection.

Authentication or refresh credentials must **not** be persisted in browser `localStorage`.

All production authentication traffic must use HTTPS.

---

## 4. Password Security

Password storage and verification must be delegated to ASP.NET Core Identity or an equivalent reviewed identity component.

Never:

- Store plaintext passwords
- Log passwords
- Send passwords back to the browser
- Implement custom hashing logic
- Include passwords in debug telemetry

Password reset tokens must be single-purpose and time-limited. Security-sensitive account changes should invalidate affected sessions where appropriate.

---

## 5. Multi-Factor Authentication

FlowOps should support TOTP authenticator MFA and recovery codes from the initial security foundation.

Organization policy should be able to require MFA for privileged Studio users. At minimum, Owner/Admin-level access should be designed to support mandatory MFA.

Later, passkeys/WebAuthn should be added as a phishing-resistant authentication option.

SMS OTP is not required for the initial architecture and should not become a dependency.

---

## 6. Step-Up Authentication

High-risk operations should be able to require recent reauthentication or a stronger factor. Examples:

- Change another user's role
- Change organization security policy
- Disable MFA
- Regenerate API credentials
- Reveal or replace integration secrets
- Delete an organization
- Perform other destructive administrative actions

---

## 7. Authorization

Authentication answers **who the actor is**. Authorization answers **what the actor may do**.

Every protected backend action should enforce:

```text
Authenticated?
      ↓
Member of active organization?
      ↓
Has required permission/policy?
      ↓
Has access to the target resource?
      ↓
ALLOW
```

Use roles for broad job functions and policies/permissions for actual capabilities.

Example roles:

```text
Owner
Admin
Workflow Designer
Manager
Approver
Member
```

Example permissions:

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

Default behavior should be **deny unless explicitly allowed**.

---

## 8. Tenant / Organization Isolation

FlowOps is multi-tenant. Most business records should contain:

```text
OrganizationId
```

A user from Organization A must never access Organization B data simply by changing a URL, request body, or resource ID.

Every tenant-scoped query must be filtered to the active organization before returning or mutating data.

Security tests should explicitly attempt cross-tenant access.

---

## 9. FlowOps Studio Security

Studio has access to high-impact capabilities such as:

- Workflow publishing
- User/role management
- Integrations and secrets
- Audit logs
- Developer/debug tools
- Organization settings

Therefore Studio should enforce stronger policies than ordinary Portal access.

Recommended design:

```text
Login
  ↓
Password / Passkey
  ↓
MFA when policy requires
  ↓
Studio
  ↓
Step-up authentication for selected high-risk actions
```

---

## 10. FlowOps Portal Security

Portal users should receive only the forms, tasks, approvals, documents, and status data they are authorized to access.

A Portal user must not gain Studio privileges by navigating directly to a Studio route or manually calling a Studio API. Backend authorization is authoritative.

Organization policy may require MFA for approvers or all members if needed.

---

## 11. Guest / Public Access

Some workflows may intentionally support users without accounts, such as:

- Restaurant receipt viewing
- Public application forms
- Vendor forms
- Booking confirmations

Public resources must not rely on predictable URLs such as:

```text
/receipt/123
/receipt/124
```

Use unguessable, scoped access tokens.

A guest token should be associated with:

- Allowed resource/workflow
- Allowed operation
- Expiration where appropriate
- Revocation state
- Audit metadata

More sensitive documents may require authenticated access or additional email verification instead of anonymous token access.

---

## 12. API & Integration Security

The FlowOps API is a first-class security boundary. Security must not depend on Studio or Portal hiding routes or buttons. Every API request must be authenticated and authorized according to the request type.

### 12.1 Human Browser Requests

Studio and Portal use secure cookie-based sessions. State-changing requests must also pass CSRF/antiforgery validation. Browser APIs must enforce the same organization, permission, and resource checks as every other API.

### 12.2 Machine / Integration Authentication

Machine integrations are separate security principals and must not reuse normal user sessions.

Initial API credentials should support:

- Cryptographically secure random generation
- One-time secret display
- One-way hash/verifier storage where possible
- Explicit organization ownership
- Explicit scopes
- Expiration where appropriate
- Revocation and rotation
- Last-used metadata
- Security audit events

Example scopes:

```text
workflow:start
workflow:read
orders:create
payments:create
```

OAuth 2.0 / OIDC client credentials may be introduced later for larger integrations.

### 12.3 Authorization and Tenant Isolation

A valid credential is never sufficient by itself. Every protected endpoint must verify:

```text
Authenticated principal?
      ↓
Credential/session still valid?
      ↓
Active organization allowed?
      ↓
Required scope/permission?
      ↓
Target resource belongs to that organization?
      ↓
Resource-level policy allows the action?
      ↓
ALLOW
```

Tenant filtering should be centralized wherever practical so developers do not have to remember to add `OrganizationId` checks manually to every query. Security tests must include ID tampering and cross-tenant access attempts.

### 12.4 Rate Limiting and Abuse Protection

Different endpoint groups should use different rate-limit policies. At minimum distinguish:

- Login and password-reset endpoints
- Guest/public endpoints
- Normal authenticated application APIs
- Machine/integration APIs
- Webhook receivers
- Expensive operations such as document generation

Rate limiting should protect both security-sensitive endpoints and server resources. Limits may be keyed by account, credential, organization, IP address, or a combination depending on the endpoint.

**Current implementation and deployment requirement.** The authentication endpoints (register, confirm, resend, login, password reset) share an `auth` policy: a fixed window of `RateLimiting:AuthPermitLimit` requests per minute (default 10) per client IP. Account lockout (5 failures, 15 minutes) limits password guessing per account, including unconfirmed accounts.

Browsers reach the API through the Next.js proxy. The API therefore sees the proxy's address unless forwarded headers are enabled, and then every user shares one bucket. Next.js forwards a client-supplied `X-Forwarded-For` unchanged, so the API must not trust that header when Next.js is exposed directly. In production:

1. Put an edge reverse proxy or load balancer in front of Studio/Portal that sets `X-Forwarded-For` to the real client address, appending to or overwriting any value the client sent.
2. Set `ForwardedHeaders__Enabled=true` on the API. Loopback peers are trusted by default; add the Next.js servers' addresses to `ForwardedHeaders:KnownProxies` when they run on other hosts.

Only the last `X-Forwarded-For` hop is used (`ForwardLimit = 1`). This must be verified in the target deployment before go-live.

### 12.5 Input Validation, Request Limits, and Errors

Treat all API input as untrusted. Validate:

- DTO shape and required fields
- IDs and allowed values
- Numeric/date ranges
- File size and content policy
- Pagination limits
- Request body size
- Timeouts and cancellation

Production errors must return safe problem details and correlation identifiers without exposing stack traces, SQL, connection strings, filesystem paths, secrets, or internal implementation details.

### 12.6 CORS and CSRF

CORS must use explicit trusted origins for authenticated browser APIs. Do not use wildcard origins for credentialed Studio/Portal requests.

Development may allow approved localhost origins separately from production configuration.

CORS is not authorization. Cookie-authenticated state-changing requests must still use CSRF/antiforgery protection.

### 12.7 Idempotency for Side-Effecting Operations

Operations that may be retried by clients or infrastructure should support idempotency when duplicate execution could cause harm. Examples include:

- Payment-related events
- Receipt/invoice generation
- Workflow starts
- Purchase orders
- External callbacks

Conceptually:

```text
Idempotency-Key: PAYMENT-93823
```

The backend should detect a previously completed request for the same key and return the prior outcome instead of repeating the side effect. Idempotency records must be scoped to the correct organization/integration and have a defined retention policy.

### 12.8 Webhook Verification and Replay Protection

Inbound webhooks must not be trusted solely because they reach a known URL. Where the provider supports signing, verify a signature over the raw payload plus timestamp or equivalent canonical data.

A webhook receiver should validate:

```text
Valid signature?
      ↓
Timestamp inside allowed window?
      ↓
Event ID already processed?
      ↓
Expected integration / organization?
      ↓
Process event
```

Store enough metadata to detect duplicate/replayed events without storing secrets. Webhook replay from Developer Tools must require permission and must not bypass the same validation/business-safety controls used by normal processing.

### 12.9 Outbound HTTP / SSRF Protection

Workflow HTTP/API nodes are an SSRF attack surface. The Integration Engine must validate outbound destinations and block access to sensitive local/private infrastructure by default.

At minimum, do not allow a user-configurable HTTP node to freely access:

- Loopback/localhost services
- Link-local/cloud metadata addresses
- Internal management endpoints
- Private network ranges unless explicitly allowed by a controlled deployment policy
- Unsupported URL schemes

Redirects should be revalidated, and DNS/IP resolution protections should be considered so an initially safe hostname cannot be used to reach a blocked internal address.

### 12.10 API Logging and Secret Redaction

Request/response inspection must never expose credentials. Redact sensitive headers and fields before persistence or display, including:

```text
Authorization
Cookie
Set-Cookie
X-API-Key
password
access_token
refresh_token
client_secret
signing_secret
```

Log correlation IDs, endpoint identity, status, duration, organization/integration identifiers where safe, and structured failure codes. Avoid logging full request/response bodies by default for sensitive endpoints.

### 12.11 File API Protection

Upload/download endpoints must enforce authorization, organization ownership, size limits, content policy, and safe generated storage names. See **File Upload Security** below for additional requirements.

---

## 13. Session Management

FlowOps should support:

- Idle/absolute expiration policy as appropriate
- Explicit logout
- Session invalidation after sensitive credential changes
- Security-stamp/session revocation where applicable
- Future view/revoke-active-sessions capability

A disabled user should lose access without waiting indefinitely for an old browser session to expire.

---

## 14. Login Abuse Protection

Protect authentication endpoints with:

- Rate limiting
- Account lockout/backoff
- Generic login failure messages
- Security logging without credential leakage

Avoid revealing whether an account exists through unnecessary differences in login or password-reset responses.

---

## 15. CSRF, XSS & Browser Security

Browser security requirements include:

- CSRF/antiforgery validation for state-changing cookie-authenticated requests
- Safe output encoding
- Avoiding unsafe HTML rendering
- Content Security Policy when practical
- Secure cookie settings
- HTTPS in production
- Restrictive CORS configuration

Do not treat CORS as an authorization mechanism.

---

## 16. Secrets

Never commit or expose:

- Passwords
- API keys
- Access/refresh tokens
- OAuth client secrets
- Database passwords
- Connection strings containing credentials
- Signing secrets

Development secrets should use environment variables or supported local secret storage.

Integration secrets should be encrypted at rest when reversible storage is required.

---

## 17. Logging, Debugging & Redaction

Developer Tools must never become a secret-exfiltration path.

Before debug data is persisted or returned to Studio, redact sensitive fields such as:

```text
password
authorization
apiKey
accessToken
refreshToken
clientSecret
connectionString
```

Redaction must happen server-side.

Audit data and debug telemetry remain separate:

- **Audit:** who did what and when
- **Debug:** why execution behaved or failed

---

## 18. Security Audit Events

Examples of events that should be auditable:

- Login success/failure where appropriate
- MFA enabled/disabled/reset
- Recovery-code regeneration
- Password reset/change
- User invited/removed
- Role/permission change
- API credential created/revoked
- Integration secret changed
- Organization security policy changed
- Workflow published
- Sensitive administrative action

Audit entries must never contain plaintext credentials.

---

## 19. File Upload Security

Uploaded files should be treated as untrusted.

Requirements include:

- Size limits
- Allowed content/type policy
- Generated storage names rather than trusting client filenames
- Store outside executable application directories
- Authorization checks on every download
- Malware scanning provider may be added later if required

---

## 20. Security Testing

Authentication and authorization features are not complete without negative tests.

Important tests include:

- Unauthenticated access is denied
- Wrong-role access is denied
- Cross-organization reads are denied
- Cross-organization writes are denied
- Hidden Studio APIs remain inaccessible to Portal-only users
- Expired/revoked guest links fail
- Revoked API credentials fail
- CSRF protection blocks invalid state-changing browser requests
- Secret fields are redacted in logs/debug views
- Disabled users lose access

---

## 21. RM0 Security Path

The initial security design must remain developable without paid identity products.

```text
ASP.NET Core Identity      RM0
PostgreSQL                 RM0
TOTP MFA                   RM0
Recovery codes             RM0
Secure cookies             RM0
Policy authorization       RM0
Tenant isolation           RM0
Mailpit test email         RM0
Passkeys / WebAuthn        RM0 when implemented locally
```

Third-party providers such as managed SSO/identity services may be supported later but must remain optional.

---

## 22. Security Definition of Done

A security-sensitive feature is not complete until it includes, where relevant:

- Authentication enforcement
- Authorization policy
- Tenant isolation
- Validation
- Secure error handling
- Audit behavior
- Secret redaction
- Negative tests
- Documentation updates

Security should be designed into each phase rather than postponed to a final hardening phase.
