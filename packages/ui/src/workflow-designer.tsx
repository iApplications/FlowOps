"use client";

import "@xyflow/react/dist/style.css";
import { useEffect, useRef, useState, type DragEvent } from "react";
import {
  ReactFlow, ReactFlowProvider, Background, Controls, MiniMap, Handle, Position, useReactFlow,
  applyNodeChanges, applyEdgeChanges, type Node, type Edge, type NodeProps, type NodeChange, type EdgeChange, type Connection,
} from "@xyflow/react";
import type { WorkflowDetail, WorkflowGraph, WorkflowIssue, WorkflowNodeConfig, WorkflowNodeType } from "@flowops/types";
import { apiRequest } from "@flowops/api-client";

// summary and issueCount are display-only values derived at render time; they are never saved.
type NodeData = { label: string; nodeType: WorkflowNodeType; config: WorkflowNodeConfig; summary?: string; issueCount?: number };
type FlowNode = Node<NodeData>;
type FormOption = { id: string; name: string; publicationCount: number };

const nodeTypeOrder: WorkflowNodeType[] = ["Start", "Condition", "Approval", "End"];
// Output handles per node type; must match WorkflowGraphValidation.OutputHandles on the server.
const outputs: Record<WorkflowNodeType, string[]> = { Start: ["next"], Condition: ["true", "false"], Approval: ["approved", "rejected"], End: [] };
const descriptions: Record<WorkflowNodeType, string> = {
  Start: "How the workflow begins", Condition: "Branch on true / false", Approval: "Ask a role to approve or reject", End: "Finish with an outcome",
};
const defaults: Record<WorkflowNodeType, { label: string; config: WorkflowNodeConfig }> = {
  Start: { label: "Start", config: { trigger: "Manual" } },
  Condition: { label: "Condition", config: { expression: "" } },
  Approval: { label: "Approval", config: { assigneeRole: "Admin", instructions: "" } },
  End: { label: "End", config: { outcome: "Completed" } },
};
const roleLabels = { Owner: "Owner", Admin: "Admin", Member: "Member" } as const;
const dragType = "application/flowops-workflow-node";
const newId = (prefix: string) => `${prefix}-${crypto.randomUUID().slice(0, 8)}`;

function summary(data: NodeData, forms: FormOption[]) {
  const config = data.config;
  switch (data.nodeType) {
    case "Start": return config.trigger === "FormSubmitted" ? `When "${forms.find(form => form.id === config.formId)?.name ?? "a form"}" is submitted` : "Started manually";
    case "Condition": return config.expression?.trim() || "No expression yet";
    case "Approval": return `Approver: ${config.assigneeRole ? roleLabels[config.assigneeRole] : "not set"}`;
    default: return `Outcome: ${config.outcome?.trim() || "not set"}`;
  }
}

function WorkflowNodeCard({ data, selected }: NodeProps<FlowNode>) {
  const handles = outputs[data.nodeType];
  return <div className={`wf-node wf-node-${data.nodeType.toLowerCase()} ${selected ? "wf-node-selected" : ""} ${data.issueCount ? "wf-node-issue" : ""}`}>
    {data.nodeType !== "Start" && <Handle type="target" position={Position.Left} className="wf-handle" />}
    {!!data.issueCount && <span className="wf-node-issue-badge" title={`${data.issueCount} ${data.issueCount === 1 ? "issue" : "issues"}`}>{data.issueCount}</span>}
    <span className="wf-node-type">{data.nodeType}</span>
    <strong className="wf-node-label">{data.label}</strong>
    <small className="wf-node-summary">{data.summary}</small>
    {handles.map((handle, index) => <div key={handle} className="wf-output" style={{ top: `${((index + 1) / (handles.length + 1)) * 100}%` }}>
      <span>{handle}</span>
      <Handle type="source" id={handle} position={Position.Right} className="wf-handle" />
    </div>)}
  </div>;
}
const nodeTypes = { Start: WorkflowNodeCard, Condition: WorkflowNodeCard, Approval: WorkflowNodeCard, End: WorkflowNodeCard };

