# Phase 2: Form Builder

## Branches

Phase 2 integration branch: `dev/Phase2_Form`. Create work branches from it and target their PRs back to it. Merge to `main` after Phase 2 implementation, internal testing and approval. Do not commit automatically.

First work branch: `feat/phase2-form-foundation`.

## Delivery sequence

1. Draft library: Studio creates, lists and edits form names/descriptions within the active organization. Persist draft identities, apply Owner/Admin and MFA checks, and record audit events.
2. Field designer and preview: stable field IDs; Text, Textarea, Number, Currency, Date, Dropdown, Checkbox and File. Add/remove/reorder fields and configure labels, required rules, options and limits. Use the same renderer for preview and Portal.
3. Publishing: explicitly publish a validated immutable schema snapshot. Later draft edits must not change published forms or historical submissions. Portal lists only published forms in assigned organizations.
4. Submissions: validate all values on the server against the exact published schema; store its identity with submission data. Portal users view their own submission history; Studio managers view organization submissions.
5. Files and acceptance: authenticated local storage with size/type limits, safe generated filenames, organization ownership checks and authorized downloads. Verify every initial field, validation, publication, history and tenant boundary.

## Current implementation

Draft library, field designer/preview, immutable publishing, Portal submissions, submission history and local file uploads/downloads are implemented. Current work branch: `feat/phase2-form-submissions` (committed as `926ecf2`). Server-side acceptance of submissions and uploads passed on 2026-10-08. Browser acceptance passed on the same day; the slice is ready to merge into `dev/Phase2_Form`. Preview validates inputs locally and does not submit or upload data. See [Progress](PROGRESS.md) for implementation, test and merge status.

## Internal test: draft library

- Sign in to Studio as an Owner/Admin with MFA satisfied.
- Open Forms from the Studio sidebar (`/forms`); Overview contains workspace/account management only. Create a draft, edit its name and description, and refresh to check persistence.
- Switch organizations: the draft must appear only in its owning organization.
- Portal accounts and Studio sessions without required MFA cannot access the draft API.
- Empty/long names and long descriptions are rejected; create/update append audit metadata.

Apply the `FormDraftFoundation` EF migration before testing against PostgreSQL. Restart the API to load the new endpoints.

## Internal test: field designer and preview

Apply the `DraftFormFields` migration and restart the API. Existing drafts start with no fields.

1. Open Forms, select a draft and click Design fields.
2. Add all eight field types. Configure labels, help text and required flags.
3. Configure text length, number/currency min/max, date bounds, dropdown options and allowed file types/size.
4. Move fields up/down and remove one. Save, refresh and verify the order, settings and remaining fields persist.
5. Preview: use Check validation with missing required values, out-of-range numbers/dates and valid values. Files exceeding the size/type limits are rejected locally; no upload occurs.
6. Save invalid rules (minimum greater than maximum, duplicate/blank dropdown options or disallowed file types) and verify visible errors.
7. Open the same draft in two tabs. Save changes in one, then save from the other: the stale save must be rejected without overwriting the first tab's changes. Reload the stale tab to continue.
8. Leave with unsaved changes: verify the warning and that choosing to stay preserves edits.
9. Change the active organization and attempt to reopen another organization's designer: access must be denied. Portal accounts cannot read or save drafts.

Automated checks cover all field types, IDs/order/removal persistence, invalid rules, stale saves, MFA, Portal restrictions and organization boundaries. Internal browser testing is still required before commit.

## Internal test: publishing and Portal viewing

Apply the `FormPublishing` migration and restart the API. Existing forms remain unpublished.

1. In Studio, open a saved form with at least one field. Publish version 1 and confirm the action. Empty forms and forms with unsaved field changes cannot be published.
2. Sign in with a separate Portal account assigned to that organization. Open Forms from the header: only published forms appear. Open the form and check its name, description, field order, settings and version number. Inputs support submission and file uploads.
3. Edit and save the Studio draft's name or fields. Refresh Portal: version 1 must remain unchanged.
4. Reload the designer after editing metadata, then publish version 2. Portal now shows version 2, with one entry for the form. Earlier versions remain stored unchanged.
5. Open a draft in two Studio tabs. Publish or save in one, then try publishing from the stale tab. It must show an error and require reloading.
6. Switch organizations in Portal through Overview. Forms must follow the active organization; a direct URL to another organization's form must be unavailable. Unpublished draft URLs must also be unavailable.
7. Portal cannot edit or publish drafts. Studio publishing requires Owner/Admin access and the organization's MFA policy.

