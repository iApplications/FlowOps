# FlowOps implementation progress

Updated: 2026-10-08

## Branch and approval status

- Phase 1 was internally tested and merged into `main`.
- Phase 2 integration branch: `dev/Phase2_Form`. All Phase 2 work slices are merged into it: draft library (#6), field designer (#7), publishing (#8), submissions/history/uploads (#9), and code review hardening (#10).
- Phase 2 is **implementation complete**. Remaining: the final Phase 2 acceptance below, then a PR from `dev/Phase2_Form` into `main`.
- Next phase: Phase 3 Workflow Designer, on a new integration branch from `main` after the Phase 2 merge.
- No automatic commits or pushes; each merge follows internal testing and user approval.

## Delivery progress

| Item | Implementation | Internal acceptance / merge |
|---|---|---|
| Phase 0 project foundation and local services | Complete | Merged earlier |
| Phase 1 authentication, account separation, MFA, organizations and membership | Complete | Tested and merged into main |
| Phase 2 draft library and metadata | Complete | Tested; merged into Phase 2 (#6) |
| Phase 2 field designer and preview | Complete: eight field types, validation, stable IDs, reorder/remove | Tested; merged into Phase 2 (#7) |
| Phase 2 immutable publishing and Portal viewing | Complete: published snapshots, republishing, organization access, no identical republish | Tested; merged into Phase 2 (#8, #10) |
| Phase 2 submissions | Complete: latest published version only, server validation, duplicate request handling | Accepted 2026-10-08; merged into Phase 2 (#9, #10) |
| Phase 2 submission history | Complete: Portal personal history, Studio organization history, original fields/values | Accepted 2026-10-08; merged into Phase 2 (#9) |
| Phase 2 local file uploads/downloads | Complete: required/optional files, streaming validation, protected downloads | Accepted 2026-10-08; merged into Phase 2 (#9, #10) |
| Code review hardening (Phase 1 + Phase 2) | Complete | Automated, API and browser acceptance passed 2026-10-08; merged into Phase 2 (#10) |
| Phase 2 final acceptance and merge to main | Pending | See [Final Phase 2 acceptance](#final-phase-2-acceptance) |
| Phase 3 workflows and subsequent roadmap phases | Pending | Not started |

## Phase 2 roadmap deliverables

| Deliverable ([Roadmap](ROADMAP.md#phase-2--form-builder)) | Delivered by |
|---|---|
| Create / edit form | Studio Forms library: organization-scoped drafts with name/description and revision checks |
| Form field designer, eight initial field types | Designer: Text, Textarea, Number, Currency, Date, Dropdown, Checkbox, File; add/remove/reorder, limits and options |
| Field validation | Schema rules in the designer and API; value rules in preview, Portal and authoritatively on the API |
| Form preview | Same renderer as Portal; local only, never submits or uploads |
| Form submission | Portal submissions with file attachments, validated against the latest published version |
| Submission history | Portal: own responses. Studio: organization responses with submitter and version. Both show original labels/values and allow authorized downloads |
| Render published forms in Portal | Portal Forms lists the latest published version of each form in the active organization |

## Behavior summary

- Publishing freezes an immutable snapshot (name, description, fields). Later draft edits never change published versions or historical responses. A draft identical to the latest version cannot be republished (409 `no_changes`); the designer disables Publish until a saved change exists.
- New responses must target the latest published version. A page opened before republishing gets 409 `form_version_outdated` and must reload.
- Portal submits all eight field types, with one file per File field. Files must match the field's extensions and 1–10 MB limit; all files together are limited to 20 MB.
- Allowed types: PDF, PNG, JPG/JPEG, UTF-8 TXT and CSV. Content signatures and UTF-8 text are validated while streaming, independently of the browser's content type.
- Local storage uses generated IDs outside the web folder; history shows sanitized original filenames.
- Portal download access is limited to the response owner in the active assigned organization. Studio requires the active organization's Owner/Admin role and MFA policy.
- Responses and attachment metadata are immutable. Retries with the same request ID and unchanged values/files return the saved response, even after republishing, without writing files again. Changed content returns 409.
- Rejected requests remove any files written during the request. Submission, attachment and audit rows are saved together.

## Code review hardening (2026-10-08, #10)

A review of the Phase 2 changes and the Phase 1 authentication/organization code found no tenant-isolation, CSRF or file-path problems. It led to these changes:

- **Login abuse:** wrong passwords on unconfirmed accounts count toward lockout. Before, they were unlimited and the 403/401 difference revealed a correct password. A successful password reset clears lockout.
- **Rate limiting behind the proxy:** the API can trust forwarded client addresses (`ForwardedHeaders:Enabled`, off by default). The auth limit is configurable (`RateLimiting:AuthPermitLimit`). Production must place an edge proxy in front of Next.js; see [Security §12.4](SECURITY.md#124-rate-limiting-and-abuse-protection).
- **Registration:** the account, its Studio signup claim and the audit event are committed in one transaction.
- **Submissions:** only the latest published version is accepted. Retries are answered before files are written. JSON and file submissions share one fingerprint/retry scheme.
- **Publishing:** identical republishing is refused; form responses include `hasUnpublishedChanges`.
- **Portal form:** after any server response other than a timeout or network failure, the next submit uses a new request ID, so a 409 no longer leaves the form stuck until reload.
- **Form metadata:** `PUT /api/forms/{id}` requires `draftRevision` and checks it before changing anything.
- **Uploads:** streaming content validation (including UTF-8 split across buffers), empty-folder cleanup, and one shared list of allowed extensions.
- **Efficiency:** the security stamp is revalidated every minute (sessions are still checked per request); CSRF tokens are reused per site; Studio pages load data in parallel; member endpoints no longer repeat policy checks.
- **Development:** the default Visual Studio profile listens on `http://localhost:5050`, with a separate HTTPS profile. A Studio home page crash when the API drops mid-load is fixed. The role-change endpoint's surface restriction is documented.

## Database and storage

| Migration | Purpose |
|---|---|
| `FormDraftFoundation` | Organization-scoped drafts |
| `DraftFormFields` | Saved field schema and draft revision |
| `FormPublishing` | Immutable published snapshots |
| `FormSubmissions` | Responses linked to user, organization and publication |
| `FormAttachments` | File metadata and request fingerprints for upload retries |

All migrations are applied to the local PostgreSQL development database. The review hardening added no migrations.

Development storage defaults to `%LOCALAPPDATA%\FlowOps\Uploads`. Override with `Storage:RootPath` in environment configuration or `Storage__RootPath` as an environment variable. Back up this folder together with the database; attachment bytes are not stored in PostgreSQL.

## Verification record

- **2026-10-07:** 21 backend tests, lint, typecheck and production builds passed. `FormAttachments` was applied to local PostgreSQL.
- **2026-10-08, submissions slice (`926ecf2`):** an API-level acceptance script ran against the live local HTTPS stack (PostgreSQL, API on 5443, Studio/Portal proxies on 3443/3444, Mailpit). It used fresh Studio Owner (TOTP MFA), Portal and second-organization accounts: 66 of 67 checks passed. The exception was the proxy limitation listed below. Browser acceptance passed (user-reported).
- **2026-10-08, review hardening (`d654140`, merged as #10):** 25 backend tests (4 new, others extended), lint, typecheck and production builds passed. The acceptance script passed **72 of 72** checks, covering:
  - all eight field types and all allowed file types, with byte-identical downloads and safe download headers
  - 21 rejection cases that leave no responses or files behind
  - 13 MB and exactly 20 MB totals through the Portal proxy
  - idempotent retries
  - privacy for other users, anonymous users, other organizations, Portal sessions on Studio APIs and removed members
  - Studio organization switching
  - republishing that keeps old schemas and files
  - stale-version rejection and refused identical republishing

  Browser spot-checks of publishing, CSRF token refresh and the stale-page reload message passed (user-reported).
- Automated backend tests use in-memory SQLite and an isolated temporary storage folder; see [Development](DEVELOPMENT.md#checks).
- **Test data:** on 2026-10-08, all acceptance-script data was removed from the local database, upload storage and Mailpit: 20 `p2-*@flowops.test` accounts, 10 `P2 Acceptance` organizations and their forms, submissions, attachments, sessions and audit events, plus about 133 MB of uploads. No other accounts or organizations were changed.

## Final Phase 2 acceptance

The submissions/uploads slice and the review hardening are accepted. Before merging `dev/Phase2_Form` into `main`, run one end-to-end pass on the merged branch:

- [x] Backend tests (25), lint, typecheck and production builds pass on the merged `dev/Phase2_Form` code (`8c7f447`, 2026-10-08).
- [ ] Studio Forms library: create a draft, rename it and edit its description, refresh, and check that a stale tab's rename is rejected. Revision checks became mandatory in #10.
- [ ] Studio designer: add fields, preview, save, publish. Publish is then disabled until a saved change exists.
- [ ] Portal: the form appears, accepts a submission with a file, and history and downloads work in Portal and Studio.
- [ ] Review the docs and approve the PR from `dev/Phase2_Form` into `main`.

## Current limitations

- File validation checks basic type signatures and UTF-8 text; full document/image parsing and malware scanning are not implemented.
- Storage is local to the API host. Keep its directory persistent and back it up with PostgreSQL.
- An abrupt process/host failure between file writes and database commit can leave unreferenced files; automatic crash recovery cleanup is not implemented.
- Through the Next.js proxy, an upload the API rejects before reading the body (over 20 MB total, or an expired session) reaches non-browser clients as a dropped connection or a proxy 500 rather than a JSON error. Nothing is saved, and the Portal form checks sizes before uploading. An expired Portal session during an upload shows a generic network error instead of a sign-in prompt.
- After a branch switch or pull that touches `next.config.ts`, restart the Next.js dev servers; an automatic restart can drop the 21 MB upload proxy setting (see [Development](DEVELOPMENT.md#form-attachment-storage)).
- Production deployment must provide an edge proxy that sets `X-Forwarded-For` before enabling forwarded headers; until then, all browser clients share one authentication rate-limit bucket.
- Workflow execution, approvals and workflow integration belong to later phases.
