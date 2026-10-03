"use client";

import { useEffect, useState } from "react";
import type { FormDraft, FormField, FormFieldType } from "@flowops/types";
import { apiRequest } from "@flowops/api-client";
import { FormRenderer } from "./form-renderer";

const fieldTypes: FormFieldType[] = ["Text", "Textarea", "Number", "Currency", "Date", "Dropdown", "Checkbox", "File"];
export function FormDesigner({ initialForm }: { initialForm: FormDraft }) {
  const [fields, setFields] = useState(initialForm.fields);
  const [revision, setRevision] = useState(initialForm.draftRevision);
  const [selectedId, setSelectedId] = useState<string | null>(initialForm.fields[0]?.id ?? null);
  const [preview, setPreview] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [failed, setFailed] = useState(false);
  const selected = fields.find(field => field.id === selectedId);
  useEffect(() => {
    if (!dirty) return;
    const warn = (event: BeforeUnloadEvent) => { event.preventDefault(); };
    const navigate = (event: MouseEvent) => {
      const link = event.target instanceof Element ? event.target.closest("a[href]") : null;
      if (!link || event.defaultPrevented) return;
      if (!window.confirm("Leave this form and discard unsaved field changes?")) {
        event.preventDefault(); event.stopPropagation();
      } else { window.removeEventListener("beforeunload", warn); }
    };
    window.addEventListener("beforeunload", warn);
    document.addEventListener("click", navigate, true);
    return () => { window.removeEventListener("beforeunload", warn); document.removeEventListener("click", navigate, true); };
  }, [dirty]);
  function change(next: FormField[]) { setFields(next); setDirty(true); setMessage(""); }
  function patch(update: Partial<FormField>) { change(fields.map(field => field.id === selectedId ? { ...field, ...update } : field)); }
  function add(type: FormFieldType) {
    const field: FormField = { id: crypto.randomUUID(), type, label: `${type} field`, required: false,
      ...(type === "Dropdown" ? { options: ["Option 1", "Option 2"] } : {}),
      ...(type === "File" ? { maxFileSizeMb: 5, acceptedExtensions: [".pdf", ".png", ".jpg"] } : {}) };
    change([...fields, field]); setSelectedId(field.id);
  }
  function move(index: number, direction: number) {
    const next = [...fields]; [next[index], next[index + direction]] = [next[index + direction], next[index]]; change(next);
  }
  async function save() {
    setBusy(true); setMessage(""); setFailed(false);
    try {
      const form = await apiRequest<FormDraft>(`/api/forms/${initialForm.id}/fields`, { method: "PUT", body: JSON.stringify({ fields, draftRevision: revision }) });
      setRevision(form.draftRevision); setFields(form.fields); setDirty(false); setMessage("Fields saved.");
    } catch (error) { setFailed(true); setMessage(error instanceof Error ? error.message : "Unable to save fields."); }
    finally { setBusy(false); }
  }
  return <section className="panel-card">
    <div className="panel-header"><div><span className="panel-kicker">Draft form</span><h2>{initialForm.name}</h2><p>{dirty ? "Unsaved changes" : "All changes saved"}. Preview does not submit data.</p></div>
      <div className="designer-actions"><button className="btn" onClick={() => setPreview(!preview)}>{preview ? "Back to designer" : "Preview"}</button>
        <button className="btn btn-primary" disabled={busy || !dirty} onClick={save}>{busy ? "Saving…" : "Save fields"}</button></div></div>
    {message && <p className="notice" role={failed ? "alert" : "status"}>{message}</p>}
    {preview ? <FormRenderer key={JSON.stringify(fields)} fields={fields} title={initialForm.name} description={initialForm.description} /> :
      <fieldset disabled={busy} className="designer-layout">
        <div className="designer-palette"><h3>Add field</h3>{fieldTypes.map(type => <button type="button" className="btn" key={type} disabled={fields.length >= 50} onClick={() => add(type)}>{type}</button>)}<small className="helper">{fields.length}/50 fields</small></div>
        <div className="designer-canvas"><h3>Fields</h3>{fields.length === 0 && <p className="empty-state">Choose a field type to begin.</p>}
          {fields.map((field, index) => <div className={`designer-field ${field.id === selectedId ? "designer-field-selected" : ""}`} key={field.id}>
            <button type="button" className="designer-select" onClick={() => setSelectedId(field.id)}><strong>{index + 1}. {field.label}{field.required ? " *" : ""}</strong><small>{field.type}</small></button>
            <div className="designer-actions"><button className="btn" type="button" aria-label={`Move ${field.label} up`} disabled={index === 0} onClick={() => move(index, -1)}>↑</button>
              <button className="btn" type="button" aria-label={`Move ${field.label} down`} disabled={index === fields.length - 1} onClick={() => move(index, 1)}>↓</button>
              <button type="button" className="text-button text-button-danger" onClick={() => { change(fields.filter(item => item.id !== field.id)); if (selectedId === field.id) setSelectedId(null); }}>Remove</button></div>
          </div>)}</div>
        <div className="stack-form"><h3>Field settings</h3>{selected ? <>
          <label className="field">Label<input maxLength={120} value={selected.label} onChange={event => patch({ label: event.target.value })} /></label>
          <label className="field">Help text<textarea maxLength={500} value={selected.helpText ?? ""} onChange={event => patch({ helpText: event.target.value })} /></label>
          <label><input type="checkbox" checked={selected.required} onChange={event => patch({ required: event.target.checked })} /> Required</label>
          {(selected.type === "Text" || selected.type === "Textarea") && <label className="field">Maximum characters<input type="number" min={1} max={10000} value={selected.maxLength ?? ""} onChange={event => patch({ maxLength: event.target.value === "" ? null : Number(event.target.value) })} /></label>}
          {(selected.type === "Number" || selected.type === "Currency") && <>{(["min", "max"] as const).map(limit => <label className="field" key={limit}>{limit === "min" ? "Minimum" : "Maximum"}<input type="number" step={selected.type === "Currency" ? "0.01" : "any"} value={selected[limit] ?? ""} onChange={event => patch({ [limit]: event.target.value === "" ? null : Number(event.target.value) })} /></label>)}</>}
          {selected.type === "Date" && <>{(["minDate", "maxDate"] as const).map(limit => <label className="field" key={limit}>{limit === "minDate" ? "Earliest date" : "Latest date"}<input type="date" value={selected[limit] ?? ""} onChange={event => patch({ [limit]: event.target.value || null })} /></label>)}</>}
          {selected.type === "Dropdown" && <label className="field">Options (one per line)<textarea value={selected.options?.join("\n") ?? ""} onChange={event => patch({ options: event.target.value.split("\n") })} /></label>}
          {selected.type === "File" && <><label className="field">Maximum file size (MB)<input type="number" min={1} max={10} value={selected.maxFileSizeMb ?? ""} onChange={event => patch({ maxFileSizeMb: event.target.value === "" ? null : Number(event.target.value) })} /></label>
            <label className="field">Allowed extensions (comma separated)<input value={selected.acceptedExtensions?.join(", ") ?? ""} onChange={event => patch({ acceptedExtensions: event.target.value.split(",").map(value => value.trim().toLowerCase()) })} /></label><p className="helper">PDF, PNG, JPG, JPEG, TXT, CSV. Upload storage comes later.</p></>}
        </> : <p className="empty-state">Select a field to configure it.</p>}</div>
      </fieldset>}
  </section>;
}
