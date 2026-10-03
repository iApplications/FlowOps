import { DashboardShell, SurfaceShell, WorkspaceAccount } from "@flowops/ui";
import type { CurrentUser } from "@flowops/types";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";

export default async function Home() {
  const cookie = (await cookies()).toString();
  const apiUrl = process.env.FLOWOPS_API_URL ?? "http://localhost:5050";
  let response: Response;
  try {
    response = await fetch(`${apiUrl}/api/auth/me`, { headers: { cookie, "X-FlowOps-Surface": "Portal" }, cache: "no-store" });
  } catch {
    return <SurfaceShell title="Portal unavailable" description="Start the FlowOps API and try again." />;
  }
  if (response.status === 401) redirect("/login");
  if (!response.ok) return <SurfaceShell title="Portal unavailable" description="The API could not load your account." />;
  const user = await response.json() as CurrentUser;
  if (user.accountSurface !== "Portal") redirect("/login");
  return (
    <DashboardShell surface="Portal" user={user}>
      <WorkspaceAccount initialUser={user} surface="Portal" />
    </DashboardShell>
  );
}
