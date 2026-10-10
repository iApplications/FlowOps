import { DashboardShell, SurfaceShell, RoleManager } from "@flowops/ui";
import type { CurrentUser } from "@flowops/types";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import Link from "next/link";

export default async function RolesPage() {
  const cookie = (await cookies()).toString();
  const apiUrl = process.env.FLOWOPS_API_URL ?? "http://localhost:5050";
  const options = { headers: { cookie, "X-FlowOps-Surface": "Studio" }, cache: "no-store" as const };
  let account: Response;
  try { account = await fetch(`${apiUrl}/api/auth/me`, options); }
  catch { return <SurfaceShell title="Roles unavailable" description="Start the FlowOps API and try again." />; }
  if (account.status === 401) redirect("/login");
  if (!account.ok) return <SurfaceShell title="Roles unavailable" description="The API could not load your account." />;
  const user = await account.json() as CurrentUser;
  if (user.accountSurface !== "Studio") redirect("/login");
  const active = user.activeOrganization;
  if (!active || (active.role !== "Owner" && active.role !== "Admin")) redirect("/");
  if (active.requireMfaForPrivileged && !user.mfaSatisfied)
    return <DashboardShell surface="Studio" user={user} section="roles"><div className="notice"><strong>Authenticator required.</strong> Set up MFA in <Link href="/#security">Security</Link> to manage {active.name}.</div></DashboardShell>;
  return <DashboardShell surface="Studio" user={user} section="roles">
    <RoleManager key={active.organizationId} organizationId={active.organizationId} organizationName={active.name} currentRole={active.role} />
  </DashboardShell>;
}
