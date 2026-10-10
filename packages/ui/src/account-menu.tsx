"use client";

import { useEffect, useRef, useState } from "react";
import type { CurrentUser, OrganizationSummary } from "@flowops/types";
import { apiRequest } from "@flowops/api-client";

// Top-right account menu shared by the Portal header and the Studio top bar: who is signed in, the active organization, switching organization,
// profile and security shortcuts, and sign out.
export function AccountMenu({ user }: { user: CurrentUser }) {
  const [open, setOpen] = useState(false);
  const [organizations, setOrganizations] = useState<OrganizationSummary[] | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const root = useRef<HTMLDivElement>(null);
  const active = user.activeOrganization;

  useEffect(() => {
    if (!open) return;
    const close = (event: MouseEvent) => { if (!root.current?.contains(event.target as Node)) setOpen(false); };
    const escape = (event: KeyboardEvent) => { if (event.key === "Escape") setOpen(false); };
    document.addEventListener("mousedown", close);
    document.addEventListener("keydown", escape);
    return () => { document.removeEventListener("mousedown", close); document.removeEventListener("keydown", escape); };
  }, [open]);

  useEffect(() => {
    if (!open || organizations) return;
    apiRequest<OrganizationSummary[]>("/api/organizations").then(setOrganizations).catch(() => setError("Unable to load organizations."));
  }, [open, organizations]);

  async function run(work: () => Promise<unknown>, then: () => void) {
    if (busy) return;
    setBusy(true); setError("");
    try { await work(); then(); }
    catch (failure) { setError(failure instanceof Error ? failure.message : "Request failed."); setBusy(false); }
  }

  return <div className="account-menu" ref={root}>
    <button type="button" className="portal-account" aria-haspopup="menu" aria-expanded={open} onClick={() => setOpen(value => !value)}>
      <span className="avatar" aria-hidden="true">{user.displayName.slice(0, 1).toUpperCase()}</span>
      <span className="portal-account-text"><strong>{user.displayName}</strong><small>{active ? `${active.name} · ${active.role}` : "No organization yet"}</small></span>
      <span className="account-menu-caret" aria-hidden="true">▾</span>
    </button>
    {open && <div className="account-menu-panel" role="menu">
      <div className="account-menu-head"><strong>{user.displayName}</strong><small>{user.email}</small></div>
      {organizations && organizations.length > 0 && <div className="account-menu-section">
        <p className="account-menu-label">Organization</p>
        {organizations.map(org => <button key={org.organizationId} type="button" role="menuitemradio" aria-checked={org.organizationId === active?.organizationId}
          className="account-menu-item" disabled={busy || org.organizationId === active?.organizationId}
          onClick={() => run(() => apiRequest(`/api/organizations/${org.organizationId}/activate`, { method: "POST" }), () => window.location.reload())}>
          <span className="org-monogram" aria-hidden="true">{org.name.slice(0, 1).toUpperCase()}</span>
          <span><strong>{org.name}</strong><small>{org.role}</small></span>
          {org.organizationId === active?.organizationId && <span className="account-menu-check" aria-hidden="true">✓</span>}
        </button>)}
      </div>}
      <div className="account-menu-section">
        <a className="account-menu-item" role="menuitem" href="/#profile"><span>Profile</span></a>
        <a className="account-menu-item" role="menuitem" href="/#security"><span>Security</span></a>
        <a className="account-menu-item" role="menuitem" href="/#organizations"><span>Organizations</span></a>
      </div>
      <div className="account-menu-section">
        <button type="button" role="menuitem" className="account-menu-item account-menu-signout" disabled={busy}
          onClick={() => run(() => apiRequest("/api/auth/logout", { method: "POST" }), () => window.location.assign("/login"))}>
          <span>Sign out</span>
        </button>
      </div>
      {error && <p className="notice" role="alert" style={{ margin: "8px 12px 12px" }}>{error}</p>}
    </div>}
  </div>;
}
