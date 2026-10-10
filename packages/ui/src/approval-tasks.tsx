"use client";

import { useEffect, useState, type FormEvent } from "react";
import type { ApprovalTaskDecision, ApprovalTaskDetail, ApprovalTaskEvent, ApprovalTaskPage, ApprovalTaskStatus } from "@flowops/types";
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
  return typeof value === "object" ? JSON.stringify(value) : String(value);
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
    <section className="panel-card" style={{ marginBottom: 24 }}>
      <div className="panel-header"><div><span className="panel-kicker">Decision</span><h2>{task.canDecide ? "Your decision" : "Status"}</h2>
        <p>{task.canDecide ? "The first decision is final. Comments are kept in the approval history." : task.status === "Pending" ? "You can comment on this request but cannot decide it." : `This task is ${statusLabels[task.status].toLowerCase()}.`}</p></div></div>
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
