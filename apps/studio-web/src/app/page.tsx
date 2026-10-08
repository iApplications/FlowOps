import { DashboardShell, SurfaceShell, WorkspaceAccount } from "@flowops/ui";
import type { CurrentUser } from "@flowops/types";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";

export default async function Home() {
  const cookie = (await cookies()).toString();
  const apiUrl = process.env.FLOWOPS_API_URL ?? "http://localhost:5050";
  let response: Response;
  try {
    response = await fetch(`${apiUrl}/api/auth/me`, { headers: { cookie, "X-FlowOps-Surface": "Studio" }, cache: "no-store" });
  } catch {
    return <SurfaceShell title="Studio unavailable" description="Start the FlowOps API and try again." />;
  }
  if (response.status === 401) redirect("/login");
  if (!response.ok) return <SurfaceShell title="Studio unavailable" description="The API could not load your account." />;
  const user = await response.json() as CurrentUser;
  if (user.accountSurface !== "Studio") redirect("/login");
  let access: Response;
  let onboarding: Response | null = null;
  try {
    access = await fetch(`${apiUrl}/api/studio/access`, { headers: { cookie, "X-FlowOps-Surface": "Studio" }, cache: "no-store" });
    if (access.status === 403)
      onboarding = await fetch(`${apiUrl}/api/auth/studio-onboarding`, { headers: { cookie, "X-FlowOps-Surface": "Studio" }, cache: "no-store" });
  } catch {
    return <SurfaceShell title="Studio unavailable" description="Start the FlowOps API and try again." />;
  }
  if (onboarding) {
    if (onboarding.ok) {
      return <SurfaceShell title="Set up your Studio workspace" description="Create a business workspace to become its Owner, then enable your authenticator in Security. Studio management becomes available after workspace access and MFA are ready.">
        <WorkspaceAccount initialUser={user} surface="Studio" onboarding />
      </SurfaceShell>;
    }
    return <SurfaceShell title="Studio access denied" description="Ask a workspace Owner to grant you an Admin role, or use Portal for participant access.">
      <a className="btn btn-primary" href={process.env.FLOWOPS_PORTAL_URL ?? "http://localhost:3001"}>Open Portal</a>
    </SurfaceShell>;
  }
  if (access.status !== 200)
    return <SurfaceShell title="Studio unavailable" description="The API could not check your Studio access." />;
  return (
    <DashboardShell surface="Studio" user={user}>
      <WorkspaceAccount initialUser={user} surface="Studio" />
    </DashboardShell>
  );
}
