"use client";

import { useEffect, useState, type FormEvent } from "react";
import type { ApprovalTaskDecision, ApprovalTaskDetail, ApprovalTaskEvent, ApprovalTaskPage, ApprovalTaskStatus, FormField, MyRequestDetail, MyRequestPage } from "@flowops/types";
import { apiRequest } from "@flowops/api-client";

// Shared by Portal (`/api/portal/tasks`) and Studio (`/api/tasks`): the same task API behind two surfaces (PHASE5 decision 3).
export type TaskSurface = { apiBase: string; pageBase: string };

const statusLabels: Record<ApprovalTaskStatus, string> = {
  Pending: "Pending", Approved: "Approved", Rejected: "Rejected", ChangesRequested: "Changes requested", Cancelled: "Cancelled",
};
const eventLabels: Record<ApprovalTaskEvent["kind"], string> = {
  Created: "Task created", Approved: "Approved", Rejected: "Rejected", ChangesRequested: "Changes requested", Resubmitted: "Resubmitted", Commented: "Comment",
};

function when(value: string | null) {
  return value ? new Date(value).toLocaleString() : "—";
}

function statusTag(status: ApprovalTaskStatus) {
  return status === "Pending" || status === "ChangesRequested" ? "tag tag-warning" : "tag";
}

function display(value: unknown) {
  if (value === null || value === undefined || value === "") return "—";
  if (typeof value === "boolean") return value ? "Yes" : "No";
  return typeof value === "object" ? JSON.stringify(value) : String(value);
}

// The requester edits the values of the form version they submitted and resubmits; files carry over from the original response.
function ResubmitForm({ fields, request, busy, onSubmit }: {
  fields: FormField[]; request: Record<string, unknown> | null; busy: boolean;
  onSubmit: (values: Record<string, string | number | boolean | null>, comment: string) => void;
}) {
  const key = (field: FormField) => field.key ?? field.id;
  const [values, setValues] = useState<Record<string, string | boolean>>(() => Object.fromEntries(fields.filter(f => f.type !== "File").map(field => {
    const current = request?.[key(field)];
    return [key(field), field.type === "Checkbox" ? current === true : current === null || current === undefined ? "" : String(current)];
  })));
  const [comment, setComment] = useState("");
  function submit(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    const out: Record<string, string | number | boolean | null> = {};
    for (const field of fields) {
      if (field.type === "File") continue;
      const raw = values[key(field)];
      out[key(field)] = field.type === "Checkbox" ? raw === true : raw === "" ? null : field.type === "Number" || field.type === "Currency" ? Number(raw) : String(raw);
    }
    onSubmit(out, comment.trim());
  }
  return <form onSubmit={submit} className="stack-form">
    {fields.map(field => {
      const id = `resubmit-${field.id}`;
      const k = key(field);
      const common = { id, required: field.required, disabled: busy };
      if (field.type === "File") return <div className="field" key={field.id}><label>{field.label}</label><small className="helper">{display(request?.[k])} · files stay as originally submitted.</small></div>;
      if (field.type === "Checkbox") return <div className="field field-check" key={field.id}>
        <input {...common} type="checkbox" checked={values[k] === true} onChange={e => setValues({ ...values, [k]: e.target.checked })} />
        <label htmlFor={id}>{field.label}{field.required ? " *" : ""}</label>
        {field.helpText && <small className="helper">{field.helpText}</small>}
      </div>;
      return <div className="field" key={field.id}>
        <label htmlFor={id}>{field.label}{field.required ? " *" : ""}</label>
        {field.type === "Textarea" ? <textarea {...common} rows={4} maxLength={field.maxLength ?? 10000} value={String(values[k])} onChange={e => setValues({ ...values, [k]: e.target.value })} /> :
          field.type === "Dropdown" ? <select {...common} value={String(values[k])} onChange={e => setValues({ ...values, [k]: e.target.value })}><option value="">Choose an option</option>{field.options?.map((option, index) => <option key={index} value={option}>{option}</option>)}</select> :
          <input {...common} type={field.type === "Date" ? "date" : field.type === "Number" || field.type === "Currency" ? "number" : "text"} value={String(values[k])}
            maxLength={field.type === "Text" ? field.maxLength ?? 10000 : undefined} min={field.type === "Date" ? field.minDate ?? undefined : field.min ?? undefined}
            max={field.type === "Date" ? field.maxDate ?? undefined : field.max ?? undefined} step={field.type === "Currency" ? "0.01" : "any"}
            onChange={e => setValues({ ...values, [k]: e.target.value })} />}
        {field.helpText && <small className="helper">{field.helpText}</small>}
      </div>;
    })}
    <label className="field">Note to the approver<textarea className="task-comment" rows={3} maxLength={2000} value={comment} disabled={busy} placeholder="Optional: what did you change?" onChange={e => setComment(e.target.value)} /></label>
    <div className="designer-actions task-actions"><button className="btn btn-primary" disabled={busy}>{busy ? "Resubmitting…" : "Resubmit for approval"}</button></div>
  </form>;
}

