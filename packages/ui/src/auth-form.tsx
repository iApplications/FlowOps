"use client";

import { useEffect, useState, type FormEvent } from "react";
import { apiRequest } from "@flowops/api-client";
import { AuthFrame } from "./shell";
import { PasswordInput } from "./password-input";

type Mode = "login" | "register" | "forgot" | "reset" | "resend";

export function AuthForm({ mode, surface }: { mode: Mode; surface: "Studio" | "Portal" }) {
  const [email, setEmail] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [password, setPassword] = useState("");
  const [code, setCode] = useState("");
  const [token, setToken] = useState("");
  const [requiresTwoFactor, setRequiresTwoFactor] = useState(false);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");

  useEffect(() => {
    if (mode === "reset") {
      const params = new URLSearchParams(window.location.search);
      setEmail(params.get("email") ?? "");
      setToken(params.get("token") ?? "");
    }
    if (mode === "login") {
      const params = new URLSearchParams(window.location.search);
      if (params.has("registered")) setMessage("Account created. Check your email to confirm it before signing in.");
      if (params.has("reset")) setMessage("Password updated. Sign in with your new password.");
    }
  }, [mode]);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setMessage("");
    try {
      if (mode === "login") {
        const result = await apiRequest<{ requiresTwoFactor?: boolean }>("/api/auth/login", {
          method: "POST", body: JSON.stringify({ email, password, code: code || null, surface }),
        });
        if (result.requiresTwoFactor) {
          setRequiresTwoFactor(true);
          setMessage("Enter your authenticator or recovery code.");
        } else window.location.assign("/");
      } else if (mode === "register") {
        await apiRequest("/api/auth/register", {
          method: "POST", body: JSON.stringify({ email, password, displayName, surface }),
        });
        window.location.assign("/login?registered=1");
      } else if (mode === "forgot" || mode === "resend") {
        await apiRequest(mode === "forgot" ? "/api/auth/password/reset-request" : "/api/auth/email/resend", {
          method: "POST", body: JSON.stringify({ email, surface }),
        });
        setMessage(mode === "forgot" ? "If this account exists, a reset link has been sent. Check Mailpit during local development." : "If this account needs confirmation, a new link has been sent.");
      } else {
        await apiRequest("/api/auth/password/reset", {
          method: "POST", body: JSON.stringify({ email, token, newPassword: password, surface }),
        });
        window.location.assign("/login?reset=1");
      }
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Request failed.");
    } finally {
      setBusy(false);
    }
  }

  const title = { login: "Sign in", register: "Create account", forgot: "Reset password", reset: "Choose a new password", resend: "Resend confirmation" }[mode];
  const intro = {
    login: "Welcome back. Sign in to continue to your workspace.",
    register: "Set up your account and confirm your email to get started.",
    forgot: "We’ll send a secure reset link to your confirmed email address.",
    reset: "Choose a strong new password for your FlowOps account.",
    resend: "Enter your email and we’ll send another confirmation link.",
  }[mode];
  return (
    <AuthFrame surface={surface} title={title} intro={intro}>
        <form onSubmit={submit}>
          <label className="field">Email address
            <input type="email" autoComplete="email" required readOnly={mode === "reset"} value={email} onChange={e => setEmail(e.target.value)} placeholder="you@company.com" />
          </label>
          {mode === "register" && <label className="field">Display name
            <input required maxLength={120} value={displayName} onChange={e => setDisplayName(e.target.value)} placeholder="How should we call you?" />
          </label>}
          {(mode === "login" || mode === "register" || mode === "reset") && <label className="field">{mode === "reset" ? "New password" : "Password"}
            <PasswordInput autoComplete={mode === "login" ? "current-password" : "new-password"} required minLength={mode === "login" ? undefined : 12} value={password} onChange={e => setPassword(e.target.value)} placeholder="Enter your password" />
          </label>}
          {requiresTwoFactor && <label className="field">Authenticator or recovery code
            <input autoComplete="one-time-code" required value={code} onChange={e => setCode(e.target.value)} placeholder="6-digit code or recovery code" />
          </label>}
          {(mode === "register" || mode === "reset") && <p className="helper">Use at least 12 characters with uppercase, lowercase, a number, and a symbol.</p>}
          {mode === "register" && surface === "Studio" && <p className="helper">After confirming your email, sign in here to create your business workspace as Owner and set up MFA.</p>}
          <button disabled={busy} className="btn btn-primary auth-submit">{busy ? "Working…" : title}</button>
        </form>
        {message && <p role="status" className="notice">{message}</p>}
        <nav className="auth-links" aria-label="Account links">
          {mode !== "login" && <a href="/login">Sign in</a>}
          {mode !== "register" && <a href="/register">Create account</a>}
          {mode === "login" && <a href="/forgot-password">Forgot password?</a>}
          {mode === "login" && <a href="/resend-confirmation">Resend confirmation</a>}
        </nav>
    </AuthFrame>
  );
}
