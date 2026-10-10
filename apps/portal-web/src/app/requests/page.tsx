import { DashboardShell, MyRequests, SurfaceShell } from "@flowops/ui";
import type { CurrentUser } from "@flowops/types";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";

export default async function RequestsPage() {
  const cookie = (await cookies()).toString();
  const apiUrl = process.env.FLOWOPS_API_URL ?? "http://localhost:5050";
  const options = { headers: { cookie, "X-FlowOps-Surface": "Portal" }, cache: "no-store" as const };
  let account: Response;
  let access: Response;
  try {
    [account, access] = await Promise.all([fetch(`${apiUrl}/api/auth/me`, options), fetch(`${apiUrl}/api/portal/my-requests?page=1`, options)]);
  } catch { return <SurfaceShell title="Requests unavailable" description="Start the FlowOps API and try again." />; }
  if (account.status === 401 || access.status === 401) redirect("/login");
  if (!account.ok) return <SurfaceShell title="Requests unavailable" description="The API could not load your account." />;
  const user = await account.json() as CurrentUser;
  if (user.accountSurface !== "Portal") redirect("/login");
  if (access.status === 403 || !user.activeOrganization) redirect("/");
  if (!access.ok) return <SurfaceShell title="Requests unavailable" description={`The API could not load your requests (HTTP ${access.status}).`} />;
  return <DashboardShell surface="Portal" user={user} section="requests">
    <MyRequests key={user.activeOrganization.organizationId} />
  </DashboardShell>;
}
