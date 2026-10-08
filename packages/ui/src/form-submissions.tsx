"use client";

import { useEffect, useRef, useState } from "react";
import type { PublishedForm, SubmissionDetail, SubmissionHistoryPage, SubmissionValues, SubmissionFile } from "@flowops/types";
import { apiRequest, apiDownload } from "@flowops/api-client";
import { FormRenderer } from "./form-renderer";

export function PortalForm({ form }: { form: PublishedForm }) {
  const requestId = useRef<string | null>(null);
  const [refresh, setRefresh] = useState(0);
  async function submit(values: SubmissionValues, files: Record<string, File>) {
    requestId.current ??= crypto.randomUUID();
    try {
      const request = JSON.stringify({ publicationId: form.id, requestId: requestId.current, values });
      const withFiles = form.fields.some(field => field.type === "File");
      const body = new FormData();
      body.append("request", request);
      Object.entries(files).forEach(([fieldId, file]) => body.append(fieldId, file));
      await apiRequest(`/api/portal/forms/${form.formId}/submissions${withFiles ? "/with-files" : ""}`, { method: "POST", signal: AbortSignal.timeout(90000), body: withFiles ? body : request });
    } catch (error) {
      if (error instanceof Error && error.name === "TimeoutError") throw new Error("Submission timed out. Retry with the same values, or refresh history to check whether it was saved.");
      throw error;
    }
    requestId.current = null; setRefresh(value => value + 1);
  }
  return <><section className="panel-card"><span className="tag">Published version {form.versionNumber}</span><FormRenderer fields={form.fields} title={form.name} description={form.description} onSubmitValues={submit} /></section><SubmissionHistory formId={form.formId} surface="Portal" refresh={refresh} /></>;
}

export function SubmissionHistory({ formId, surface, refresh = 0 }: { formId: string; surface: "Studio" | "Portal"; refresh?: number }) {
  const base = surface === "Studio" ? `/api/forms/${formId}/submissions` : `/api/portal/forms/${formId}/submissions`;
  const [page, setPage] = useState(1);
  const [data, setData] = useState<SubmissionHistoryPage | null>(null);
  const [detail, setDetail] = useState<SubmissionDetail | null>(null);
  const [message, setMessage] = useState("");
  const [loading, setLoading] = useState(true);
  const [opening, setOpening] = useState(false);
  const [downloading, setDownloading] = useState<string | null>(null);
  const [reload, setReload] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    apiRequest<SubmissionHistoryPage>(`${base}?page=${page}`, { signal: AbortSignal.any([controller.signal, AbortSignal.timeout(15000)]) }).then(rows => {
      setData(rows); setMessage(""); setLoading(false);
    }).catch(error => { if (!controller.signal.aborted) { setData(null); setMessage(error.name === "TimeoutError" ? "History took too long to load. Try Refresh history." : error.message); setLoading(false); } });
    return () => controller.abort();
  }, [base, page, refresh, reload]);
  async function open(id: string) {
    setOpening(true); setMessage("");
    try { setDetail(await apiRequest<SubmissionDetail>(`${base}/${id}`, { signal: AbortSignal.timeout(15000) })); }
    catch (error) { setMessage(error instanceof Error ? error.message : "Unable to load submission."); }
    finally { setOpening(false); }
  }
  async function download(file: SubmissionFile) {
    if (!detail || downloading) return;
    setDownloading(file.id); setMessage("");
    try {
      const blob = await apiDownload(`${base}/${detail.submission.id}/files/${file.id}`);
      const url = URL.createObjectURL(blob);
      const link = document.createElement("a"); link.href = url; link.download = file.fileName;
      document.body.appendChild(link); link.click(); link.remove();
      window.setTimeout(() => URL.revokeObjectURL(url), 1000);
    } catch (error) { setMessage(error instanceof Error ? error.message : "Unable to download file."); }
    finally { setDownloading(null); }
  }
  return <section className="panel-card" style={{ marginTop: 24 }} id="submissions">
    <div className="panel-header"><div><span className="panel-kicker">{surface === "Studio" ? "Organization responses" : "Your responses"}</span><h2>Submission history</h2><p>{surface === "Studio" ? "Responses submitted to this form in your active organization." : "Only your own submissions appear here."}</p></div><button type="button" className="btn" disabled={opening} onClick={() => { setLoading(true); setReload(value => value + 1); }}>Refresh history</button></div>
    {message && <p className="notice" role="alert">{message}</p>}
    {loading ? <p className="empty-state">Loading submissions…</p> : !data?.items.length ? <p className="empty-state">No submissions yet.</p> : <>
      <div className="org-list">{data.items.map(item => <button className="org-option" type="button" key={item.id} disabled={opening} onClick={() => open(item.id)}><span><strong>{item.name}</strong><small style={{ display: "block" }}>{new Date(item.submittedAt).toLocaleString()}{surface === "Studio" && ` · ${item.submittedBy || item.email}`}</small></span><span className="tag">Version {item.versionNumber}</span></button>)}</div>
      <div className="designer-actions" style={{ marginTop: 16 }}><button className="btn" disabled={page <= 1 || opening} onClick={() => { setLoading(true); setPage(page - 1); }}>Previous</button><span>Page {page} · {data.total} submissions</span><button className="btn" disabled={page * data.pageSize >= data.total || opening} onClick={() => { setLoading(true); setPage(page + 1); }}>Next</button></div>
    </>}
    {detail && <div className="submission-detail"><div className="panel-header"><div><h3>{detail.form.name}</h3><p>Version {detail.form.versionNumber} · {new Date(detail.submission.submittedAt).toLocaleString()}</p></div><button type="button" className="btn" onClick={() => setDetail(null)}>Close response</button></div>{detail.form.description && <p className="helper">{detail.form.description}</p>}<dl>{detail.form.fields.map(field => {
      const value = detail.submission.values[field.id];
      const file = detail.files?.find(item => item.fieldId === field.id);
      return <div key={field.id}><dt>{field.label}</dt><dd>{field.type === "File" ? file ? <button type="button" className="btn" disabled={!!downloading} onClick={() => download(file)}>{downloading === file.id ? "Downloading…" : `Download ${file.fileName}`} ({(file.size / 1024).toFixed(1)} KB)</button> : "Not provided" : typeof value === "boolean" ? value ? "Yes" : "No" : value === null || value === undefined || value === "" ? "Not provided" : String(value)}</dd></div>;
    })}</dl></div>}
  </section>;
}
