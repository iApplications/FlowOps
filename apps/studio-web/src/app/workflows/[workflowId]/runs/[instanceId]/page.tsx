import { DashboardShell, SurfaceShell, WorkflowRunDetail } from "@flowops/ui";
import type { CurrentUser, WorkflowRunDetail as RunDetail } from "@flowops/types";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import Link from "next/link";

export default async function WorkflowRunPage({ params }: { params: Promise<{ workflowId: string; instanceId: string }> }) {
  const { workflowId, instanceId } = await params;
  const cookie = (await cookies()).toString();
  const apiUrl = process.env.FLOWOPS_API_URL ?? "http://localhost:5050";
  const options = { headers: { cookie, "X-FlowOps-Surface": "Studio" }, cache: "no-store" as const };
  const back = <Link className="btn" href={`/workflows/${encodeURIComponent(workflowId)}/runs`}>Back to runs</Link>;
  let account: Response;
  let response: Response;
  try {
    [account, response] = await Promise.all([fetch(`${apiUrl}/api/auth/me`, options), fetch(`${apiUrl}/api/workflow-instances/${encodeURIComponent(instanceId)}`, options)]);
  } catch { return <SurfaceShell title="Run unavailable" description="Start the FlowOps API and try again." />; }
  if (account.status === 401 || response.status === 401) redirect("/login");
  if (!account.ok) return <SurfaceShell title="Run unavailable" description="The API could not load your account." />;
  const user = await account.json() as CurrentUser;
  if (user.accountSurface !== "Studio") redirect("/login");
  if (response.status === 403 || !user.activeOrganization) redirect("/");
  if (!response.ok) return <SurfaceShell title="Run unavailable" description={response.status === 404 ? "This run is not in your active organization." : `The API could not load this run (HTTP ${response.status}).`}>{back}</SurfaceShell>;
  const run = await response.json() as RunDetail;
  // The run belongs to the workflow in the URL; otherwise send the user to the run's own workflow.
  if (run.workflow.id !== workflowId) redirect(`/workflows/${run.workflow.id}/runs/${run.id}`);
  return <DashboardShell surface="Studio" user={user} section="workflows">
    <div className="designer-actions form-navigation">{back}</div>
    <WorkflowRunDetail run={run} />
  </DashboardShell>;
}
