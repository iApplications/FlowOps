import { DashboardShell, SurfaceShell, TaskInbox } from "@flowops/ui";
import type { CurrentUser } from "@flowops/types";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";

export default async function TasksPage() {
  const cookie = (await cookies()).toString();
  const apiUrl = process.env.FLOWOPS_API_URL ?? "http://localhost:5050";
  const options = { headers: { cookie, "X-FlowOps-Surface": "Portal" }, cache: "no-store" as const };
  let account: Response;
  let access: Response;
  try {
    [account, access] = await Promise.all([fetch(`${apiUrl}/api/auth/me`, options), fetch(`${apiUrl}/api/portal/tasks?page=1`, options)]);
  } catch { return <SurfaceShell title="Tasks unavailable" description="Start the FlowOps API and try again." />; }
  if (account.status === 401 || access.status === 401) redirect("/login");
  if (!account.ok) return <SurfaceShell title="Tasks unavailable" description="The API could not load your account." />;
  const user = await account.json() as CurrentUser;
  if (user.accountSurface !== "Portal") redirect("/login");
  if (access.status === 403 || !user.activeOrganization) redirect("/");
  if (!access.ok) return <SurfaceShell title="Tasks unavailable" description={`The API could not load tasks (HTTP ${access.status}).`} />;
  return <DashboardShell surface="Portal" user={user} section="tasks">
    <TaskInbox key={user.activeOrganization.organizationId} surface={{ apiBase: "/api/portal/tasks", pageBase: "/tasks" }} />
  </DashboardShell>;
}
