"use client";

import { useEffect, useMemo, useState, type FormEvent } from "react";
import { apiRequest } from "@flowops/api-client";
import type { OrganizationRoleItem } from "@flowops/types";

type Member = { userId: string; email: string; displayName: string; role: string; roleName: string; joinedAt: string };
type Scope = { organizationId: string; organizationName: string; currentRole: string };

const PAGE_SIZE = 10;
const surfaceOf = (baseRole: string) => baseRole === "Member" ? "Portal" : "Studio";

function Pager({ page, pages, total, noun, onPage }: { page: number; pages: number; total: number; noun: string; onPage: (page: number) => void }) {
  if (total === 0) return null;
  return <div className="dir-pager">
    <span>{total} {noun}{total === 1 ? "" : "s"}{pages > 1 && ` · Page ${page} of ${pages}`}</span>
    {pages > 1 && <span className="dir-pager-buttons">
      <button className="btn" disabled={page <= 1} onClick={() => onPage(page - 1)}>Previous</button>
      <button className="btn" disabled={page >= pages} onClick={() => onPage(page + 1)}>Next</button>
    </span>}
  </div>;
}

function Avatar({ name }: { name: string }) {
  return <span className="member-avatar" aria-hidden="true">{(name.trim().slice(0, 1) || "?").toUpperCase()}</span>;
}

