import type { ReactNode } from "react";
import type { CurrentUser } from "@flowops/types";
import { AccountMenu } from "./account-menu";

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

type StudioSection = "overview" | "forms" | "workflows" | "tasks" | "requests" | "people" | "roles";
const studioHeadings: Record<StudioSection, { eyebrow: string; title: string; description: string }> = {
  overview: { eyebrow: "Organization foundation", title: "Workspace overview", description: "Manage access, people, and account security from one place." },
  forms: { eyebrow: "Form builder", title: "Forms", description: "Manage draft forms for your active organization." },
  workflows: { eyebrow: "Workflow designer", title: "Workflows", description: "Design draft workflows for your active organization." },
  tasks: { eyebrow: "Approvals", title: "Tasks", description: "Decide approvals assigned to your role in the active organization." },
  requests: { eyebrow: "Requests", title: "My requests", description: "Requests you started in the active organization." },
  people: { eyebrow: "Access", title: "People", description: "Who belongs to the active organization and which role each person holds." },
  roles: { eyebrow: "Access", title: "Roles", description: "Business roles that route approval steps in the active organization." },
};

export function DashboardShell({ surface, user, children, section = "overview" }: {
  surface: "Studio" | "Portal";
  user: CurrentUser;
  children: ReactNode;
  section?: StudioSection;
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
            <a href="/workflows" aria-current={section === "workflows" ? "page" : undefined}><span className="studio-nav-dot" />Workflows</a>
            <a href="/tasks" aria-current={section === "tasks" ? "page" : undefined}><span className="studio-nav-dot" />Tasks</a>
            <a href="/#organizations"><span className="studio-nav-dot" />Organizations</a>
            <a href="/people" aria-current={section === "people" ? "page" : undefined}><span className="studio-nav-dot" />People</a>
            <a href="/roles" aria-current={section === "roles" ? "page" : undefined}><span className="studio-nav-dot" />Roles</a>
            <a href="/#security"><span className="studio-nav-dot" />Security</a>
          </nav>
        </div>
        <div className="studio-sidebar-foot">FlowOps Studio<br />Design and publish forms for your organization.</div>
      </aside>
      <div className="studio-main" id="overview">
        <header className="studio-topbar">
          <small>Studio / {active?.name ?? "Getting started"}</small>
          <AccountMenu user={user} />
        </header>
        <main className="studio-content">
          <div className="dashboard-heading">
            <div><p className="eyebrow">{studioHeadings[section].eyebrow}</p><h1>{studioHeadings[section].title}</h1><p>{studioHeadings[section].description}</p></div>
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
      <nav className="designer-actions" aria-label="Portal sections"><a className="btn" href="/" aria-current={section === "overview" ? "page" : undefined}>Overview</a><a className="btn" href="/forms" aria-current={section === "forms" ? "page" : undefined}>Forms</a><a className="btn" href="/tasks" aria-current={section === "tasks" ? "page" : undefined}>Tasks</a><a className="btn" href="/requests" aria-current={section === "requests" ? "page" : undefined}>My requests</a></nav>
      <AccountMenu user={user} />
    </header>
    <main className="portal-content">
      <div className={section === "overview" ? "portal-heading" : "portal-heading portal-heading-compact"}><div><p className="eyebrow">Your workspace</p><h1>{section === "forms" ? "Forms" : section === "tasks" ? "Tasks" : section === "requests" ? "My requests" : `Good to see you, ${name}.`}</h1><p>{section === "forms" ? "View published forms from your active organization." : section === "tasks" ? "Approvals waiting for you and your decision history." : section === "requests" ? "Where the requests you submitted stand, and what approvers said." : "Manage your account, security and assigned organizations."}</p></div></div>
      {section === "overview" && <section className="portal-hero">
        <div><p className="eyebrow">{active ? "You are in" : "Your next step"}</p>
          <h2>{active ? active.name : "Join your organization"}</h2>
          <p>{active ? `You have ${active.role.toLowerCase()} access here. Open Forms to view what your organization has published.` : "Ask your organization administrator to add your confirmed email address through Studio."}</p>
        </div>
        <div className="portal-hero-art" aria-hidden="true"><span /></div>
      </section>}
      {children}
    </main>
  </div>;
}
