import { DashboardShell, SurfaceShell, FormRenderer } from "@flowops/ui";
import type { CurrentUser, PublishedForm } from "@flowops/types";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import Link from "next/link";

export default async function PublishedFormPage({ params }: { params: Promise<{ formId: string }> }) {
  const { formId } = await params;
  const cookie = (await cookies()).toString();
  const apiUrl = process.env.FLOWOPS_API_URL ?? "http://localhost:5050";
  const options = { headers: { cookie, "X-FlowOps-Surface": "Portal" }, cache: "no-store" as const };
  let account: Response;
  let response: Response;
  try {
    [account, response] = await Promise.all([fetch(`${apiUrl}/api/auth/me`, options), fetch(`${apiUrl}/api/portal/forms/${encodeURIComponent(formId)}`, options)]);
  } catch { return <SurfaceShell title="Form unavailable" description="Start the FlowOps API and try again." />; }
  if (account.status === 401 || response.status === 401) redirect("/login");
  if (!account.ok) return <SurfaceShell title="Form unavailable" description="The API could not load your account." />;
  const user = await account.json() as CurrentUser;
  if (user.accountSurface !== "Portal") redirect("/login");
  if (!user.activeOrganization || response.status === 403) redirect("/");
  if (!response.ok) return <SurfaceShell title="Form unavailable" description="This form is not published in your active organization."><Link className="btn" href="/forms">Back to Forms</Link></SurfaceShell>;
  const form = await response.json() as PublishedForm;
  return <DashboardShell surface="Portal" user={user} section="forms"><div className="designer-actions form-navigation"><Link className="btn" href="/forms">Back to Forms</Link></div><section className="panel-card"><span className="tag">Published version {form.versionNumber}</span><FormRenderer fields={form.fields} title={form.name} description={form.description} readOnly /></section></DashboardShell>;
}