// People in the active organization: searchable, filterable by role and paged, so large organizations stay usable.
export function PeopleDirectory({ organizationId, organizationName, currentRole }: Scope) {
  const [members, setMembers] = useState<Member[]>([]);
  const [roles, setRoles] = useState<OrganizationRoleItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [query, setQuery] = useState("");
  const [roleFilter, setRoleFilter] = useState("");
  const [page, setPage] = useState(1);
  const [adding, setAdding] = useState(false);
  const [email, setEmail] = useState("");
  const [newRole, setNewRole] = useState("Member");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [isError, setIsError] = useState(false);
  const isOwner = currentRole === "Owner";
  const base = `/api/organizations/${organizationId}`;

  async function load() {
    const [people, roleList] = await Promise.all([apiRequest<Member[]>(`${base}/members`), apiRequest<OrganizationRoleItem[]>(`${base}/roles`)]);
    setMembers(people);
    setRoles(roleList);
  }
  useEffect(() => {
    const requested = new URLSearchParams(window.location.search).get("role");
    if (requested) setRoleFilter(requested);
    load().catch(error => { setIsError(true); setMessage(error instanceof Error ? error.message : "Unable to load people."); }).finally(() => setLoading(false));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [organizationId]);

  const customRoles = roles.filter(role => !role.isBuiltIn);
  const filtered = useMemo(() => {
    const text = query.trim().toLowerCase();
    return members.filter(member => (!roleFilter || member.roleName === roleFilter)
      && (!text || member.displayName.toLowerCase().includes(text) || member.email.toLowerCase().includes(text)));
  }, [members, query, roleFilter]);
  const pages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
  const current = Math.min(page, pages);
  const visible = filtered.slice((current - 1) * PAGE_SIZE, current * PAGE_SIZE);
  const roleNames = ["Owner", "Admin", "Member", ...customRoles.map(role => role.name)];

  async function run(work: () => Promise<unknown>, success: string) {
    setMessage("");
    setIsError(false);
    try { await work(); await load(); setMessage(success); }
    catch (error) { setIsError(true); setMessage(error instanceof Error ? error.message : "Request failed."); }
  }

  async function addPerson(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    const form = event.currentTarget as HTMLFormElement;
    if (!form.checkValidity()) { setIsError(true); setMessage("Enter a valid email address."); return; }
    setBusy(true);
    await run(async () => {
      await apiRequest(`${base}/members`, { method: "POST", body: JSON.stringify({ email, role: newRole }) });
      setEmail("");
    }, "Person added.");
    setBusy(false);
  }

  const assignable = [{ name: "Member", level: "Member" }, ...(isOwner ? [{ name: "Admin", level: "Admin" }] : []),
    ...customRoles.filter(role => role.isActive && (isOwner || role.baseRole === "Member")).map(role => ({ name: role.name, level: role.baseRole }))];

  return <section className="panel-card dir-card">
    <div className="panel-header"><div><span className="panel-kicker">{organizationName}</span><h2>People</h2>
      <p>Add confirmed Portal accounts as Members or Studio accounts as Admins. Each person has one role. Choosing a role on the other site moves them to their account there (same email), which must already be registered and confirmed.</p></div>
      <button className="btn btn-primary" onClick={() => setAdding(value => !value)} aria-expanded={adding}>{adding ? "Close" : "Add person"}</button></div>

    {adding && <form noValidate onSubmit={addPerson} className="dir-add">
      <label className="field">Email<input className="input" type="email" required maxLength={256} placeholder="Confirmed user email" value={email} onChange={e => { setEmail(e.target.value); setMessage(""); }} /></label>
      <label className="field">Role<select className="select" value={newRole} onChange={e => setNewRole(e.target.value)}>
        {assignable.map(role => <option key={role.name} value={role.name}>{role.name} ({surfaceOf(role.level)})</option>)}</select></label>
      <button className="btn btn-primary" disabled={busy}>{busy ? "Adding…" : "Add person"}</button>
    </form>}

    {message && <p role={isError ? "alert" : "status"} className="notice" style={{ margin: "0 0 14px" }}>{message}</p>}

    <div className="dir-toolbar">
      <input className="input" type="search" aria-label="Search people" placeholder="Search by name or email" value={query} onChange={e => { setQuery(e.target.value); setPage(1); }} />
      <select className="select" aria-label="Filter by role" value={roleFilter} onChange={e => { setRoleFilter(e.target.value); setPage(1); }}>
        <option value="">All roles</option>{roleNames.map(name => <option key={name} value={name}>{name}</option>)}</select>
      {(query || roleFilter) && <button className="text-button" onClick={() => { setQuery(""); setRoleFilter(""); setPage(1); }}>Clear</button>}
    </div>

    {loading ? <p className="muted">Loading people…</p> : filtered.length === 0
      ? <div className="empty-state">{members.length === 0 ? "No people yet." : "No one matches this search."}</div>
      : <div className="dir-table" role="table" aria-label="People">
        <div className="dir-row dir-head" role="row"><span role="columnheader">Person</span><span role="columnheader">Role</span><span role="columnheader">Access</span><span role="columnheader">Joined</span><span role="columnheader"><span className="sr-only">Actions</span></span></div>
        {visible.map(member => {
          const canEdit = member.role !== "Owner" && (isOwner || member.role === "Member");
          const options = [{ name: member.roleName, level: member.role },
            ...assignable.filter(role => role.name !== member.roleName)];
          return <div key={member.userId} className="dir-row" role="row">
            <span className="member-main" role="cell"><Avatar name={member.displayName} /><span><strong>{member.displayName}</strong><small>{member.email}</small></span></span>
            <span role="cell">{isOwner && member.role !== "Owner"
              ? <select className="select dir-select" aria-label={`Role for ${member.displayName}`} value={member.roleName}
                  onChange={e => run(() => apiRequest(`${base}/members/${member.userId}`, { method: "PUT", body: JSON.stringify({ role: e.target.value }) }), "Role updated.")}>
                  {options.map(role => <option key={role.name} value={role.name}>{role.name} ({surfaceOf(role.level)})</option>)}</select>
              : <span className="tag">{member.roleName}</span>}</span>
            <span role="cell" className="muted">{surfaceOf(member.role)}</span>
            <span role="cell" className="muted">{new Date(member.joinedAt).toLocaleDateString()}</span>
            <span role="cell" className="dir-actions">{canEdit && <button className="text-button text-button-danger" onClick={() => {
              if (window.confirm(`Remove ${member.displayName} from ${organizationName}?`)) run(() => apiRequest(`${base}/members/${member.userId}`, { method: "DELETE" }), "Person removed.");
            }}>Remove</button>}</span>
          </div>;
        })}
      </div>}
    <Pager page={current} pages={pages} total={filtered.length} noun="person" onPage={setPage} />
  </section>;
}

// Custom roles: the Owner creates, retires and deletes them; Admins can read the list.
export function RoleManager({ organizationId, organizationName, currentRole }: Scope) {
  const [roles, setRoles] = useState<OrganizationRoleItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [page, setPage] = useState(1);
  const [creating, setCreating] = useState(false);
  const [name, setName] = useState("");
  const [baseRole, setBaseRole] = useState<"Member" | "Admin">("Member");
  const [message, setMessage] = useState("");
  const [isError, setIsError] = useState(false);
  const isOwner = currentRole === "Owner";
  const base = `/api/organizations/${organizationId}/roles`;

  async function load() { setRoles(await apiRequest<OrganizationRoleItem[]>(base)); }
  useEffect(() => {
    load().catch(error => { setIsError(true); setMessage(error instanceof Error ? error.message : "Unable to load roles."); }).finally(() => setLoading(false));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [organizationId]);

  async function run(work: () => Promise<unknown>, success: string) {
    setMessage("");
    setIsError(false);
    try { await work(); await load(); setMessage(success); }
    catch (error) { setIsError(true); setMessage(error instanceof Error ? error.message : "Request failed."); }
  }

  const builtIn = roles.filter(role => role.isBuiltIn);
  const custom = roles.filter(role => !role.isBuiltIn);
  const pages = Math.max(1, Math.ceil(custom.length / PAGE_SIZE));
  const current = Math.min(page, pages);
  const visible = custom.slice((current - 1) * PAGE_SIZE, current * PAGE_SIZE);
  const descriptions: Record<string, string> = {
    Owner: "Full control of the organization and its roles.",
    Admin: "Studio access to build forms and workflows.",
    Member: "Portal access to submit requests and decide assigned tasks.",
  };

  return <div className="role-page">
    <section className="panel-card dir-card">
      <div className="panel-header"><div><span className="panel-kicker">{organizationName}</span><h2>Built-in roles</h2><p>These always exist and cannot be changed.</p></div></div>
      <div className="dir-table" role="table" aria-label="Built-in roles">
        {builtIn.map(role => <div key={role.name} className="dir-row dir-row-roles" role="row">
          <span role="cell"><strong>{role.name}</strong><small className="muted" style={{ display: "block" }}>{descriptions[role.name]}</small></span>
          <span role="cell" className="muted">{surfaceOf(role.baseRole)} access</span><span role="cell"><span className="tag">Built-in</span></span>
        </div>)}
      </div>
    </section>

    <section className="panel-card dir-card">
      <div className="panel-header"><div><span className="panel-kicker">{organizationName}</span><h2>Custom roles</h2>
        <p>Business roles such as Manager or Finance for approval steps. Names cannot change; deactivate a role to retire it.</p></div>
        {isOwner && <button className="btn btn-primary" onClick={() => setCreating(value => !value)} aria-expanded={creating}>{creating ? "Close" : "New role"}</button>}</div>

      {creating && <form noValidate className="dir-add" onSubmit={event => {
        event.preventDefault();
        run(async () => {
          await apiRequest(base, { method: "POST", body: JSON.stringify({ name, baseRole }) });
          setName("");
          setCreating(false);
        }, "Role created.");
      }}>
        <label className="field">Role name<input className="input" minLength={2} maxLength={32} required placeholder="e.g. Finance" value={name} onChange={e => setName(e.target.value)} /></label>
        <label className="field">Base access<select className="select" value={baseRole} onChange={e => setBaseRole(e.target.value as "Member" | "Admin")}>
          <option value="Member">Member (Portal)</option><option value="Admin">Admin (Studio)</option></select></label>
        <button className="btn btn-primary">Create role</button>
      </form>}

      {message && <p role={isError ? "alert" : "status"} className="notice" style={{ margin: "0 0 14px" }}>{message}</p>}

      {loading ? <p className="muted">Loading roles…</p> : custom.length === 0
        ? <div className="empty-state">No custom roles yet.{isOwner && " Create one to route approvals to a specific team, such as Finance."}</div>
        : <div className="dir-table" role="table" aria-label="Custom roles">
          <div className="dir-row dir-row-roles dir-head" role="row"><span role="columnheader">Role</span><span role="columnheader">Access</span><span role="columnheader">People</span><span role="columnheader">Status</span><span role="columnheader"><span className="sr-only">Actions</span></span></div>
          {visible.map(role => <div key={role.id} className="dir-row dir-row-roles" role="row">
            <span role="cell"><strong>{role.name}</strong></span>
            <span role="cell" className="muted">{surfaceOf(role.baseRole)}</span>
            <span role="cell"><a className="text-button" href={`/people?role=${encodeURIComponent(role.name)}`}>{role.memberCount ?? 0} {role.memberCount === 1 ? "person" : "people"}</a></span>
            <span role="cell"><span className={role.isActive ? "tag" : "tag tag-warning"}>{role.isActive ? "Active" : "Inactive"}</span></span>
            <span role="cell" className="dir-actions">{isOwner && <>
              <button className="text-button" onClick={() => run(() => apiRequest(`${base}/${role.id}`, { method: "PUT", body: JSON.stringify({ isActive: !role.isActive }) }), role.isActive ? "Role deactivated." : "Role activated.")}>{role.isActive ? "Deactivate" : "Activate"}</button>
              <button className="text-button text-button-danger" onClick={() => {
                if (window.confirm(`Delete the ${role.name} role? This cannot be undone.`)) run(() => apiRequest(`${base}/${role.id}`, { method: "DELETE" }), "Role deleted.");
              }}>Delete</button></>}</span>
          </div>)}
        </div>}
      <Pager page={current} pages={pages} total={custom.length} noun="role" onPage={setPage} />
    </section>
  </div>;
}
