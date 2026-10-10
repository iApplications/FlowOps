import { DashboardShell, SurfaceShell, TaskDetail } from "@flowops/ui";
import type { ApprovalTaskDetail, CurrentUser } from "@flowops/types";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import Link from "next/link";

export default async function TaskPage({ params }: { params: Promise<{ taskId: string }> }) {
  const { taskId } = await params;
  const cookie = (await cookies()).toString();
  const apiUrl = process.env.FLOWOPS_API_URL ?? "http://localhost:5050";
  const options = { headers: { cookie, "X-FlowOps-Surface": "Portal" }, cache: "no-store" as const };
  const back = <Link className="btn" href="/tasks">Back to tasks</Link>;
  let account: Response;
  let response: Response;
  try {
    [account, response] = await Promise.all([fetch(`${apiUrl}/api/auth/me`, options), fetch(`${apiUrl}/api/portal/tasks/${encodeURIComponent(taskId)}`, options)]);
  } catch { return <SurfaceShell title="Task unavailable" description="Start the FlowOps API and try again." />; }
  if (account.status === 401 || response.status === 401) redirect("/login");
  if (!account.ok) return <SurfaceShell title="Task unavailable" description="The API could not load your account." />;
  const user = await account.json() as CurrentUser;
  if (user.accountSurface !== "Portal") redirect("/login");
  if (response.status === 403 || !user.activeOrganization) redirect("/");
  if (!response.ok) return <SurfaceShell title="Task unavailable" description={response.status === 404 ? "This task is not available to you in the active organization." : `The API could not load this task (HTTP ${response.status}).`}>{back}</SurfaceShell>;
  const task = await response.json() as ApprovalTaskDetail;
  return <DashboardShell surface="Portal" user={user} section="tasks">
    <div className="designer-actions form-navigation">{back}</div>
    <TaskDetail surface={{ apiBase: "/api/portal/tasks", pageBase: "/tasks" }} initial={task} />
  </DashboardShell>;
}
