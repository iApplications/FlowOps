import { DashboardShell, SurfaceShell, FormLibrary } from "@flowops/ui";
import type { CurrentUser } from "@flowops/types";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";

export default async function FormsPage() {
  const cookie = (await cookies()).toString();
  const apiUrl = process.env.FLOWOPS_API_URL ?? "http://localhost:5050";
  const options = { headers: { cookie, "X-FlowOps-Surface": "Studio" }, cache: "no-store" as const };
  let response: Response;
  let access: Response;
  try {
    response = await fetch(`${apiUrl}/api/auth/me`, options);
    access = await fetch(`${apiUrl}/api/studio/access`, options);
  } catch {
    return <SurfaceShell title="Forms unavailable" description="Start the FlowOps API and try again." />;
  }
  if (response.status === 401 || access.status === 401) redirect("/login");
  if (!response.ok) return <SurfaceShell title="Forms unavailable" description="The API could not load your account." />;
  const user = await response.json() as CurrentUser;
  if (user.accountSurface !== "Studio") redirect("/login");
  if (access.status === 403 || !user.activeOrganization) redirect("/");
  if (!access.ok) return <SurfaceShell title="Forms unavailable" description="The API could not check your workspace access." />;
  return <DashboardShell surface="Studio" user={user} section="forms">
    <FormLibrary key={user.activeOrganization.organizationId} organizationName={user.activeOrganization.name} />
  </DashboardShell>;
}
