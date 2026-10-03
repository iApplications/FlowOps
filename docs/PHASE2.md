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

## Current first slice

Draft library is implemented; the field designer, publishing, Portal rendering, submissions/history and file storage are still pending. Form metadata can be edited; there is no publish or submit action in this slice.

## Internal test: draft library

- Sign in to Studio as an Owner/Admin with MFA satisfied.
- Open Forms from the Studio sidebar (`/forms`); Overview contains workspace/account management only. Create a draft, edit its name and description, and refresh to check persistence.
- Switch organizations: the draft must appear only in its owning organization.
- Portal accounts and Studio sessions without required MFA cannot access the draft API.
- Empty/long names and long descriptions are rejected; create/update append audit metadata.

Apply the `FormDraftFoundation` EF migration before testing against PostgreSQL. Restart the API to load the new endpoints.
