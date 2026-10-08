# FlowOps implementation progress

Updated: 2026-10-07

## Branch and approval status

- Phase 1 was internally tested and merged into `main`.
- Phase 2 integration branch: `dev/Phase2_Form`.
- Current work branch: `feat/phase2-form-submissions`, based on the merged publishing slice (`9212aa0`).
- Submissions, history and file uploads are combined in the current work branch and remain uncommitted.
- Complete internal testing, then commit and open a PR into `dev/Phase2_Form`. Merge Phase 2 into `main` after final acceptance. No automatic commits or pushes.

## Delivery progress

| Item | Implementation | Internal acceptance / merge |
|---|---|---|
| Phase 0 project foundation and local services | Complete | Merged earlier |
| Phase 1 authentication, account separation, MFA, organizations and membership | Complete | Tested and merged into main |
| Phase 2 draft library and metadata | Complete | Tested and merged into Phase 2 |
| Phase 2 field designer and preview | Complete: eight field types, validation, stable IDs, reorder/remove | Tested and merged into Phase 2 |
| Phase 2 immutable publishing and Portal viewing | Complete: published snapshots, republishing, organization access | Tested and merged into Phase 2 |
| Phase 2 submissions | Complete: exact publication ID, server validation, duplicate request handling | Awaiting internal browser checks |
| Phase 2 submission history | Complete: Portal personal history, Studio organization history, original fields/values | Awaiting internal browser checks |
| Phase 2 local file uploads/downloads | Complete: required/optional files, validation, protected downloads | Awaiting internal browser checks |
| Phase 2 final acceptance and merge to main | Pending | After the combined slice is tested and merged |
| Phase 3 workflows and subsequent roadmap phases | Pending | Not started |

## Combined submissions and file slice

- Portal submits all eight field types, with one file per File field.
- Files are validated against the published field's extension and 1–10 MB size limit. Total files per response are limited to 20 MB.
- Allowed types: PDF, PNG, JPG/JPEG, UTF-8 TXT and CSV. Basic content signatures/text encoding are checked independently of the browser's supplied content type.
- Local storage uses generated IDs rather than user filenames for paths, outside the web folder. History displays sanitized original filenames.
- Portal download access is restricted to the response owner and active assigned organization. Studio access requires the active organization's Owner/Admin and MFA permissions.
- Responses and attachment metadata are immutable. Republished schemas do not alter existing responses or downloads.
- Retries with the same request ID and unchanged values/files return the saved response. Changed content cannot overwrite it.
- Rejected requests clean up files already written during that request. Submission, attachment and audit rows are saved together.
- Preview remains local and never submits or uploads data.

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
- Internal browser testing of the combined submissions/uploads slice is pending. Use [the Phase 2 checklist](PHASE2.md#internal-test-file-uploads-and-downloads) and the submissions checklist immediately above it.

## Remaining acceptance checks

- [ ] Submit valid responses with required and optional files, then refresh and inspect history in both sites.
- [ ] Download original files from Portal and Studio; verify names and contents.
- [ ] Reject missing/empty/oversized/disallowed files and text renamed as PDF/image.
- [ ] Test multiple files totaling more than 10 MB and up to 20 MB through Portal; reject totals over 20 MB.
- [ ] Confirm invalid responses do not leave history entries or attachments.
- [ ] Confirm Portal history/download privacy and organization switching, including membership removal.
- [ ] Confirm old response schemas and files remain unchanged after republishing.
- [ ] Verify every field type and normal account flows with the API running over local HTTPS.
- [ ] Review changes, commit with user approval, and merge the combined slice into `dev/Phase2_Form`.
- [ ] Finish Phase 2 acceptance before merging into `main`.

## Current limitations

- File validation checks basic type signatures and encoding; full document/image parsing and malware scanning are not implemented.
- Storage is local to the API host. Keep its directory persistent and back it up with PostgreSQL.
- An abrupt process/host failure between file writes and database commit can leave unreferenced files; automatic crash recovery cleanup is not implemented.
- Workflow execution, approvals and workflow integration belong to later phases.
