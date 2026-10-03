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

Draft library, field designer/preview, immutable publishing and Portal viewing are implemented. Current work branch: `feat/phase2-form-publishing`. Submissions/history and file storage are still pending. Preview validates inputs locally and does not upload files or submit data. Portal renders the latest published version read-only until submissions are implemented.

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
2. Sign in with a separate Portal account assigned to that organization. Open Forms from the header: only published forms appear. Open the form and check its name, description, field order, settings and version number. Inputs are read-only; submissions/uploads come next.
3. Edit and save the Studio draft's name or fields. Refresh Portal: version 1 must remain unchanged.
4. Reload the designer after editing metadata, then publish version 2. Portal now shows version 2, with one entry for the form. Earlier versions remain stored unchanged.
5. Open a draft in two Studio tabs. Publish or save in one, then try publishing from the stale tab. It must show an error and require reloading.
6. Switch organizations in Portal through Overview. Forms must follow the active organization; a direct URL to another organization's form must be unavailable. Unpublished draft URLs must also be unavailable.
7. Portal cannot edit or publish drafts. Studio publishing requires Owner/Admin access and the organization's MFA policy.

Automated backend checks verify snapshots survive draft edits, republishing preserves previous versions, stale revisions are rejected, published data cannot be modified through the context, and surface/organization boundaries hold. Run internal browser checks before committing this branch to `dev/Phase2_Form`.
