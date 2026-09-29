import { SurfaceShell } from "@flowops/ui";

export default function Home() {
  return (
    <SurfaceShell
      title="Studio"
      description="The workspace for building, publishing, and monitoring business workflows."
    >
      <p className="text-sm text-slate-400">
        Phase 0 foundation is ready. Workflow configuration arrives in later phases.
      </p>
    </SurfaceShell>
  );
}
