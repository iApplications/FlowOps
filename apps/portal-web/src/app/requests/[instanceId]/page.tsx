import { DashboardShell, MyRequestDetailView, SurfaceShell } from "@flowops/ui";
import type { CurrentUser, MyRequestDetail } from "@flowops/types";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import Link from "next/link";

export default async function RequestPage({ params }: { params: Promise<{ instanceId: string }> }) {
  const { instanceId } = await params;
  const cookie = (await cookies()).toString();
  const apiUrl = process.env.FLOWOPS_API_URL ?? "http://localhost:5050";
  const options = { headers: { cookie, "X-FlowOps-Surface": "Portal" }, cache: "no-store" as const };
  const back = <Link className="btn" href="/requests">Back to my requests</Link>;
  let account: Response;
  let response: Response;
  try {
    [account, response] = await Promise.all([fetch(`${apiUrl}/api/auth/me`, options), fetch(`${apiUrl}/api/portal/my-requests/${encodeURIComponent(instanceId)}`, options)]);
  } catch { return <SurfaceShell title="Request unavailable" description="Start the FlowOps API and try again." />; }
  if (account.status === 401 || response.status === 401) redirect("/login");
  if (!account.ok) return <SurfaceShell title="Request unavailable" description="The API could not load your account." />;
  const user = await account.json() as CurrentUser;
  if (user.accountSurface !== "Portal") redirect("/login");
  if (response.status === 403 || !user.activeOrganization) redirect("/");
  if (!response.ok) return <SurfaceShell title="Request unavailable" description={response.status === 404 ? "This request is not available to you in the active organization." : `The API could not load this request (HTTP ${response.status}).`}>{back}</SurfaceShell>;
  const request = await response.json() as MyRequestDetail;
  return <DashboardShell surface="Portal" user={user} section="requests">
    <div className="designer-actions form-navigation">{back}</div>
    <MyRequestDetailView initial={request} />
  </DashboardShell>;
}
