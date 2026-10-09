import { DashboardShell, SurfaceShell } from "@flowops/ui";
// Separate entry point so React Flow and its stylesheet are bundled only where the designer is used.
import { WorkflowDesigner } from "@flowops/ui/workflow-designer";
import type { CurrentUser, WorkflowDetail } from "@flowops/types";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import Link from "next/link";

export default async function WorkflowDesignerPage({ params }: { params: Promise<{ workflowId: string }> }) {
  const { workflowId } = await params;
  const cookie = (await cookies()).toString();
  const apiUrl = process.env.FLOWOPS_API_URL ?? "http://localhost:5050";
  const options = { headers: { cookie, "X-FlowOps-Surface": "Studio" }, cache: "no-store" as const };
  let account: Response;
  let response: Response;
  try {
    [account, response] = await Promise.all([fetch(`${apiUrl}/api/auth/me`, options), fetch(`${apiUrl}/api/workflows/${encodeURIComponent(workflowId)}`, options)]);
  } catch { return <SurfaceShell title="Designer unavailable" description="Start the FlowOps API and try again." />; }
  if (account.status === 401 || response.status === 401) redirect("/login");
  if (!account.ok) return <SurfaceShell title="Designer unavailable" description="The API could not load your account." />;
  const user = await account.json() as CurrentUser;
  if (user.accountSurface !== "Studio") redirect("/login");
  if (response.status === 403 || !user.activeOrganization) redirect("/");
  if (!response.ok) return <SurfaceShell title="Workflow unavailable" description={response.status === 404 ? "This workflow is not in your active organization." : `The API could not load this workflow (HTTP ${response.status}).`}><Link className="btn" href="/workflows">Back to Workflows</Link></SurfaceShell>;
  const workflow = await response.json() as WorkflowDetail;
  if (!workflow.draft) return <SurfaceShell title="No editable draft" description="This workflow has no draft version to design."><Link className="btn" href="/workflows">Back to Workflows</Link></SurfaceShell>;
  return <DashboardShell surface="Studio" user={user} section="workflows">
    <div className="designer-actions form-navigation"><Link className="btn" href="/workflows">Back to Workflows</Link></div>
    <WorkflowDesigner key={`${workflow.id}-${workflow.draft.id}`} initialWorkflow={workflow} />
  </DashboardShell>;
}
