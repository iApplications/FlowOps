# FlowOps implementation progress

Updated: 2026-10-08

## Branch and approval status

- Phase 1 was internally tested and merged into `main`.
- Phase 2 integration branch: `dev/Phase2_Form`.
- Submissions, history and file uploads were accepted and merged into `dev/Phase2_Form` (PR #9, `7d9ec68`).
- Current work branch: `fix/review-hardening`, based on `dev/Phase2_Form`. It contains fixes from the 2026-10-08 code review of Phase 1 and Phase 2 (see below).
- Browser spot-check of the hardening changes passed (user-reported, 2026-10-08). Next: merge the PR into `dev/Phase2_Form`, then complete final Phase 2 acceptance and merge into `main`. No automatic commits or pushes.

## Delivery progress

| Item | Implementation | Internal acceptance / merge |
|---|---|---|
| Phase 0 project foundation and local services | Complete | Merged earlier |
| Phase 1 authentication, account separation, MFA, organizations and membership | Complete | Tested and merged into main |
| Phase 2 draft library and metadata | Complete | Tested and merged into Phase 2 |
| Phase 2 field designer and preview | Complete: eight field types, validation, stable IDs, reorder/remove | Tested and merged into Phase 2 |
| Phase 2 immutable publishing and Portal viewing | Complete: published snapshots, republishing, organization access | Tested and merged into Phase 2 |
| Phase 2 submissions | Complete: exact publication ID, server validation, duplicate request handling | Accepted 2026-10-08; merged into Phase 2 (#9) |
| Phase 2 submission history | Complete: Portal personal history, Studio organization history, original fields/values | Accepted 2026-10-08; merged into Phase 2 (#9) |
| Phase 2 local file uploads/downloads | Complete: required/optional files, validation, protected downloads | Accepted 2026-10-08; merged into Phase 2 (#9) |
| Code review hardening (Phase 1 + Phase 2) | Complete on `fix/review-hardening` | Automated, API and browser acceptance passed 2026-10-08; PR into Phase 2 pending |
| Phase 2 final acceptance and merge to main | Pending | After the hardening branch is merged |
| Phase 3 workflows and subsequent roadmap phases | Pending | Not started |

## Combined submissions and file slice

- Portal submits all eight field types, with one file per File field.
- Files are validated against the published field's extension and 1–10 MB size limit. Total files per response are limited to 20 MB.
- Allowed types: PDF, PNG, JPG/JPEG, UTF-8 TXT and CSV. Basic content signatures/text encoding are checked independently of the browser's supplied content type.
- Local storage uses generated IDs rather than user filenames for paths, outside the web folder. History displays sanitized original filenames.
- Portal download access is restricted to the response owner and active assigned organization. Studio access requires the active organization's Owner/Admin and MFA permissions.
- Responses and attachment metadata are immutable. Republished schemas do not alter existing responses or downloads.
- New responses must target the latest published version. A page opened before republishing gets 409 `form_version_outdated` and must reload.
- Retries with the same request ID and unchanged values/files return the saved response, even after republishing, without writing files again. Changed content cannot overwrite it.
- Rejected requests clean up files already written during that request. Submission, attachment and audit rows are saved together.
- Preview remains local and never submits or uploads data.

## Code review hardening (2026-10-08)

A review of the Phase 2 changes and the Phase 1 authentication/organization code found no tenant-isolation, CSRF or file-path problems. It led to these changes:

- **Login abuse:** wrong passwords on unconfirmed accounts now count toward lockout. Before, they were unlimited and the 403/401 difference revealed a correct password. A successful password reset clears lockout.
- **Rate limiting behind the proxy:** the API can trust forwarded client addresses (`ForwardedHeaders:Enabled`, off by default). The auth limit is configurable (`RateLimiting:AuthPermitLimit`). Production must place an edge proxy in front of Next.js; see [Security §12.4](SECURITY.md#124-rate-limiting-and-abuse-protection).
- **Registration:** the account, its Studio signup claim and the audit event are committed in one transaction.
- **Submissions:** only the latest published version is accepted. Retries are answered before files are written. JSON and file submissions share one fingerprint/retry scheme.
- **Portal form:** after any server response other than a timeout or network failure, the next submit uses a new request ID, so a 409 no longer leaves the form stuck until reload.
- **Form metadata:** `PUT /api/forms/{id}` now requires `draftRevision` and checks it before changing anything.
- **Publishing:** a draft identical to the latest published version (name, description and fields) can no longer be republished (409 `no_changes`). Identical versions added nothing, and under the latest-version rule they forced open Portal pages to reload. Form responses include `hasUnpublishedChanges`; the designer disables Publish and explains why when nothing has changed.
- **Uploads:** content is validated while streaming instead of re-reading the file; UTF-8 characters split across buffers are handled; empty folders are removed after rejections; allowed file types are defined once.
- **Efficiency:** the security stamp is revalidated every minute (session revocation is still checked per request), the browser reuses CSRF tokens per site, Studio pages load their two API calls in parallel, and member-management endpoints no longer repeat the policy checks.
- **Development:** the default Visual Studio launch profile again listens on `http://localhost:5050`, with a separate HTTPS profile. A Studio home page crash when the API drops mid-load was fixed. The surface restriction on the role-change endpoint is documented.

Verification: 25 backend tests (4 new, others extended), lint, typecheck and production builds pass. The API acceptance script passed **72 of 72** checks against the HTTPS stack, including stale-version rejection, post-republish retries and the refused unchanged republish. The over-20 MB proxy case counts as passing because the request is rejected and nothing is saved.

## Database and storage

| Migration | Purpose |
|---|---|
| `FormDraftFoundation` | Organization-scoped drafts |
| `DraftFormFields` | Saved field schema and draft revision |
| `FormPublishing` | Immutable published snapshots |
| `FormSubmissions` | Responses linked to user, organization and publication |
| `FormAttachments` | File metadata and request fingerprints for upload retries |

These migrations are applied to the local PostgreSQL development database. Rebuild/restart the API to load the new endpoints. Restart Portal after its upload proxy configuration change.

Development storage defaults to `%LOCALAPPDATA%\FlowOps\Uploads`. Override with `Storage:RootPath` in environment configuration or `Storage__RootPath` as an environment variable. Back up this folder together with the database; attachment bytes are not stored in PostgreSQL.

## Verification record

2026-10-07: `dotnet test FlowOps.slnx -c Release -p:UseAppHost=false --no-restore` passed **21 tests**. `npm.cmd run lint`, `npm.cmd run typecheck` and `npm.cmd run build` passed for both web apps. `FormAttachments` was applied successfully to local PostgreSQL.

- Automated backend checks cover authentication, CSRF, MFA, account separation, tenant isolation, all field types, publication immutability, response validation, history privacy and duplicate requests.
- Attachment checks cover allowed types, required files, zero bytes, invalid extensions, invalid contents, size limits, unknown/duplicate File fields, cleanup after rejection, filename path sanitization, retries and authorized downloads.
- The API accepts a valid 12 MB response spread across two files and rejects a response totaling more than 20 MB. The Portal proxy configuration allows upload bodies up to 21 MB including multipart overhead; confirm that route in the browser checklist.
- Access checks include anonymous users, another Portal user, another organization and revoked membership; historical files remain available after republishing.
- Lint, TypeScript checks and production builds for Studio and Portal pass.
2026-10-08: tests (21), lint, typecheck and production builds re-run and passed on `926ecf2`. An API-level acceptance script then ran against the live local HTTPS stack (PostgreSQL, API on 5443, Studio/Portal proxies on 3443/3444, Mailpit). It used fresh Studio Owner (TOTP MFA), Portal and second-organization accounts: **66 of 67 checks passed**.

- Passed: all eight field types; PDF/PNG/JPG/JPEG/TXT/CSV uploads; byte-identical downloads from Portal and Studio with attachment/original-filename, no-store and nosniff headers; 21 rejection cases (missing/zero-byte/disallowed/oversized files, text renamed as PDF/PNG/JPG, non-UTF-8 text, mixed good+bad files, duplicate/unknown File fields, fabricated attachment references, and every value-validation rule), with no history rows or stored files left behind; 13 MB and exactly 20 MB totals accepted through the Portal proxy; idempotent retries (same content returns the saved response; changed file, changed filename or another user's reuse returns 409); history/detail/download privacy for another Portal user, anonymous users, another organization, Portal sessions on Studio APIs, and removed members; Studio organization switching; republishing with a File field removed/changed keeps the old schema and attachments, and a page still showing v1 submits against v1.
- Not passed as written: a 21 MB total sent through the Portal proxy is rejected without saving anything, but the client receives a dropped connection instead of the API's "Files must total 20 MB or less" response. The Next.js proxy aborts when the API answers before reading the full body (the same happens for an unauthenticated upload that gets a 401). Browser users are not affected: the Portal form blocks totals over 20 MB and oversized files before uploading. Accepted as a known limitation.
- The run left test accounts `p2-*-muywls0e@flowops.test`, organizations `P2 Acceptance A/B muywls0e` and ~34 MB of attachments in local development storage.

## Remaining acceptance checks

Server behavior is covered by the 2026-10-08 API acceptance run. On 2026-10-08 the user reported that the visual browser checks below passed over local HTTPS.

- [x] Portal over HTTPS: submit a form with all eight field types and files; history appears below the form and survives refresh; Download saves the original file.
- [x] Portal: invalid values and bad files (wrong type, too large, totals over 20 MB) show visible feedback; inputs and the button are disabled while a submission is in flight.
- [x] Studio: submission history under the designer shows submitter/version; response detail shows original labels/values and downloads attachments.
- [x] Studio Preview: choosing a file and checking validation creates no response or stored attachment.
- [ ] Merge the PR from `feat/phase2-form-submissions` into `dev/Phase2_Form`.
- [ ] Finish Phase 2 acceptance before merging into `main`.

## Current limitations

- File validation checks basic type signatures and encoding; full document/image parsing and malware scanning are not implemented.
- Storage is local to the API host. Keep its directory persistent and back it up with PostgreSQL.
- An abrupt process/host failure between file writes and database commit can leave unreferenced files; automatic crash recovery cleanup is not implemented.
- Through the Next.js proxy, an upload the API rejects before reading the body (over 20 MB total, or an expired session) reaches non-browser clients as a dropped connection or a proxy 500 rather than a JSON error (nothing is saved). The Portal form checks sizes before uploading. An expired Portal session during an upload shows a generic network error instead of a sign-in prompt.
- Workflow execution, approvals and workflow integration belong to later phases.
