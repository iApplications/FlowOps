import { SurfaceShell } from "@flowops/ui";

export default function Home() {
  return (
    <SurfaceShell
      title="Portal"
      description="A simple place to submit requests, complete assigned tasks, and follow progress."
    >
      <p className="text-sm text-slate-400">
        Phase 0 foundation is ready. Forms and tasks arrive in later phases.
      </p>
    </SurfaceShell>
  );
}