Automated backend checks verify snapshots survive draft edits, republishing preserves previous versions, stale revisions are rejected, published data cannot be modified through the context, and surface/organization boundaries hold. Run internal browser checks before committing this branch to `dev/Phase2_Form`.

## Internal test: submissions and history

Apply the `FormSubmissions` migration and rebuild/restart the API. The local development database has this migration applied.

1. Publish a form containing all eight field types. Use required rules and limits; include required and optional File fields.
2. Portal: open the form, try missing required values, excessive text, out-of-range numbers/dates and an unchecked required checkbox. Valid values should submit successfully; history appears below the form. Refresh the page and open the saved response.
3. Studio: open the same form's designer. Submission history below it shows organization responses with the submitter and published version. Open a response to inspect its original field labels and values.
4. Sign in with a second assigned Portal account. It must see only its own history. Trying another user's submission detail API URL must return 404. Studio managers can see both users' responses.
5. Edit fields/name and publish a new version. Existing history must keep the old title, labels and values. A Portal page already open before republishing submits against the version it displayed; refreshing loads the newest version.
6. Switch organizations and retry form/history URLs: submissions, history and details from another organization must be unavailable. Removing a Portal membership must also prevent access on the next request.
7. During a submission the button and inputs are disabled. Repeating the same request ID with the same response returns the original submission, without adding another row or audit event. Changed values with an already saved request ID are rejected.
8. Optional File fields can be left empty. Required File fields must contain a valid file. The server rejects missing required files and fabricated references to existing attachments.

History is paginated at 25 rows. Submissions are immutable, store the publication ID and are validated against that snapshot on the server. Audits record submission metadata, not answer values. No workflow execution or approval is started in this slice.

Automated checks cover all value types, invalid values/unknown fields, CSRF, duplicate requests, exact version retention, history privacy and organization boundaries. Internal browser testing remains required before commit.

## Internal test: file uploads and downloads

Apply `FormAttachments` after `FormSubmissions`, rebuild/restart the API and restart Portal to load its proxy configuration. Both migrations are applied to the local development database.

1. In Studio, add required and optional File fields. Choose allowed extensions and a size limit of 1–10 MB, then save and publish.
2. Portal: submit a valid PDF, PNG, JPG/JPEG, UTF-8 TXT or CSV permitted by the field. One file can be selected per field. Refresh and open the response; Download should save the original bytes with the original base filename.
3. Studio: open the same response in submission history and download its attachments.
4. Test a missing required file, a zero-byte file, an unapproved extension, a file over the field limit, and a text file renamed to `.pdf`/`.png`/`.jpg`. Each must fail with visible feedback. Optional fields can be left empty.
5. With multiple File fields, verify total files over 20 MB are rejected. Verify more than 10 MB in total can succeed when individual fields allow it and total files remain at most 20 MB.
6. Submit several files together; if one fails validation, no response or attachment should appear. Correct the problem and retry.
7. Repeat the same request with unchanged values/files: history must contain one response. A changed file or filename with an already saved request ID must be rejected rather than overwrite the response.
8. A second Portal user, an anonymous user, a user switched to another organization, and a user whose membership was removed must not be able to download the attachment. Studio requires its existing Owner/Admin and MFA permissions.
9. Republish with the File field removed or changed. Existing responses must retain their original files and downloads.
10. Studio Preview remains local: selecting a file and checking validation creates no response or stored attachment.

Files use generated paths outside the public web folder. Only authenticated download endpoints expose them, with attachment disposition and no-store/nosniff headers. Validation checks extension, size, basic PDF/image signatures and UTF-8 text encoding; it does not fully parse documents/images or perform malware scanning.
