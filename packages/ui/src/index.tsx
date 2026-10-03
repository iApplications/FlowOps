import type { ReactNode } from "react";
export { AuthForm } from "./auth-form";
export { ConfirmEmail } from "./confirm-email";
export { WorkspaceAccount } from "./workspace-account";
export { BrandMark, DashboardShell } from "./shell";

type SurfaceShellProps = {
  title: string;
  description: string;
  children?: ReactNode;
};

export function SurfaceShell({ title, description, children }: SurfaceShellProps) {
  return (
    <main className="simple-shell">
      <a className="brand" href="/"><span className="brand-symbol" aria-hidden="true"><i /><i /><i /></span>FlowOps</a>
      <div className="simple-content">
        <p className="eyebrow">Workspace</p>
        <h1>{title}</h1>
        <p>{description}</p>
        {children && <div style={{ marginTop: 28 }}>{children}</div>}
      </div>
    </main>
  );
}
export { FormLibrary } from "./form-library";
