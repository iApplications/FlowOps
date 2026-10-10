"use client";

import { useEffect, useState, type FormEvent } from "react";
import type { WorkflowDetail, WorkflowRunDetail as RunDetail, WorkflowRunPage, WorkflowRunStatus } from "@flowops/types";
import { apiRequest } from "@flowops/api-client";
import { ApprovalHistory } from "./approval-tasks";

function statusTag(status: WorkflowRunStatus | string) {
  return status === "Failed" || status === "Waiting" ? "tag tag-warning" : "tag";
}

function when(value: string | null) {
  return value ? new Date(value).toLocaleString() : "—";
}

// Runs of one workflow: start a manual run of the latest published version and browse run history.
export function WorkflowRuns({ workflow }: { workflow: WorkflowDetail }) {
  const [data, setData] = useState<WorkflowRunPage | null>(null);
  const [page, setPage] = useState(1);
  const [reload, setReload] = useState(0);
  const [loading, setLoading] = useState(true);
  const [values, setValues] = useState("{\n  \"amount\": 1200\n}");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [failed, setFailed] = useState(false);
  useEffect(() => {
    let cancelled = false;
    apiRequest<WorkflowRunPage>(`/api/workflows/${workflow.id}/runs?page=${page}`).then(rows => { if (!cancelled) setData(rows); })
      .catch(error => { if (!cancelled) { setFailed(true); setMessage(error.message); } })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [workflow.id, page, reload]);

  async function start(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    let request: unknown;
    try { request = values.trim() ? JSON.parse(values) : {}; } catch { request = null; }
    if (!request || typeof request !== "object" || Array.isArray(request)) {
      setFailed(true); setMessage("Request values must be a JSON object, for example { \"amount\": 1200 }."); return;
    }
    setBusy(true); setMessage(""); setFailed(false);
    try {
      // One key per click: a network retry of this request cannot start a second run.
      const run = await apiRequest<RunDetail>(`/api/workflows/${workflow.id}/runs`, {
        method: "POST", headers: { "Idempotency-Key": crypto.randomUUID() }, body: JSON.stringify({ request }),
      });
      setFailed(run.status === "Failed");
      setMessage(run.status === "Failed" ? `Run failed: ${run.errorMessage}` : run.status === "Waiting" ? "Run started and is waiting for approval." : `Run completed: ${run.outcome}.`);
      setLoading(true); setPage(1); setReload(value => value + 1);
    } catch (error) { setFailed(true); setMessage(error instanceof Error ? error.message : "Unable to start the run."); }
    finally { setBusy(false); }
  }

  const published = workflow.latestPublished !== null;
  return <>
    <section className="panel-card" style={{ marginBottom: 24 }}>
      <div className="panel-header"><div><span className="panel-kicker">{workflow.latestPublished ? `Published version ${workflow.latestPublished.versionNumber}` : "Not published yet"}</span><h2>Run {workflow.name}</h2>
        <p>Starts a run of the latest published version. The values become <code>request.*</code> variables for conditions, for example <code>request.amount</code>.</p></div></div>
      {!published ? <p className="empty-state">Publish this workflow in the designer before running it.</p> :
        <form onSubmit={start} className="stack-form">
          <label className="field">Request values (JSON)<textarea rows={5} value={values} disabled={busy} spellCheck={false} style={{ fontFamily: "monospace" }} onChange={event => setValues(event.target.value)} /></label>
          <div className="designer-actions"><button className="btn btn-primary" disabled={busy}>{busy ? "Running…" : "Start run"}</button></div>
        </form>}
      {message && <p className={failed ? "notice" : "notice notice-success"} role={failed ? "alert" : "status"}>{message}</p>}
    </section>
    <section className="panel-card">
      <div className="panel-header"><div><span className="panel-kicker">History</span><h2>Runs</h2><p>Each run stays on the version it started with.</p></div>
        <button type="button" className="btn" onClick={() => { setLoading(true); setReload(value => value + 1); }}>Refresh</button></div>
      {loading ? <p className="empty-state">Loading runs…</p> : !data?.items.length ? <p className="empty-state">No runs yet.</p> : <>
        <div className="org-list">{data.items.map(run => <a key={run.id} className="org-option" href={`/workflows/${workflow.id}/runs/${run.id}`}>
          <span><strong>{when(run.startedAt)}</strong><small style={{ display: "block" }}>Version {run.versionNumber} · {run.trigger === "Manual" ? "Manual" : "Form submitted"}{run.startedBy ? ` · ${run.startedBy}` : ""}{run.errorMessage ? ` · ${run.errorMessage}` : ""}</small></span>
          <span className="designer-actions"><span className={statusTag(run.status)}>{run.status}</span>{run.outcome && <span className="tag">{run.outcome}</span>}</span>
        </a>)}</div>
        <div className="designer-actions"><button className="btn" disabled={page <= 1} onClick={() => { setLoading(true); setPage(page - 1); }}>Previous</button><span>Page {page} · {data.total} runs</span>
          <button className="btn" disabled={page * data.pageSize >= data.total} onClick={() => { setLoading(true); setPage(page + 1); }}>Next</button></div>
      </>}
    </section>
  </>;
}

function json(value: unknown) {
  return value === null || value === undefined ? "—" : JSON.stringify(value, null, 2);
}

// One run: its pinned version, variables, and every execution with the steps it ran.
export function WorkflowRunDetail({ run }: { run: RunDetail }) {
  const labels = new Map(run.version.graph.nodes.map(node => [node.id, node.label]));
  return <>
    <section className="panel-card" style={{ marginBottom: 24 }}>
      <div className="panel-header"><div><span className="panel-kicker">{run.workflow.name} · Version {run.version.versionNumber}</span><h2>Run {run.status.toLowerCase()}{run.outcome ? `: ${run.outcome}` : ""}</h2>
        <p>Started {when(run.startedAt)}{run.startedBy ? ` by ${run.startedBy}` : ""} · {run.trigger === "Manual" ? "Manual" : "Form submitted"} · Finished {when(run.completedAt)}</p>
        {run.sourceSubmissionId && <p className="helper">Submission <code>{run.sourceSubmissionId}</code></p>}</div>
        <span className={statusTag(run.status)}>{run.status}</span></div>
      {run.status === "Waiting" && run.waitingNodeId && <p className="notice">Waiting at <strong>{labels.get(run.waitingNodeId) ?? run.waitingNodeId}</strong>, waiting for an approval decision.</p>}
      {run.errorMessage && <p className="notice" role="alert"><strong>{run.errorCode}</strong>: {run.errorMessage}</p>}
      <div className="submission-detail"><dl>
        <div><dt>Correlation ID</dt><dd><code>{run.correlationId}</code></dd></div>
        <div><dt>Run ID</dt><dd><code>{run.id}</code></dd></div>
        <div><dt>Variables</dt><dd><pre style={{ margin: 0, whiteSpace: "pre-wrap" }}>{json(run.variables)}</pre></dd></div>
      </dl></div>
    </section>
    {run.approvals.map(approval => <section className="panel-card" style={{ marginBottom: 24 }} key={approval.id}>
      <div className="panel-header"><div><span className="panel-kicker">Approval · {labels.get(approval.nodeId) ?? approval.nodeId}</span><h2>{approval.assigneeRole} approval</h2>
        <p>Raised {when(approval.createdAt)}{approval.decidedAt ? ` · Decided ${when(approval.decidedAt)}` : ""}</p></div>
        <span className={statusTag(approval.status)}>{approval.status}</span></div>
      <ApprovalHistory history={approval.history} />
    </section>)}
    {run.executions.map(execution => <section className="panel-card" style={{ marginBottom: 24 }} key={execution.id}>
      <div className="panel-header"><div><span className="panel-kicker">Execution {execution.sequence} · {execution.reason}</span><h2>{execution.steps.length} {execution.steps.length === 1 ? "step" : "steps"}</h2>
        <p>{when(execution.startedAt)}{execution.triggeredBy ? ` · ${execution.triggeredBy}` : ""} · Correlation <code>{execution.correlationId}</code></p></div>
        <span className={statusTag(execution.status)}>{execution.status}</span></div>
      <ol className="org-list" style={{ listStyle: "none", padding: 0 }}>{execution.steps.map(step => <li key={step.id} className="org-option" style={{ display: "block" }}>
        <div className="designer-actions" style={{ justifyContent: "space-between" }}>
          <span><strong>{step.sequence}. {labels.get(step.nodeId) ?? step.nodeId}</strong><small style={{ display: "block" }}>{step.nodeType} · {step.durationMs} ms</small></span>
          <span className={statusTag(step.status)}>{step.status}</span></div>
        {step.errorMessage && <p className="notice" role="alert"><strong>{step.errorCode}</strong>: {step.errorMessage}</p>}
        <details style={{ marginTop: 8 }}><summary className="helper">Input and output</summary>
          <pre style={{ whiteSpace: "pre-wrap", fontSize: 12 }}>Input: {json(step.input)}{"\n"}Output: {json(step.output)}</pre></details>
      </li>)}</ol>
    </section>)}
  </>;
}
