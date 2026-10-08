import { DashboardShell, SurfaceShell, WorkflowLibrary } from "@flowops/ui";
import type { CurrentUser } from "@flowops/types";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";

export default async function WorkflowsPage() {
  const cookie = (await cookies()).toString();
  const apiUrl = process.env.FLOWOPS_API_URL ?? "http://localhost:5050";
  const options = { headers: { cookie, "X-FlowOps-Surface": "Studio" }, cache: "no-store" as const };
  let response: Response;
  let access: Response;
  try {
    [response, access] = await Promise.all([fetch(`${apiUrl}/api/auth/me`, options), fetch(`${apiUrl}/api/studio/access`, options)]);
  } catch {
    return <SurfaceShell title="Workflows unavailable" description="Start the FlowOps API and try again." />;
  }
  if (response.status === 401 || access.status === 401) redirect("/login");
  if (!response.ok) return <SurfaceShell title="Workflows unavailable" description="The API could not load your account." />;
  const user = await response.json() as CurrentUser;
  if (user.accountSurface !== "Studio") redirect("/login");
  if (access.status === 403 || !user.activeOrganization) redirect("/");
  if (!access.ok) return <SurfaceShell title="Workflows unavailable" description="The API could not check your workspace access." />;
  return <DashboardShell surface="Studio" user={user} section="workflows">
    <WorkflowLibrary key={user.activeOrganization.organizationId} organizationName={user.activeOrganization.name} />
  </DashboardShell>;
}
