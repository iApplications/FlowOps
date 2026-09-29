import type { ReactNode } from "react";

type SurfaceShellProps = {
  title: string;
  description: string;
  children?: ReactNode;
};

export function SurfaceShell({ title, description, children }: SurfaceShellProps) {
  return (
    <main className="min-h-screen bg-slate-950 px-6 py-16 text-slate-100">
      <div className="mx-auto max-w-4xl">
        <p className="mb-4 text-sm font-semibold uppercase tracking-[0.2em] text-sky-300">
          FlowOps
        </p>
        <h1 className="text-4xl font-semibold tracking-tight">{title}</h1>
        <p className="mt-4 max-w-2xl text-lg text-slate-300">{description}</p>
        {children && <div className="mt-10">{children}</div>}
      </div>
    </main>
  );
}
