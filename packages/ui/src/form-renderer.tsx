"use client";

import { useState, type FormEvent } from "react";
import type { FormField } from "@flowops/types";

export function FormRenderer({ fields, title, description }: { fields: FormField[]; title: string; description: string }) {
  const [message, setMessage] = useState("");
  function validate(event: FormEvent) { event.preventDefault(); setMessage("Preview validation passed. Nothing was submitted."); }
  return <form className="stack-form form-preview" onSubmit={validate}>
    <h3>{title}</h3>{description && <p className="helper">{description}</p>}
    {fields.length === 0 && <p className="empty-state">Add fields to preview your form.</p>}
    {fields.map(field => {
      const inputId = `preview-${field.id}`;
      const common = { id: inputId, name: field.id, required: field.required, "aria-describedby": field.helpText ? `${inputId}-help` : undefined };
      return <div className="field" key={field.id}>
        <label htmlFor={inputId}>{field.label}{field.required ? " *" : ""}</label>
        {field.type === "Textarea" ? <textarea {...common} maxLength={field.maxLength ?? undefined} /> :
          field.type === "Dropdown" ? <select {...common}><option value="">Choose an option</option>{field.options?.map((option, index) => <option key={index} value={option}>{option}</option>)}</select> :
          field.type === "Checkbox" ? <input {...common} type="checkbox" /> :
          field.type === "File" ? <><input {...common} type="file" accept={field.acceptedExtensions?.join(",")} onChange={event => {
            const file = event.target.files?.[0];
            const extension = file ? `.${file.name.split(".").pop()?.toLowerCase()}` : "";
            event.target.setCustomValidity(file && file.size > (field.maxFileSizeMb ?? 10) * 1024 * 1024 ? `File must be ${field.maxFileSizeMb} MB or smaller.` :
              file && !field.acceptedExtensions?.includes(extension) ? "Choose an allowed file type." : "");
          }} /><small className="helper">{field.acceptedExtensions?.join(", ")} · up to {field.maxFileSizeMb} MB. Preview files stay in your browser.</small></> :
          <input {...common} type={field.type === "Date" ? "date" : field.type === "Number" || field.type === "Currency" ? "number" : "text"}
            maxLength={field.maxLength ?? undefined} min={field.type === "Date" ? field.minDate ?? undefined : field.min ?? undefined}
            max={field.type === "Date" ? field.maxDate ?? undefined : field.max ?? undefined} step={field.type === "Currency" ? "0.01" : "any"} />}
        {field.helpText && <small className="helper" id={`${inputId}-help`}>{field.helpText}</small>}
      </div>;
    })}
    {fields.length > 0 && <button className="btn" type="submit">Check validation</button>}
    {message && <p className="notice" role="status">{message}</p>}
  </form>;
}
