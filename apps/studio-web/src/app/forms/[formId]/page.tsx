import { DashboardShell, SurfaceShell, FormDesigner } from "@flowops/ui";
import type { CurrentUser, FormDraft } from "@flowops/types";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import Link from "next/link";

export default async function DesignerPage({ params }: { params: Promise<{ formId: string }> }) {
  const { formId } = await params;
  const cookie = (await cookies()).toString();
  const apiUrl = process.env.FLOWOPS_API_URL ?? "http://localhost:5050";
  const options = { headers: { cookie, "X-FlowOps-Surface": "Studio" }, cache: "no-store" as const };
  let account: Response;
  let response: Response;
  try {
    account = await fetch(`${apiUrl}/api/auth/me`, options);
    response = await fetch(`${apiUrl}/api/forms/${encodeURIComponent(formId)}`, options);
  } catch { return <SurfaceShell title="Designer unavailable" description="Start the FlowOps API and try again." />; }
  if (account.status === 401 || response.status === 401) redirect("/login");
  if (!account.ok) return <SurfaceShell title="Designer unavailable" description="The API could not load your account." />;
  const user = await account.json() as CurrentUser;
  if (user.accountSurface !== "Studio") redirect("/login");
  if (response.status === 403 || !user.activeOrganization) redirect("/");
  if (!response.ok) return <SurfaceShell title="Form unavailable" description={response.status === 404 ? "This form is not in your active organization." : response.status === 405 ? "The running API does not include the field designer yet. Stop debugging in Visual Studio, rebuild the API, then start it again." : `The API could not load this form (HTTP ${response.status}).`}><Link className="btn" href="/forms">Back to Forms</Link></SurfaceShell>;
  const form = await response.json() as FormDraft;
  return <DashboardShell surface="Studio" user={user} section="forms">
    <p><Link className="text-button" href="/forms">← Back to Forms</Link></p>
    <FormDesigner key={form.id} initialForm={form} />
  </DashboardShell>;
}