export function ApprovalHistory({ history }: { history: ApprovalTaskEvent[] }) {
  return <ol className="org-list" style={{ listStyle: "none", padding: 0 }}>{history.map(event => <li key={event.id} className="org-option" style={{ display: "block" }}>
    <strong>{eventLabels[event.kind]}</strong><small style={{ display: "block" }}>{event.actor ?? "System"} · {when(event.createdAt)}</small>
    {event.comment && <p style={{ margin: "6px 0 0" }}>{event.comment}</p>}
  </li>)}</ol>;
}

// My Tasks: pending approvals for the caller's role, with a decided tab.
export function TaskInbox({ surface }: { surface: TaskSurface }) {
  const [tab, setTab] = useState<"pending" | "decided">("pending");
  const [page, setPage] = useState(1);
  const [data, setData] = useState<ApprovalTaskPage | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [reload, setReload] = useState(0);
  useEffect(() => {
    let cancelled = false;
    apiRequest<ApprovalTaskPage>(`${surface.apiBase}?status=${tab}&page=${page}`).then(rows => { if (!cancelled) { setData(rows); setError(""); } })
      .catch(failure => { if (!cancelled) setError(failure instanceof Error ? failure.message : "Unable to load tasks."); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [surface.apiBase, tab, page, reload]);
  // Re-clicking the active tab changes no state, so no fetch would run; ignore it rather than stay on "Loading".
  const choose = (next: "pending" | "decided") => { if (next === tab) return; setLoading(true); setPage(1); setTab(next); };
  return <section className="panel-card">
    <div className="panel-header"><div><span className="panel-kicker">Approvals</span><h2>My tasks</h2><p>Requests waiting for a decision from someone with your role.</p></div>
      <button type="button" className="btn" onClick={() => { setLoading(true); setReload(value => value + 1); }}>Refresh</button></div>
    <div className="designer-actions task-tabs" role="tablist" aria-label="Task status">
      <button type="button" role="tab" aria-selected={tab === "pending"} className={tab === "pending" ? "btn btn-primary" : "btn"} onClick={() => choose("pending")}>Pending</button>
      <button type="button" role="tab" aria-selected={tab === "decided"} className={tab === "decided" ? "btn btn-primary" : "btn"} onClick={() => choose("decided")}>Decided</button>
    </div>
    {error && <p className="notice" role="alert">{error}</p>}
    {loading ? <p className="empty-state">Loading tasks…</p> : !data?.items.length ? <p className="empty-state">{tab === "pending" ? "No approvals are waiting for you." : "No decided tasks yet."}</p> : <>
      <div className="org-list">{data.items.map(task => <a key={task.id} className="org-option" href={`${surface.pageBase}/${task.id}`}>
        <span><strong>{task.workflow}</strong><small style={{ display: "block" }}>{task.requester ? `From ${task.requester} · ` : ""}{when(task.createdAt)} · {task.assigneeRole} approval</small></span>
        <span className={statusTag(task.status)}>{statusLabels[task.status]}</span>
      </a>)}</div>
      <div className="designer-actions"><button className="btn" disabled={page <= 1} onClick={() => { setLoading(true); setPage(page - 1); }}>Previous</button><span>Page {page} · {data.total} tasks</span>
        <button className="btn" disabled={page * data.pageSize >= data.total} onClick={() => { setLoading(true); setPage(page + 1); }}>Next</button></div>
    </>}
  </section>;
}

// One task: the request values, instructions, history, and the decision form when the caller may decide.
export function TaskDetail({ surface, initial }: { surface: TaskSurface; initial: ApprovalTaskDetail }) {
  const [task, setTask] = useState(initial);
  const [comment, setComment] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [failed, setFailed] = useState(false);

  async function call<T>(path: string, body: unknown) {
    return apiRequest<T>(`${surface.apiBase}/${task.id}/${path}`, { method: "POST", body: JSON.stringify(body) });
  }

  async function decide(decision: ApprovalTaskDecision) {
    if (busy) return;
    if (decision === "request_changes" && !comment.trim()) { setFailed(true); setMessage("Add a comment explaining what should change."); return; }
    setBusy(true); setMessage(""); setFailed(false);
    try {
      const updated = await call<ApprovalTaskDetail>("decision", { decision, comment: comment.trim() || undefined, revision: task.revision });
      setTask(updated); setComment("");
      setMessage(decision === "approve" ? "Approved. The workflow continues." : decision === "reject" ? "Rejected. The workflow continues on its rejected path." : "Changes requested from the requester.");
    } catch (error) {
      setFailed(true); setMessage(error instanceof Error ? error.message : "Unable to save the decision.");
      // A conflict means someone else decided first; show the latest state.
      try { setTask(await apiRequest<ApprovalTaskDetail>(`${surface.apiBase}/${task.id}`)); } catch { /* keep what is shown */ }
    } finally { setBusy(false); }
  }

  async function addComment(event: FormEvent) {
    event.preventDefault();
    if (busy || !comment.trim()) return;
    setBusy(true); setMessage(""); setFailed(false);
    try { setTask(await call<ApprovalTaskDetail>("comments", { comment: comment.trim() })); setComment(""); setMessage("Comment added."); }
    catch (error) { setFailed(true); setMessage(error instanceof Error ? error.message : "Unable to add the comment."); }
    finally { setBusy(false); }
  }

  async function resubmit(values: Record<string, string | number | boolean | null>, note: string) {
    setBusy(true); setMessage(""); setFailed(false);
    try {
      setTask(await call<ApprovalTaskDetail>("resubmit", { values, comment: note || undefined, revision: task.revision }));
      setMessage("Resubmitted. The approver will review your changes.");
    } catch (error) {
      setFailed(true); setMessage(error instanceof Error ? error.message : "Unable to resubmit.");
      try { setTask(await apiRequest<ApprovalTaskDetail>(`${surface.apiBase}/${task.id}`)); } catch { /* keep what is shown */ }
    } finally { setBusy(false); }
  }

  const values = Object.entries(task.request ?? {});
  return <>
    <section className="panel-card" style={{ marginBottom: 24 }}>
      <div className="panel-header"><div><span className="panel-kicker">{task.workflow} · Version {task.workflowVersion}</span><h2>Approval request</h2>
        <p>{task.requester ? `From ${task.requester} · ` : ""}{when(task.createdAt)} · {task.assigneeRole} approval</p></div>
        <span className={statusTag(task.status)}>{statusLabels[task.status]}</span></div>
      {task.instructions && <p className="notice"><strong>Instructions:</strong> {task.instructions}</p>}
      <div className="submission-detail"><dl>
        {values.length === 0 ? <div><dt>Request</dt><dd>No values.</dd></div> : values.map(([key, value]) => <div key={key}><dt>{key}</dt><dd>{display(value)}</dd></div>)}
      </dl></div>
    </section>
    {task.canResubmit && task.fields && <section className="panel-card" style={{ marginBottom: 24 }}>
      <div className="panel-header"><div><span className="panel-kicker">Changes requested</span><h2>Update your request</h2>
        <p>The approver asked for changes (see the history below). Update the values and resubmit; the same request goes back for a decision.</p></div></div>
      <ResubmitForm key={task.revision} fields={task.fields} request={task.request} busy={busy} onSubmit={resubmit} />
      {message && <p className={failed ? "notice" : "notice notice-success"} role={failed ? "alert" : "status"}>{message}</p>}
    </section>}
    <section className="panel-card" style={{ marginBottom: 24 }}>
      <div className="panel-header"><div><span className="panel-kicker">Decision</span><h2>{task.canDecide ? "Your decision" : "Status"}</h2>
        <p>{task.canDecide ? "The first decision is final. Comments are kept in the approval history." : task.status === "Pending" ? "You can comment on this request but cannot decide it." : task.status === "ChangesRequested" ? "Waiting for the requester to resubmit." : `This task is ${statusLabels[task.status].toLowerCase()}.`}</p></div></div>
      <form onSubmit={addComment} className="stack-form">
        <label className="field">Comment<textarea className="task-comment" rows={4} maxLength={2000} value={comment} disabled={busy} placeholder="Add a comment (required when requesting changes)" onChange={event => setComment(event.target.value)} /></label>
        {task.canDecide && <p className="helper"><strong>Request changes</strong> sends the request back to the requester with your comment. The run stays on hold until they resubmit.</p>}
        <div className="designer-actions task-actions">
          {task.canDecide && <>
            <button type="button" className="btn btn-primary" disabled={busy} onClick={() => decide("approve")}>Approve</button>
            <button type="button" className="btn" disabled={busy} onClick={() => decide("reject")}>Reject</button>
            <button type="button" className="btn" disabled={busy} onClick={() => decide("request_changes")}>Request changes</button>
          </>}
          <button className="btn" disabled={busy || !comment.trim()}>Add comment</button>
        </div>
      </form>
      {message && <p className={failed ? "notice" : "notice notice-success"} role={failed ? "alert" : "status"}>{message}</p>}
    </section>
    <section className="panel-card">
      <div className="panel-header"><div><span className="panel-kicker">History</span><h2>Approval history</h2></div></div>
      <ApprovalHistory history={task.history} />
    </section>
  </>;
}

const stepLabels: Record<string, string> = { Waiting: "Waiting", Completed: "Completed", Failed: "Stopped" };

// My requests (Portal): the runs the caller started, where each one is, and its approval history.
export function MyRequests() {
  const [page, setPage] = useState(1);
  const [data, setData] = useState<MyRequestPage | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [reload, setReload] = useState(0);
  useEffect(() => {
    let cancelled = false;
    apiRequest<MyRequestPage>(`/api/portal/my-requests?page=${page}`).then(rows => { if (!cancelled) { setData(rows); setError(""); } })
      .catch(failure => { if (!cancelled) setError(failure instanceof Error ? failure.message : "Unable to load your requests."); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [page, reload]);
  return <section className="panel-card">
    <div className="panel-header"><div><span className="panel-kicker">Requests</span><h2>My requests</h2><p>Runs you started by submitting forms, and where they stand.</p></div>
      <button type="button" className="btn" onClick={() => { setLoading(true); setReload(value => value + 1); }}>Refresh</button></div>
    {error && <p className="notice" role="alert">{error}</p>}
    {loading ? <p className="empty-state">Loading requests…</p> : !data?.items.length ? <p className="empty-state">You have not started any requests yet. Submit a form that starts a workflow.</p> : <>
      <div className="org-list">{data.items.map(request => <a key={request.id} className="org-option" href={`/requests/${request.id}`}>
        <span><strong>{request.workflow}</strong><small style={{ display: "block" }}>{when(request.startedAt)}{request.currentStep ? ` · At ${request.currentStep}` : ""}</small></span>
        <span className={request.status === "Completed" ? "tag" : "tag tag-warning"}>{request.headline}</span>
      </a>)}</div>
      <div className="designer-actions"><button className="btn" disabled={page <= 1} onClick={() => { setLoading(true); setPage(page - 1); }}>Previous</button><span>Page {page} · {data.total} requests</span>
        <button className="btn" disabled={page * data.pageSize >= data.total} onClick={() => { setLoading(true); setPage(page + 1); }}>Next</button></div>
    </>}
  </section>;
}

export function MyRequestDetailView({ initial }: { initial: MyRequestDetail }) {
  const request = initial;
  const values = Object.entries(request.request ?? {});
  return <>
    <section className="panel-card" style={{ marginBottom: 24 }}>
      <div className="panel-header"><div><span className="panel-kicker">Version {request.versionNumber} · {stepLabels[request.status] ?? request.status}</span><h2>{request.workflow}</h2>
        <p>Started {when(request.startedAt)}{request.completedAt ? ` · Finished ${when(request.completedAt)}` : ""}{request.currentStep ? ` · At ${request.currentStep}` : ""}</p></div>
        <span className={request.status === "Completed" ? "tag" : "tag tag-warning"}>{request.headline}</span></div>
      {request.openTaskId && <div className={request.headline === "Changes requested" ? "request-callout" : "request-callout is-neutral"}>
        <div><strong>{request.headline === "Changes requested" ? "The approver asked for changes" : request.headline}</strong>
          <span>{request.headline === "Changes requested" ? "Update your request and resubmit it for approval." : "Open the approval to see comments and history."}</span></div>
        <a className={request.headline === "Changes requested" ? "btn btn-primary" : "btn"} href={`/tasks/${request.openTaskId}`}>{request.headline === "Changes requested" ? "Update and resubmit" : "View the approval"}</a>
      </div>}
      <div className="submission-detail"><dl>
        {values.length === 0 ? <div><dt>Request</dt><dd>No values.</dd></div> : values.map(([key, value]) => <div key={key}><dt>{key}</dt><dd>{display(value)}</dd></div>)}
      </dl></div>
    </section>
    <section className="panel-card">
      <div className="panel-header"><div><span className="panel-kicker">History</span><h2>Approval history</h2></div></div>
      {request.approvals.length === 0 ? <p className="empty-state">No approvals were needed for this request.</p> : request.approvals.map(approval => <div key={approval.id}>
        <p><strong>{approval.assigneeRole} approval</strong> · <span className={statusTag(approval.status)}>{statusLabels[approval.status]}</span></p>
        <ApprovalHistory history={approval.history} />
      </div>)}
    </section>
  </>;
}
