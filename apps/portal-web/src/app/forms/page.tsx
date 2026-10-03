import { DashboardShell, SurfaceShell } from "@flowops/ui";
import type { CurrentUser, PublishedForm } from "@flowops/types";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import Link from "next/link";

export default async function FormsPage() {
  const cookie = (await cookies()).toString();
  const apiUrl = process.env.FLOWOPS_API_URL ?? "http://localhost:5050";
  const options = { headers: { cookie, "X-FlowOps-Surface": "Portal" }, cache: "no-store" as const };
  let account: Response;
  let response: Response;
  try {
    [account, response] = await Promise.all([fetch(`${apiUrl}/api/auth/me`, options), fetch(`${apiUrl}/api/portal/forms`, options)]);
  } catch { return <SurfaceShell title="Forms unavailable" description="Start the FlowOps API and try again." />; }
  if (account.status === 401 || response.status === 401) redirect("/login");
  if (!account.ok) return <SurfaceShell title="Forms unavailable" description="The API could not load your account." />;
  const user = await account.json() as CurrentUser;
  if (user.accountSurface !== "Portal") redirect("/login");
  if (!user.activeOrganization || response.status === 403) redirect("/");
  if (!response.ok) return <SurfaceShell title="Forms unavailable" description={`The API could not load forms (HTTP ${response.status}).`} />;
  const forms = await response.json() as PublishedForm[];
  return <DashboardShell surface="Portal" user={user} section="forms"><section className="panel-card">
    <div className="panel-header"><div><span className="panel-kicker">{user.activeOrganization.name}</span><h2>Published forms</h2><p>Forms shared by your organization.</p></div></div>
    {forms.length === 0 ? <p className="empty-state">Your organization has not published any forms yet.</p> : <div className="org-list">{forms.map(form => <Link className="org-option" href={`/forms/${form.formId}`} key={form.id}><span><strong>{form.name}</strong><small style={{ display: "block" }}>{form.description}</small></span><span className="tag">Version {form.versionNumber}</span></Link>)}</div>}
  </section></DashboardShell>;
}