function toFlow(graph: WorkflowGraph): { nodes: FlowNode[]; edges: Edge[] } {
  return {
    nodes: graph.nodes.map(node => ({ id: node.id, type: node.type, position: node.position,
      data: { label: node.label, nodeType: node.type, config: { ...defaults[node.type].config, ...(node.config ?? {}) } } })),
    edges: graph.connections.map(connection => ({ id: connection.id, source: connection.source, sourceHandle: connection.sourceHandle,
      target: connection.target, label: connection.sourceHandle === "next" ? undefined : connection.sourceHandle })),
  };
}

// Only the selected type's settings are sent; the server rejects settings that belong to another type.
function cleanConfig(data: NodeData): WorkflowNodeConfig {
  const config = data.config;
  switch (data.nodeType) {
    case "Start": return config.trigger === "FormSubmitted" ? { trigger: "FormSubmitted", formId: config.formId ?? null } : { trigger: "Manual" };
    case "Condition": return { expression: config.expression ?? "" };
    case "Approval": return { assigneeRole: config.assigneeRole ?? null, instructions: config.instructions ?? "" };
    default: return { outcome: config.outcome ?? "" };
  }
}

export function WorkflowDesigner({ initialWorkflow }: { initialWorkflow: WorkflowDetail }) {
  return <ReactFlowProvider><Designer initialWorkflow={initialWorkflow} /></ReactFlowProvider>;
}

