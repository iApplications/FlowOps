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
