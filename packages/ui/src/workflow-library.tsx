"use client";

import { useEffect, useState, type FormEvent } from "react";
import type { WorkflowSummary } from "@flowops/types";
import { apiRequest } from "@flowops/api-client";

export function WorkflowLibrary({ organizationName }: { organizationName: string }) {
  const [workflows, setWorkflows] = useState<WorkflowSummary[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [failed, setFailed] = useState(false);
  useEffect(() => {
    let cancelled = false;
    apiRequest<WorkflowSummary[]>("/api/workflows").then(rows => { if (!cancelled) setWorkflows(rows); })
      .catch(error => { if (!cancelled) { setFailed(true); setMessage(error.message); } })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, []);

  function select(workflow: WorkflowSummary | null) {
    setSelectedId(workflow?.id ?? null); setName(workflow?.name ?? "");
    setDescription(workflow?.description ?? ""); setMessage(""); setFailed(false);
  }
  async function save(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    setBusy(true); setMessage(""); setFailed(false);
    try {
      await apiRequest(selectedId ? `/api/workflows/${selectedId}` : "/api/workflows", {
        method: selectedId ? "PUT" : "POST",
        body: JSON.stringify({ name, description, revision: workflows.find(workflow => workflow.id === selectedId)?.revision }),
      });
      setWorkflows(await apiRequest<WorkflowSummary[]>("/api/workflows"));
      setSelectedId(null); setName(""); setDescription(""); setMessage(selectedId ? "Workflow details saved." : "Workflow created with a Start → End draft.");
    } catch (error) { setFailed(true); setMessage(error instanceof Error ? error.message : "Unable to save workflow."); }
    finally { setBusy(false); }
  }
  return <section className="panel-card" id="workflows" style={{ marginBottom: 24 }}>
    <div className="panel-header"><div><span className="panel-kicker">{organizationName}</span><h2>Workflows</h2>
      <p>Create workflows, then select one and open the designer to edit its draft version. Publishing and running workflows arrive in a later phase.</p></div><span className="tag">{workflows.length} workflows</span></div>
    {message && <p className="notice" role={failed ? "alert" : "status"}>{message}</p>}
    {loading ? <p className="empty-state">Loading workflows…</p> : workflows.length === 0 ? <p className="empty-state">Create your first workflow for this organization.</p> :
      <div className="org-list">{workflows.map(workflow => <button key={workflow.id} type="button" disabled={busy} className={`org-option ${selectedId === workflow.id ? "org-option-active" : ""}`} onClick={() => select(workflow)}>
        <span><strong>{workflow.name}</strong><small style={{ display: "block" }}>{workflow.description || "No description"}</small></span>
        <span className="designer-actions">
          <span className="tag">{workflow.draftVersionNumber ? `Draft v${workflow.draftVersionNumber}` : "No draft"}{workflow.publishedVersions ? ` · ${workflow.publishedVersions} published` : ""}</span>
          {workflow.issueCount !== null && <span className={workflow.issueCount ? "tag tag-warning" : "tag"}>{workflow.issueCount ? `${workflow.issueCount} ${workflow.issueCount === 1 ? "issue" : "issues"}` : "Complete"}</span>}
        </span>
      </button>)}</div>}
    <div className="section-divider" />
    <form onSubmit={save} className="stack-form">
      <h3>{selectedId ? "Edit workflow details" : "New workflow"}</h3>
      <label className="field">Workflow name<input required maxLength={120} value={name} disabled={busy} onChange={event => setName(event.target.value)} placeholder="Expense approval" /></label>
      <label className="field">Description<textarea maxLength={2000} value={description} disabled={busy} onChange={event => setDescription(event.target.value)} placeholder="Describe what this workflow does" /></label>
      <div className="designer-actions">
        <button className="btn btn-primary" disabled={busy}>{busy ? "Saving…" : selectedId ? "Save changes" : "Create workflow"}</button>
        {selectedId && <>
          <a className="btn" href={`/workflows/${selectedId}`} aria-disabled={busy || undefined} onClick={event => { if (busy) event.preventDefault(); }}>Open designer</a>
          <button type="button" className="btn" disabled={busy} onClick={() => select(null)}>Cancel editing</button>
        </>}
      </div>
    </form>
  </section>;
}
