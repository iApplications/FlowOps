"use client";

import { useEffect, useState, type FormEvent } from "react";
import { apiRequest } from "@flowops/api-client";
import type { CurrentUser, OrganizationSummary } from "@flowops/types";
import { PasswordInput } from "./password-input";
import { QRCodeSVG } from "qrcode.react";

export function WorkspaceAccount({ initialUser, surface, onboarding = false }: { initialUser: CurrentUser; surface: "Studio" | "Portal"; onboarding?: boolean }) {
  const [user, setUser] = useState(initialUser);
  const [organizations, setOrganizations] = useState<OrganizationSummary[]>([]);
  const [organizationName, setOrganizationName] = useState("");
  const [displayName, setDisplayName] = useState(initialUser.displayName);
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [mfaKey, setMfaKey] = useState("");
  const [mfaUri, setMfaUri] = useState("");
  const [mfaSetupBusy, setMfaSetupBusy] = useState(false);
  const [mfaCode, setMfaCode] = useState("");
  const [disablePassword, setDisablePassword] = useState("");
  const [recoveryCodes, setRecoveryCodes] = useState<string[]>([]);
  const [message, setMessage] = useState("");
  const active = user.activeOrganization;
  const privileged = active?.role === "Owner" || active?.role === "Admin";
  const canManage = privileged && (!active?.requireMfaForPrivileged || user.mfaSatisfied);
  const canCreateOrganization = surface === "Studio" && user.canCreateWorkspace;

  useEffect(() => {
    apiRequest<OrganizationSummary[]>("/api/organizations").then(setOrganizations)
      .catch(error => setMessage(error.message));
  }, []);
  async function refresh() {
    const [updatedUser, updatedOrganizations] = await Promise.all([
      apiRequest<CurrentUser>("/api/auth/me"),
      apiRequest<OrganizationSummary[]>("/api/organizations"),
    ]);
    setUser(updatedUser);
    setOrganizations(updatedOrganizations);
  }

  async function action(work: () => Promise<unknown>, success: string, reload = false) {
    setMessage("");
    try {
      await work();
      if (reload) window.location.reload();
      else { await refresh(); setMessage(success); }
    } catch (error) { setMessage(error instanceof Error ? error.message : "Request failed."); }
  }

  async function createOrganization(event: FormEvent) {
    event.preventDefault();
    if (!canCreateOrganization) return;
    await action(() => apiRequest("/api/organizations", { method: "POST", body: JSON.stringify({ name: organizationName }) }), "Organization created.", true);
  }

  return (
    <div>
      {onboarding && canManage && <div className="notice" style={{ margin: "0 0 20px" }}><strong>Your workspace is ready.</strong> Save any recovery codes shown below before continuing. <a className="btn btn-primary" href="/">Enter Studio</a></div>}
      {message && <p role="status" className="notice" style={{ margin: "0 0 20px" }}>{message}</p>}
      {surface === "Studio" && privileged && active?.requireMfaForPrivileged && !user.mfaSatisfied &&
        <div className="notice" style={{ margin: "0 0 20px" }}><strong>Authenticator required.</strong> Set up MFA in Security to manage {active.name} in {surface}.</div>}
      <div className="workspace-grid">
        <div className="workspace-main">
          <section className="panel-card" id="organizations">
            <div className="panel-header"><div><span className="panel-kicker">Spaces</span><h2>Organizations</h2><p>{canCreateOrganization ? "Choose where you work or create a new space." : "Choose an organization you have been added to."}</p></div><span className="tag">{organizations.length} total</span></div>
            <div className="org-list">
              {organizations.length === 0 && <p className="empty-state">{canCreateOrganization ? "No organizations yet. Create one below to begin." : "No organizations yet. Ask your organization administrator to add your account."}</p>}
              {organizations.map(org => <button key={org.organizationId} type="button"
                className={`org-option ${org.organizationId === active?.organizationId ? "org-option-active" : ""}`}
                onClick={() => action(() => apiRequest(`/api/organizations/${org.organizationId}/activate`, { method: "POST" }), "", true)}>
                <span className="org-option-main"><span className="org-monogram" aria-hidden="true">{org.name.slice(0, 1).toUpperCase()}</span><span><strong>{org.name}</strong><small>{org.role} access</small></span></span>
                {org.organizationId === active?.organizationId ? <span className="tag">Active</span> : <span className="muted" aria-hidden="true">↗</span>}
              </button>)}
            </div>
            {canCreateOrganization && <><div className="section-divider" />
            <form onSubmit={createOrganization} className="inline-form">
              <input aria-label="New organization name" className="input" placeholder="New organization name" minLength={2} maxLength={120} required value={organizationName} onChange={e => setOrganizationName(e.target.value)} />
              <button className="btn btn-primary">Create organization</button>
            </form>
            </>}
          </section>

          {surface === "Studio" && canManage && active && <section className="panel-card" id="members">
            <div className="panel-header"><div><span className="panel-kicker">{active.name}</span><h2>People & roles</h2><p>Manage who belongs to {active.name} and the roles that route approvals.</p></div></div>
            <div className="inline-form"><a className="btn btn-primary" href="/people">Manage people</a><a className="btn" href="/roles">Manage roles</a></div>
          </section>}

          {surface === "Portal" && <section className="panel-card">
            <div className="panel-header"><div><span className="panel-kicker">Coming next</span><h2>Requests and tasks</h2><p>Forms, approvals, and status tracking will appear here as those modules are built.</p></div></div>
            <div className="empty-state">Your account and organization are ready. There are no request features in this phase yet.</div>
          </section>}
        </div>

        <div className="workspace-side">
          <section className="panel-card" id="profile">
            <div className="panel-header"><div><span className="panel-kicker">Your account</span><h2>Profile</h2></div></div>
            <div className="account-summary"><span className="avatar" aria-hidden="true">{user.displayName.slice(0, 1).toUpperCase()}</span><span><strong>{user.displayName}</strong><small>{user.email}</small></span></div>
            <form className="stack-form" onSubmit={event => { event.preventDefault(); action(() => apiRequest("/api/auth/me", { method: "PUT", body: JSON.stringify({ displayName }) }), "Profile updated."); }}>
              <label className="field">Display name<input value={displayName} required maxLength={120} onChange={e => setDisplayName(e.target.value)} /></label>
              <button className="btn">Save profile</button>
            </form>
            <div className="section-divider" />
            <button className="btn" onClick={() => action(() => apiRequest("/api/auth/logout", { method: "POST" }), "", true)}>Sign out</button>
          </section>

          <section className="panel-card" id="security">
            <div className="panel-header"><div><span className="panel-kicker">Protection</span><h2>Security</h2><p>Manage your authenticator and active sessions.</p></div><span className={`tag ${user.twoFactorEnabled ? "" : "tag-warning"}`}>{user.twoFactorEnabled ? "MFA on" : "MFA off"}</span></div>
            {!user.twoFactorEnabled && <>
              <p className="helper">Add an authenticator for stronger sign-in protection. Owners and Admins need it for Studio.</p>
              <button className="btn btn-primary" disabled={mfaSetupBusy} style={{ marginTop: 12 }} onClick={async () => {
                setMfaSetupBusy(true); setMessage("");
                try { const setup = await apiRequest<{ sharedKey: string; authenticatorUri: string }>("/api/auth/mfa/setup"); setMfaKey(setup.sharedKey); setMfaUri(setup.authenticatorUri); setMfaCode(""); }
                catch (error) { setMessage(error instanceof Error ? error.message : "Setup failed."); }
                finally { setMfaSetupBusy(false); }
              }}>{mfaSetupBusy ? "Preparing setup…" : "Set up authenticator"}</button>
              {mfaKey && <div className="stack-form" style={{ marginTop: 18 }}>
                <p className="helper">In Microsoft Authenticator, tap +, choose Other account, and scan this QR code. Google Authenticator and other time-based authenticator apps can also scan it.</p>
                <div className="mfa-qr"><QRCodeSVG value={mfaUri} size={220} level="M" marginSize={4} title="Scan to add your FlowOps authenticator" /></div>
                <details className="helper"><summary>Can’t scan? Enter a setup key</summary>
                  <p>Account name: FlowOps ({user.email}). Choose a time-based account and enter this key:</p>
                  <code className="mfa-secret">{mfaKey.toUpperCase()}</code>
                </details>
                <p className="helper">Enter the current six-digit code from the FlowOps account in your app, then enable MFA.</p>
                <label className="field">Verification code<input autoComplete="one-time-code" inputMode="numeric" placeholder="6-digit code" value={mfaCode} onChange={e => setMfaCode(e.target.value)} /></label>
                <button className="btn btn-primary" onClick={async () => {
                  try { const response = await apiRequest<{ recoveryCodes: string[] }>("/api/auth/mfa/enable", { method: "POST", body: JSON.stringify({ code: mfaCode }) }); setRecoveryCodes(response.recoveryCodes); await refresh(); setMfaKey(""); setMfaUri(""); setMfaCode(""); }
                  catch (error) { setMessage(error instanceof Error ? error.message : "Invalid code."); }
                }}>Verify and enable</button>
              </div>}
            </>}
            {user.twoFactorEnabled && <div className="stack-form">
              <label className="field">Authenticator code<input autoComplete="one-time-code" inputMode="numeric" placeholder="6-digit code" value={mfaCode} onChange={e => setMfaCode(e.target.value)} /></label>
              <button className="btn" onClick={async () => {
                try { const response = await apiRequest<{ recoveryCodes: string[] }>("/api/auth/mfa/recovery-codes", { method: "POST", body: JSON.stringify({ code: mfaCode }) }); setRecoveryCodes(response.recoveryCodes); setMfaCode(""); }
                catch (error) { setMessage(error instanceof Error ? error.message : "Unable to regenerate codes."); }
              }}>Regenerate recovery codes</button>
              <details className="helper"><summary>Disable authenticator</summary>
                <div className="stack-form" style={{ marginTop: 12 }}><label className="field">Current password<PasswordInput autoComplete="current-password" value={disablePassword} onChange={e => setDisablePassword(e.target.value)} /></label>
                  <button className="btn btn-danger" onClick={() => action(() => apiRequest("/api/auth/mfa/disable", { method: "POST", body: JSON.stringify({ password: disablePassword, code: mfaCode }) }), "", true)}>Disable MFA and sign out</button></div>
              </details>
            </div>}
            {recoveryCodes.length > 0 && <div className="notice"><strong>Save these recovery codes now. They are shown once.</strong><pre className="recovery-list">{recoveryCodes.join("\n")}</pre></div>}
            <div className="section-divider" />
            <form className="stack-form" onSubmit={event => { event.preventDefault(); action(() => apiRequest("/api/auth/password/change", { method: "POST", body: JSON.stringify({ currentPassword, newPassword }) }), "", true); }}>
              <label className="field">Current password<PasswordInput autoComplete="current-password" required value={currentPassword} onChange={e => setCurrentPassword(e.target.value)} /></label>
              <label className="field">New password<PasswordInput autoComplete="new-password" required minLength={12} value={newPassword} onChange={e => setNewPassword(e.target.value)} /></label>
              <button className="btn">Change password</button>
            </form>
            <button className="text-button text-button-danger" style={{ marginTop: 20 }} onClick={() => action(() => apiRequest("/api/auth/sessions/revoke-all", { method: "POST" }), "", true)}>Sign out of all sessions</button>
          </section>
        </div>
      </div>
    </div>
  );
}
