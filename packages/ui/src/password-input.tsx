"use client";

import { useId, useState, type ComponentProps } from "react";

export function PasswordInput(props: Omit<ComponentProps<"input">, "type">) {
  const generatedId = useId();
  const id = props.id ?? generatedId;
  const [visible, setVisible] = useState(false);

  return <div className="password-control" onBlur={event => {
    if (!event.currentTarget.contains(event.relatedTarget)) setVisible(false);
  }}>
    <input {...props} id={id} type={visible ? "text" : "password"} />
    <button type="button" className="password-toggle" aria-controls={id}
      aria-label={visible ? "Hide password" : "Show password"}
      onClick={() => setVisible(current => !current)}>
      {visible ? "Hide" : "Show"}
    </button>
  </div>;
}
