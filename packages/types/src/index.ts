export type PlatformHealth = {
  status: "Healthy" | "Degraded" | "Unhealthy";
  service: string;
};

export type OrganizationRoleItem = {
  id: string | null;
  name: string;
  baseRole: "Owner" | "Admin" | "Member";
  isBuiltIn: boolean;
  isActive: boolean;
  memberCount: number | null;
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
  // Name used by workflow conditions (request.<key>); generated from the label when saved without one.
  key?: string | null;
};
export type FormDraft = { id: string; name: string; description: string; fields: FormField[]; draftRevision: number; publicationCount: number; hasUnpublishedChanges?: boolean | null };
export type PublishedForm = { id: string; formId: string; name: string; description: string; fields: FormField[]; versionNumber: number; publishedAt: string };
export type SubmissionValues = Record<string, string | number | boolean | null>;
export type SubmissionSummary = { id: string; publicationId: string; versionNumber: number; name: string; submittedAt: string; submittedBy?: string | null; email?: string | null };
export type SubmissionHistoryPage = { items: SubmissionSummary[]; total: number; page: number; pageSize: number };
export type SubmissionFile = { id: string; fieldId: string; fileName: string; size: number };
export type SubmissionDetail = { submission: { id: string; submittedAt: string; values: SubmissionValues }; form: PublishedForm; files: SubmissionFile[] };
export type WorkflowNodeType = "Start" | "Condition" | "Calculation" | "Approval" | "End";
export type WorkflowNodeConfig = {
  trigger?: "Manual" | "FormSubmitted" | null; formId?: string | null; expression?: string | null;
  assigneeRole?: string | null; instructions?: string | null; outcome?: string | null;
  assignments?: { variable: string | null; expression: string | null }[] | null;
};
export type WorkflowVariable = { name: string; type: "Number" | "Text" | "Boolean"; initialValue?: string | null };
export type WorkflowNode = { id: string; type: WorkflowNodeType; label: string; position: { x: number; y: number }; config?: WorkflowNodeConfig | null };
export type WorkflowConnection = { id: string; source: string; sourceHandle: string; target: string };
export type WorkflowGraph = { schemaVersion: number; nodes: WorkflowNode[]; connections: WorkflowConnection[]; variables?: WorkflowVariable[] | null };
export type WorkflowSummary = {
  id: string; name: string; description: string; revision: number; createdAt: string; updatedAt: string;
  draftVersionNumber: number | null; publishedVersions: number; latestPublishedVersionNumber: number | null; issueCount: number | null;
};
export type WorkflowIssue = { code: string; message: string; nodeId?: string | null };
export type WorkflowDraftVersion = { id: string; versionNumber: number; status: "Draft"; draftRevision: number; graph: WorkflowGraph; issues: WorkflowIssue[]; hasUnpublishedChanges: boolean; updatedAt: string };
export type WorkflowPublishedVersion = { id: string; versionNumber: number; publishedAt: string };
export type WorkflowDetail = Omit<WorkflowSummary, "draftVersionNumber" | "issueCount" | "latestPublishedVersionNumber">
  & { draft: WorkflowDraftVersion | null; latestPublished: WorkflowPublishedVersion | null };
export type WorkflowRunStatus = "Running" | "Waiting" | "Completed" | "Failed" | "Cancelled";
export type WorkflowRunSummary = {
  id: string; versionNumber: number; status: WorkflowRunStatus; outcome: string | null; trigger: "Manual" | "FormSubmitted"; waitingNodeId: string | null;
  startedBy: string | null; startedAt: string; completedAt: string | null; errorCode: string | null; errorMessage: string | null; correlationId: string;
};
export type WorkflowRunPage = { items: WorkflowRunSummary[]; total: number; page: number; pageSize: number };
export type WorkflowRunStep = {
  id: string; sequence: number; nodeId: string; nodeType: WorkflowNodeType; status: "Running" | "Waiting" | "Completed" | "Failed"; attempt: number;
  input: unknown; output: unknown; durationMs: number; errorCode: string | null; errorMessage: string | null; startedAt: string; completedAt: string | null;
};
export type WorkflowRunExecution = {
  id: string; sequence: number; reason: "Start" | "Resume"; status: "Running" | "Waiting" | "Completed" | "Failed"; triggeredBy: string | null;
  correlationId: string; startedAt: string; completedAt: string | null; errorCode: string | null; errorMessage: string | null; steps: WorkflowRunStep[];
};
export type WorkflowRunDetail = Omit<WorkflowRunSummary, "versionNumber"> & {
  workflow: { id: string; name: string }; version: { id: string; versionNumber: number; publishedAt: string; graph: WorkflowGraph };
  sourceSubmissionId: string | null; variables: Record<string, unknown>; executions: WorkflowRunExecution[]; approvals: WorkflowRunApproval[];
};

export type ApprovalTaskStatus = "Pending" | "Approved" | "Rejected" | "ChangesRequested" | "Cancelled";
export type ApprovalTaskSummary = {
  id: string; status: ApprovalTaskStatus; assigneeRole: string; instructions: string | null; createdAt: string; decidedAt: string | null;
  workflow: string; requester: string | null;
};
export type ApprovalTaskPage = { items: ApprovalTaskSummary[]; total: number; page: number; pageSize: number };
export type ApprovalTaskEvent = {
  id: string; kind: "Created" | "Approved" | "Rejected" | "ChangesRequested" | "Resubmitted" | "Commented"; actor: string | null; comment: string | null; createdAt: string;
};
export type ApprovalTaskDetail = {
  id: string; status: ApprovalTaskStatus; assigneeRole: string; instructions: string | null; revision: number; createdAt: string; decidedAt: string | null;
  workflow: string; workflowVersion: number; instanceId: string; requester: string | null;
  request: Record<string, unknown> | null; submission: Record<string, unknown> | null; canDecide: boolean; history: ApprovalTaskEvent[];
  // Set for the requester while changes are requested: the form fields (with request keys) they can edit and resubmit.
  canResubmit: boolean; fields: FormField[] | null;
};
export type ApprovalTaskDecision = "approve" | "reject" | "request_changes";
export type WorkflowRunApproval = {
  id: string; nodeId: string; status: ApprovalTaskStatus; assigneeRole: string; createdAt: string; decidedAt: string | null; history: ApprovalTaskEvent[];
};

export type MyRequestSummary = {
  id: string; workflow: string; versionNumber: number; status: string; outcome: string | null; headline: string; currentStep: string | null;
  openTaskId: string | null; startedAt: string; completedAt: string | null;
};
export type MyRequestPage = { items: MyRequestSummary[]; total: number; page: number; pageSize: number };
export type MyRequestDetail = MyRequestSummary & {
  request: Record<string, unknown> | null;
  approvals: { id: string; status: ApprovalTaskStatus; assigneeRole: string; createdAt: string; decidedAt: string | null; history: ApprovalTaskEvent[] }[];
};
