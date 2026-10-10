"use client";

import { useRef, useState, type FormEvent } from "react";
import type { FormField, SubmissionValues } from "@flowops/types";

export function FormRenderer({ fields, title, description, readOnly = false, onSubmitValues }: { fields: FormField[]; title: string; description: string; readOnly?: boolean; onSubmitValues?: (values: SubmissionValues, files: Record<string, File>) => Promise<void> }) {
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);
  const submitting = useRef(false);
  async function validate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (submitting.current || readOnly) return;
    setFailed(false);
    if (!onSubmitValues) { setMessage("Preview validation passed. Nothing was submitted."); return; }
    const form = event.currentTarget;
    const data = new FormData(form);
    const values: SubmissionValues = {};
    const files: Record<string, File> = {};
    fields.forEach(field => {
      const raw = data.get(field.id);
      if (field.type === "File") { if (raw instanceof File && raw.name) files[field.id] = raw; return; }
      values[field.id] = field.type === "Checkbox" ? raw === "on" : raw === null || raw === "" ? null : field.type === "Number" || field.type === "Currency" ? Number(raw) : String(raw);
    });
    if (Object.values(files).reduce((total, file) => total + file.size, 0) > 20 * 1024 * 1024) {
      setFailed(true); setMessage("Files must total 20 MB or less."); return;
    }
    submitting.current = true; setBusy(true); setMessage("");
    try { await onSubmitValues(values, files); form.reset(); setMessage("Form submitted successfully. Your response is available in submission history."); }
    catch (error) { setFailed(true); setMessage(error instanceof Error ? error.message : "Unable to submit form."); }
    finally { submitting.current = false; setBusy(false); }
  }
  return <form className="stack-form form-preview" onSubmit={validate}>
    <h3>{title}</h3>{description && <p className="helper">{description}</p>}
    {fields.length === 0 && <p className="empty-state">Add fields to preview your form.</p>}
    {fields.map(field => {
      const inputId = `preview-${field.id}`;
      const common = { id: inputId, name: field.id, required: field.required, disabled: readOnly || busy, "aria-describedby": field.helpText ? `${inputId}-help` : undefined };
      if (field.type === "Checkbox") return <div className="field field-check" key={field.id}>
        <input {...common} type="checkbox" />
        <label htmlFor={inputId}>{field.label}{field.required ? " *" : ""}</label>
        {field.helpText && <small className="helper" id={`${inputId}-help`}>{field.helpText}</small>}
      </div>;
      return <div className="field" key={field.id}>
        <label htmlFor={inputId}>{field.label}{field.required ? " *" : ""}</label>
        {field.type === "Textarea" ? <textarea {...common} maxLength={field.maxLength ?? 10000} /> :
          field.type === "Dropdown" ? <select {...common}><option value="">Choose an option</option>{field.options?.map((option, index) => <option key={index} value={option}>{option}</option>)}</select> :
          field.type === "File" ? <><input {...common} type="file" accept={field.acceptedExtensions?.join(",")} onChange={event => {
            const file = event.target.files?.[0];
            const extension = file ? `.${file.name.split(".").pop()?.toLowerCase()}` : "";
            event.target.setCustomValidity(file && file.size === 0 ? "Choose a non-empty file." : file && file.size > (field.maxFileSizeMb ?? 10) * 1024 * 1024 ? `File must be ${field.maxFileSizeMb} MB or smaller.` :
              file && !field.acceptedExtensions?.includes(extension) ? "Choose an allowed file type." : "");
          }} /><small className="helper">{field.acceptedExtensions?.join(", ")} · up to {field.maxFileSizeMb} MB. {onSubmitValues ? "Uploaded with your response. Total files: up to 20 MB." : "Preview files stay in your browser."}</small></> :
          <input {...common} type={field.type === "Date" ? "date" : field.type === "Number" || field.type === "Currency" ? "number" : "text"}
            maxLength={field.type === "Text" ? field.maxLength ?? 10000 : undefined} min={field.type === "Date" ? field.minDate ?? undefined : field.min ?? undefined}
            max={field.type === "Date" ? field.maxDate ?? undefined : field.max ?? undefined} step={field.type === "Currency" ? "0.01" : "any"} />}
        {field.helpText && <small className="helper" id={`${inputId}-help`}>{field.helpText}</small>}
      </div>;
    })}
    {!readOnly && fields.length > 0 && <button className="btn btn-primary" disabled={busy} type="submit">{busy ? "Submitting…" : onSubmitValues ? "Submit form" : "Check validation"}</button>}
    {message && <p className="notice" role={failed ? "alert" : "status"}>{message}</p>}
  </form>;
}
