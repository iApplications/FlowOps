import type { ReactNode } from "react";
import type { CurrentUser } from "@flowops/types";

export function BrandMark({ label = "FlowOps" }: { label?: string }) {
  return <span className="brand"><span className="brand-symbol" aria-hidden="true"><i /><i /><i /></span>{label}</span>;
}

export function AuthFrame({ surface, title, intro, children }: {
  surface: "Studio" | "Portal";
  title: string;
  intro: string;
  children: ReactNode;
}) {
  const studio = surface === "Studio";
  return <main className="auth-screen">
    <aside className="auth-story">
      <BrandMark />
      <div className="auth-story-inner auth-story-main">
        <p className="auth-story-label">{studio ? "Build with clarity" : "Your work, in one place"}</p>
        <h2>{studio ? "Make every process flow better." : "A calmer way to get work done."}</h2>
        <p>{studio ? "Bring people, approvals, and workflows together. Start with a secure workspace for your team." : "Submit requests, follow progress, and act on what matters, all from your workspace."}</p>
        <div className="auth-art" aria-hidden="true"><span className="auth-art-node"><span /></span><span className="auth-art-line" /><span className="auth-art-node"><span /></span><span className="auth-art-line" /><span className="auth-art-node"><span /></span></div>
      </div>
      <p className="auth-story-foot">FlowOps {surface} · Your foundation for better operations</p>
    </aside>
    <div className="auth-main">
      <div className="auth-card">
        <p className="eyebrow">{surface} / Secure access</p>
        <h1>{title}</h1>
        <p className="auth-card-intro">{intro}</p>
        {children}
      </div>
    </div>
  </main>;
}

export function DashboardShell({ surface, user, children, section = "overview" }: {
  surface: "Studio" | "Portal";
  user: CurrentUser;
  children: ReactNode;
  section?: "overview" | "forms";
}) {
  const active = user.activeOrganization;
  const name = user.displayName.trim().split(/\s+/)[0] || "there";
  if (surface === "Studio") {
    return <div className="studio-layout">
      <aside className="studio-sidebar">
        <BrandMark />
        <div>
          <p className="studio-sidebar-label">Workspace</p>
          <nav className="studio-nav" aria-label="Studio sections">
            <a href="/" aria-current={section === "overview" ? "page" : undefined}><span className="studio-nav-dot" />Overview</a>
            <a href="/forms" aria-current={section === "forms" ? "page" : undefined}><span className="studio-nav-dot" />Forms</a>
            <a href="/#organizations"><span className="studio-nav-dot" />Organizations</a>
            <a href="/#members"><span className="studio-nav-dot" />People</a>
            <a href="/#security"><span className="studio-nav-dot" />Security</a>
          </nav>
        </div>
        <div className="studio-sidebar-foot">FlowOps Studio<br />Draft forms are available. Field design comes next.</div>
      </aside>
      <div className="studio-main" id="overview">
        <header className="studio-topbar">
          <small>Studio / {active?.name ?? "Getting started"}</small>
          <span className="topbar-user"><span className="avatar" aria-hidden="true">{name.slice(0, 1).toUpperCase()}</span>{user.displayName}</span>
        </header>
        <main className="studio-content">
          <div className="dashboard-heading">
            <div><p className="eyebrow">{section === "forms" ? "Form builder" : "Organization foundation"}</p><h1>{section === "forms" ? "Forms" : "Workspace overview"}</h1><p>{section === "forms" ? "Manage draft forms for your active organization." : "Manage access, people, and account security from one place."}</p></div>
            <span className="status-chip">Phase 2 · Form foundation</span>
          </div>
          {section === "overview" && <section className="studio-hero">
            <div><p className="eyebrow">{active ? "Current workspace" : "Get started"}</p>
              <h2>{active ? active.name : "Create your first organization"}</h2>
              <p>{active ? `Your role is ${active.role}. Set up the people and security for this workspace before building workflows.` : "An organization keeps your people, settings, and future workflows together."}</p>
            </div>
            <div className="studio-hero-icon" aria-hidden="true">◎</div>
          </section>}
          {children}
        </main>
      </div>
    </div>;
  }

  return <div className="portal-layout">
    <header className="portal-header">
      <BrandMark label="FlowOps Portal" />
      <span className="portal-header-note">Your space for requests and approvals</span>
    </header>
    <main className="portal-content">
      <div className="portal-heading"><div><p className="eyebrow">Your workspace</p><h1>Good to see you, {name}.</h1><p>Keep your account and organization ready for what comes next.</p></div></div>
      <section className="portal-hero">
        <div><p className="eyebrow">{active ? "You are in" : "Your next step"}</p>
          <h2>{active ? active.name : "Join your organization"}</h2>
          <p>{active ? `You have ${active.role.toLowerCase()} access here. Forms, requests, and tasks will appear as those modules are added.` : "Ask your organization administrator to add your confirmed email address through Studio."}</p>
        </div>
        <div className="portal-hero-art" aria-hidden="true"><span /></div>
      </section>
      {children}
    </main>
  </div>;
}
