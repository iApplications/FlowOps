"use client";

import { useEffect, useRef, useState } from "react";
import { apiRequest } from "@flowops/api-client";
import { AuthFrame } from "./shell";

export function ConfirmEmail({ surface }: { surface: "Studio" | "Portal" }) {
  const submitted = useRef(false);
  const [message, setMessage] = useState("Confirming your email…");
  useEffect(() => {
    if (submitted.current) return;
    submitted.current = true;
    const params = new URLSearchParams(window.location.search);
    const email = params.get("email");
    const token = params.get("token");
    if (!email || !token) { setMessage("This confirmation link is incomplete."); return; }
    apiRequest("/api/auth/email/confirm", {
      method: "POST", body: JSON.stringify({ email, token, surface }),
    }).then(() => setMessage("Email confirmed. You can now sign in."))
      .catch(error => setMessage(error instanceof Error ? error.message : "Confirmation failed."));
  }, [surface]);
  return <AuthFrame surface={surface} title="Email confirmation" intro="One quick check before your workspace is ready.">
    <p role="status" className="notice">{message}</p>
    <a className="btn btn-primary" style={{ marginTop: 22 }} href="/login">Go to sign in</a>
  </AuthFrame>;
}