function Designer({ initialWorkflow }: { initialWorkflow: WorkflowDetail }) {
  const draft = initialWorkflow.draft!;
  const [initial] = useState(() => toFlow(draft.graph));
  const [nodes, setNodes] = useState<FlowNode[]>(initial.nodes);
  const [edges, setEdges] = useState<Edge[]>(initial.edges);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [revision, setRevision] = useState(draft.draftRevision);
  const [versionNumber, setVersionNumber] = useState(draft.versionNumber);
  const [latestPublished, setLatestPublished] = useState<number | null>(initialWorkflow.latestPublished?.versionNumber ?? null);
  const [unpublished, setUnpublished] = useState(draft.hasUnpublishedChanges ?? true);
  // Completeness issues are computed by the server for the last saved graph.
  const [issues, setIssues] = useState<WorkflowIssue[]>(draft.issues ?? []);
  const [forms, setForms] = useState<FormOption[]>([]);
  const [dirty, setDirty] = useState(false);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [failed, setFailed] = useState(false);
  const canvas = useRef<HTMLDivElement>(null);
  const { screenToFlowPosition, fitView } = useReactFlow();
  const selected = nodes.find(node => node.id === selectedId);
  const hasStart = nodes.some(node => node.data.nodeType === "Start");

  useEffect(() => {
    let cancelled = false;
    apiRequest<FormOption[]>("/api/forms").then(rows => { if (!cancelled) setForms(rows); }).catch(() => { /* The form picker stays empty; saving reports problems. */ });
    return () => { cancelled = true; };
  }, []);
  useEffect(() => {
    if (!dirty) return;
    const warn = (event: BeforeUnloadEvent) => { event.preventDefault(); };
    const navigate = (event: MouseEvent) => {
      const link = event.target instanceof Element ? event.target.closest("a[href]") : null;
      if (!link || event.defaultPrevented) return;
      if (!window.confirm("Leave this workflow and discard unsaved changes?")) { event.preventDefault(); event.stopPropagation(); }
      else window.removeEventListener("beforeunload", warn);
    };
    window.addEventListener("beforeunload", warn);
    document.addEventListener("click", navigate, true);
    return () => { window.removeEventListener("beforeunload", warn); document.removeEventListener("click", navigate, true); };
  }, [dirty]);

  function touch() { setDirty(true); setMessage(""); setFailed(false); }
  function onNodesChange(changes: NodeChange<FlowNode>[]) {
    setNodes(current => applyNodeChanges(changes, current));
    if (changes.some(change => change.type === "position" || change.type === "remove" || change.type === "add")) touch();
  }
  function onEdgesChange(changes: EdgeChange[]) {
    setEdges(current => applyEdgeChanges(changes, current));
    if (changes.some(change => change.type === "remove" || change.type === "add")) touch();
  }
  // Mirrors the server's structural rules so invalid links cannot be drawn.
  function isValidConnection(connection: Edge | Connection) {
    const source = nodes.find(node => node.id === connection.source);
    const target = nodes.find(node => node.id === connection.target);
    return !!source && !!target && source.id !== target.id && target.data.nodeType !== "Start"
      && outputs[source.data.nodeType].includes(connection.sourceHandle ?? "")
      && !edges.some(edge => edge.source === connection.source && edge.sourceHandle === connection.sourceHandle);
  }
  function onConnect(connection: Connection) {
    if (!isValidConnection(connection)) return;
    setEdges(current => [...current, { id: newId("c"), source: connection.source, sourceHandle: connection.sourceHandle,
      target: connection.target, label: connection.sourceHandle === "next" ? undefined : connection.sourceHandle ?? undefined }]);
    touch();
  }
  function add(type: WorkflowNodeType, position?: { x: number; y: number }) {
    if (type === "Start" && hasStart) return;
    const bounds = canvas.current?.getBoundingClientRect();
    const at = position ?? screenToFlowPosition({ x: (bounds?.left ?? 0) + (bounds?.width ?? 0) / 2, y: (bounds?.top ?? 0) + (bounds?.height ?? 0) / 2 });
    const node: FlowNode = { id: newId(type.toLowerCase()), type, position: at, selected: true,
      data: { label: defaults[type].label, nodeType: type, config: { ...defaults[type].config } } };
    setNodes(current => [...current.map(item => ({ ...item, selected: false })), node]);
    setSelectedId(node.id);
    touch();
  }
  function onDrop(event: DragEvent) {
    event.preventDefault();
    const type = event.dataTransfer.getData(dragType) as WorkflowNodeType;
    if (nodeTypeOrder.includes(type)) add(type, screenToFlowPosition({ x: event.clientX, y: event.clientY }));
  }
  function update(patch: Partial<NodeData> & { config?: WorkflowNodeConfig }) {
    setNodes(current => current.map(node => node.id === selectedId ? { ...node, data: { ...node.data, ...patch, config: { ...node.data.config, ...patch.config } } } : node));
    touch();
  }
  function removeSelected() {
    if (!selectedId) return;
    setNodes(current => current.filter(node => node.id !== selectedId));
    setEdges(current => current.filter(edge => edge.source !== selectedId && edge.target !== selectedId));
    setSelectedId(null);
    touch();
  }
  async function save() {
    if (busy) return;
    setBusy(true); setMessage(""); setFailed(false);
    const graph: WorkflowGraph = {
      schemaVersion: draft.graph.schemaVersion,
      nodes: nodes.map(node => ({ id: node.id, type: node.data.nodeType, label: node.data.label,
        position: { x: Math.round(node.position.x), y: Math.round(node.position.y) }, config: cleanConfig(node.data) })),
      connections: edges.map(edge => ({ id: edge.id, source: edge.source, sourceHandle: edge.sourceHandle ?? "", target: edge.target })),
    };
    try {
      const saved = await apiRequest<WorkflowDetail>(`/api/workflows/${initialWorkflow.id}/draft`, { method: "PUT", body: JSON.stringify({ graph, draftRevision: revision }) });
      setRevision(saved.draft!.draftRevision); setIssues(saved.draft!.issues ?? []); setUnpublished(saved.draft!.hasUnpublishedChanges); setDirty(false); setMessage("Draft saved.");
    } catch (error) { setFailed(true); setMessage(error instanceof Error ? error.message : "Unable to save the workflow."); }
    finally { setBusy(false); }
  }
  // Freezes the saved draft as a Published version; the server opens the next draft with the same graph.
  async function publish() {
    if (busy || !canPublish) return;
    if (!window.confirm(`Publish draft version ${versionNumber}? Published versions cannot be changed; further edits go into version ${versionNumber + 1}.`)) return;
    setBusy(true); setMessage(""); setFailed(false);
    try {
      const published = await apiRequest<WorkflowDetail>(`/api/workflows/${initialWorkflow.id}/publish`, { method: "POST", body: JSON.stringify({ draftRevision: revision }) });
      setLatestPublished(published.latestPublished?.versionNumber ?? null);
      setVersionNumber(published.draft!.versionNumber); setRevision(published.draft!.draftRevision);
      setIssues(published.draft!.issues ?? []); setUnpublished(published.draft!.hasUnpublishedChanges);
      setMessage(`Version ${published.latestPublished?.versionNumber} published. Further edits go into draft version ${published.draft!.versionNumber}.`);
    } catch (error) { setFailed(true); setMessage(error instanceof Error ? error.message : "Unable to publish the workflow."); }
    finally { setBusy(false); }
  }

  // Select and centre the node an issue refers to.
  function focusNode(nodeId: string) {
    if (!nodes.some(node => node.id === nodeId)) return;
    setNodes(current => current.map(node => ({ ...node, selected: node.id === nodeId })));
    setSelectedId(nodeId);
    void fitView({ nodes: [{ id: nodeId }], duration: 300, maxZoom: 1 });
  }

  const issuesByNode = issues.reduce<Record<string, number>>((counts, issue) => {
    if (issue.nodeId) counts[issue.nodeId] = (counts[issue.nodeId] ?? 0) + 1;
    return counts;
  }, {});
  const displayNodes = nodes.map(node => ({ ...node, data: { ...node.data, summary: summary(node.data, forms), issueCount: issuesByNode[node.id] ?? 0 } }));
  const config = selected?.data.config ?? {};
  const canPublish = !busy && !dirty && issues.length === 0 && unpublished;
  const publishHint = dirty ? "Save the draft before publishing." : issues.length ? "Resolve the issues before publishing."
    : !unpublished ? `No changes since version ${latestPublished}.` : `Publishes version ${versionNumber}.`;
  return <section className="panel-card">
    <div className="panel-header"><div><span className="panel-kicker">Draft version {versionNumber}{latestPublished ? ` · Published version ${latestPublished}` : " · Not published yet"}</span><h2>{initialWorkflow.name}</h2>
      <p>{dirty ? "Unsaved changes" : "All changes saved"}. Drag nodes from the palette, connect outputs to inputs, and select a node to configure it.</p></div>
      <div className="designer-actions">
        <button type="button" className="btn" disabled={busy || !dirty} onClick={save}>{busy ? "Working…" : "Save draft"}</button>
        <button type="button" className="btn btn-primary" disabled={!canPublish} title={publishHint} onClick={publish}>Publish version {versionNumber}</button>
      </div></div>
    {message && <p className="notice" role={failed ? "alert" : "status"}>{message}</p>}
    <div className={`wf-issues ${issues.length ? "" : "wf-issues-clear"}`} aria-live="polite">
      <div className="wf-issues-header">
        <strong>{issues.length ? `${issues.length} ${issues.length === 1 ? "issue" : "issues"} to resolve` : "No issues: this draft is complete."}</strong>
        <small className="helper">{dirty ? "Showing the last saved check. Save the draft to re-check." : issues.length ? "Drafts with issues can be saved, but publishing requires none." : publishHint}</small>
      </div>
      {issues.length > 0 && <ul>{issues.map((issue, index) => <li key={`${issue.code}-${issue.nodeId ?? "workflow"}-${index}`}>
        {issue.nodeId && nodes.some(node => node.id === issue.nodeId)
          ? <button type="button" className="wf-issue-link" onClick={() => focusNode(issue.nodeId!)}>{issue.message}</button>
          : <span>{issue.message}</span>}
      </li>)}</ul>}
    </div>
    <div className="wf-designer">
      <div className="designer-palette" aria-label="Add nodes"><h3>Add node</h3>
        {nodeTypeOrder.map(type => <button key={type} type="button" className="btn wf-palette-item" draggable={!(type === "Start" && hasStart)} disabled={busy || (type === "Start" && hasStart)}
          title={type === "Start" && hasStart ? "A workflow has one Start node" : `Drag onto the canvas or click to add ${type}`}
          onDragStart={event => { event.dataTransfer.setData(dragType, type); event.dataTransfer.effectAllowed = "move"; }} onClick={() => add(type)}>
          <strong>{type}</strong><small>{descriptions[type]}</small></button>)}
      </div>
      <div className="wf-canvas" ref={canvas} onDrop={onDrop} onDragOver={event => { event.preventDefault(); event.dataTransfer.dropEffect = "move"; }}>
        <ReactFlow nodes={displayNodes} edges={edges} nodeTypes={nodeTypes} onNodesChange={onNodesChange} onEdgesChange={onEdgesChange}
          onConnect={onConnect} isValidConnection={isValidConnection} onSelectionChange={({ nodes: picked }) => setSelectedId(picked[0]?.id ?? null)}
          deleteKeyCode={["Backspace", "Delete"]} nodesDraggable={!busy} fitView fitViewOptions={{ padding: 0.3, maxZoom: 1 }}>
          <Background gap={20} />
          <Controls showInteractive={false} />
          <MiniMap pannable zoomable />
        </ReactFlow>
      </div>
      <aside className="wf-settings" aria-label="Node settings">{selected ? <>
        <header className="wf-settings-header">
          <span className={`wf-type-badge wf-type-${selected.data.nodeType.toLowerCase()}`}>{selected.data.nodeType}</span>
          <h3>{selected.data.label.trim() || "Untitled node"}</h3>
          <p className="helper">{descriptions[selected.data.nodeType]}</p>
        </header>
        <div className="wf-settings-body">
        <label className="field">Label<input maxLength={80} value={selected.data.label} disabled={busy} onChange={event => update({ label: event.target.value })} /></label>
        {selected.data.nodeType === "Start" && <>
          <label className="field">Trigger<select value={config.trigger ?? "Manual"} disabled={busy} onChange={event => update({ config: event.target.value === "FormSubmitted" ? { trigger: "FormSubmitted" } : { trigger: "Manual", formId: null } })}>
            <option value="Manual">Started manually</option><option value="FormSubmitted">When a form is submitted</option></select></label>
          {config.trigger === "FormSubmitted" && <label className="field">Form<select value={config.formId ?? ""} disabled={busy} onChange={event => update({ config: { formId: event.target.value || null } })}>
            <option value="">Choose a form…</option>
            {forms.map(form => <option key={form.id} value={form.id}>{form.name}{form.publicationCount ? "" : " (not published yet)"}</option>)}</select></label>}
        </>}
        {selected.data.nodeType === "Condition" && <label className="field">Expression<textarea maxLength={500} value={config.expression ?? ""} disabled={busy} placeholder="request.amount >= 500" onChange={event => update({ config: { expression: event.target.value } })} />
          <small className="helper">Compare workflow values with ==, !=, &lt;, &lt;=, &gt;, &gt;= and combine with &amp;&amp;, ||, !. No code is run.</small></label>}
        {selected.data.nodeType === "Approval" && <>
          <label className="field">Approver role<select value={config.assigneeRole ?? ""} disabled={busy} onChange={event => update({ config: { assigneeRole: (event.target.value || null) as WorkflowNodeConfig["assigneeRole"] } })}>
            {Object.keys(roleLabels).map(role => <option key={role} value={role}>{role}</option>)}</select></label>
          <label className="field">Instructions<textarea maxLength={1000} value={config.instructions ?? ""} disabled={busy} placeholder="What should the approver check?" onChange={event => update({ config: { instructions: event.target.value } })} /></label>
        </>}
        {selected.data.nodeType === "End" && <label className="field">Outcome<input maxLength={80} value={config.outcome ?? ""} disabled={busy} placeholder="Approved" onChange={event => update({ config: { outcome: event.target.value } })} /></label>}
        </div>
        <footer className="wf-settings-footer"><button type="button" className="btn btn-danger" disabled={busy} onClick={removeSelected}>Remove node</button></footer>
      </> : <div className="wf-settings-empty">
        <h3>Node settings</h3>
        <p>Select a node on the canvas to edit its label and settings.</p>
        <p className="helper">Tip: press Delete to remove the selected node or connection.</p>
      </div>}</aside>
    </div>
  </section>;
}
