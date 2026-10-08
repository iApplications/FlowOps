export type PlatformHealth = {
  status: "Healthy" | "Degraded" | "Unhealthy";
  service: string;
};

export type OrganizationSummary = {
  organizationId: string;
  name: string;
  role: "Owner" | "Admin" | "Member";
  requireMfaForPrivileged: boolean;
};

export type CurrentUser = {
  id: string;
  email: string;
  accountSurface: "Studio" | "Portal";
  displayName: string;
  twoFactorEnabled: boolean;
  canCreateWorkspace: boolean;
  mfaSatisfied: boolean;
  expiresAt: string;
  activeOrganization: OrganizationSummary | null;
};
export type FormFieldType = "Text" | "Textarea" | "Number" | "Currency" | "Date" | "Dropdown" | "Checkbox" | "File";
export type FormField = {
  id: string; type: FormFieldType; label: string; required: boolean; helpText?: string | null;
  maxLength?: number | null; min?: number | null; max?: number | null;
  minDate?: string | null; maxDate?: string | null; options?: string[] | null;
  maxFileSizeMb?: number | null; acceptedExtensions?: string[] | null;
};
export type FormDraft = { id: string; name: string; description: string; fields: FormField[]; draftRevision: number; publicationCount: number };
export type PublishedForm = { id: string; formId: string; name: string; description: string; fields: FormField[]; versionNumber: number; publishedAt: string };
export type SubmissionValues = Record<string, string | number | boolean | null>;
export type SubmissionSummary = { id: string; publicationId: string; versionNumber: number; name: string; submittedAt: string; submittedBy?: string | null; email?: string | null };
export type SubmissionHistoryPage = { items: SubmissionSummary[]; total: number; page: number; pageSize: number };
export type SubmissionFile = { id: string; fieldId: string; fileName: string; size: number };
export type SubmissionDetail = { submission: { id: string; submittedAt: string; values: SubmissionValues }; form: PublishedForm; files: SubmissionFile[] };
