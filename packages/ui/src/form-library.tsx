"use client";

import { useEffect, useState, type FormEvent } from "react";
import { apiRequest } from "@flowops/api-client";

type DraftForm = { id: string; name: string; description: string };

export function FormLibrary({ organizationName }: { organizationName: string }) {
  const [forms, setForms] = useState<DraftForm[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [failed, setFailed] = useState(false);
  useEffect(() => {
    let cancelled = false;
    apiRequest<DraftForm[]>("/api/forms").then(rows => { if (!cancelled) setForms(rows); })
      .catch(error => { if (!cancelled) { setFailed(true); setMessage(error.message); } })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, []);

  function select(form: DraftForm | null) {
    setSelectedId(form?.id ?? null); setName(form?.name ?? "");
    setDescription(form?.description ?? ""); setMessage(""); setFailed(false);
  }
  async function save(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    setBusy(true); setMessage(""); setFailed(false);
    try {
      await apiRequest(selectedId ? `/api/forms/${selectedId}` : "/api/forms", {
        method: selectedId ? "PUT" : "POST", body: JSON.stringify({ name, description }),
      });
      setForms(await apiRequest<DraftForm[]>("/api/forms"));
      setSelectedId(null); setName(""); setDescription(""); setMessage("Draft saved.");
    } catch (error) { setFailed(true); setMessage(error instanceof Error ? error.message : "Unable to save form."); }
    finally { setBusy(false); }
  }
  return <section className="panel-card" id="forms" style={{ marginBottom: 24 }}>
    <div className="panel-header"><div><span className="panel-kicker">{organizationName}</span><h2>Forms</h2>
      <p>Create and organize draft forms. Field design and publishing are the next steps.</p></div><span className="tag">{forms.length} drafts</span></div>
    {message && <p className="notice" role={failed ? "alert" : "status"}>{message}</p>}
    {loading ? <p className="empty-state">Loading forms…</p> : forms.length === 0 ? <p className="empty-state">Create your first form for this organization.</p> :
      <div className="org-list">{forms.map(form => <button key={form.id} type="button" disabled={busy} className={`org-option ${selectedId === form.id ? "org-option-active" : ""}`} onClick={() => select(form)}>
        <span><strong>{form.name}</strong><small style={{ display: "block" }}>{form.description || "No description"}</small></span><span className="tag">Draft</span>
      </button>)}</div>}
    <div className="section-divider" />
    <form onSubmit={save} className="stack-form">
      <h3>{selectedId ? "Edit draft details" : "New form"}</h3>
      <label className="field">Form name<input required maxLength={120} value={name} disabled={busy} onChange={event => setName(event.target.value)} placeholder="Expense claim" /></label>
      <label className="field">Description<textarea maxLength={2000} value={description} disabled={busy} onChange={event => setDescription(event.target.value)} placeholder="Describe what this form is for" /></label>
      <button className="btn btn-primary" disabled={busy}>{busy ? "Saving…" : selectedId ? "Save changes" : "Create draft"}</button>
      {selectedId && <button type="button" className="btn" disabled={busy} onClick={() => select(null)}>Cancel editing</button>}
    </form>
  </section>;
}
