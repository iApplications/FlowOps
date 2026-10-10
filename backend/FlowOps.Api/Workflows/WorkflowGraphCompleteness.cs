namespace FlowOps.Api.Workflows;

public sealed record WorkflowIssue(string Code, string Message, string? NodeId = null);

// Completeness rules for a structurally valid draft. Drafts with issues can still be saved; Phase 4
// publishing will require zero issues. Messages name nodes by label so they read well in the designer.
public static class WorkflowGraphCompleteness
{
    // forms maps the organization's referenced form IDs to their name and whether they have a published version.
    public static List<WorkflowIssue> Issues(WorkflowGraph graph, IReadOnlyDictionary<Guid, (string Name, bool Published)> forms)
    {
        var issues = new List<WorkflowIssue>();
        var nodes = graph.Nodes.ToDictionary(x => x.Id);
        var outgoing = graph.Connections.GroupBy(x => x.Source).ToDictionary(x => x.Key, x => x.ToList());
        var incoming = graph.Connections.GroupBy(x => x.Target).ToDictionary(x => x.Key, x => x.Count());
        var starts = graph.Nodes.Where(x => x.Type == WorkflowNodeTypes.Start).ToList();
        var ends = graph.Nodes.Where(x => x.Type == WorkflowNodeTypes.End).ToList();
        var declared = (graph.Variables ?? []).ToDictionary(x => x.Name, x => x.Type);

        if (starts.Count == 0) issues.Add(new("start_missing", "Add a Start node."));
        foreach (var extra in starts.Skip(1)) issues.Add(new("start_multiple", $"{extra.Label}: a workflow has exactly one Start node.", extra.Id));
        if (ends.Count == 0) issues.Add(new("end_missing", "Add at least one End node."));

        foreach (var node in graph.Nodes)
        {
            if (node.Type == WorkflowNodeTypes.Start && incoming.ContainsKey(node.Id))
                issues.Add(new("start_incoming", $"{node.Label}: Start cannot have incoming connections.", node.Id));
            var used = outgoing.TryGetValue(node.Id, out var links) ? links.Select(x => x.SourceHandle).ToHashSet() : [];
            foreach (var handle in WorkflowGraphValidation.OutputHandles(node.Type).Where(x => !used.Contains(x)))
                issues.Add(new("output_unconnected", $"{node.Label}: connect the '{handle}' output.", node.Id));
            if (node.Type == WorkflowNodeTypes.Condition && WorkflowExpression.Validate(node.Config?.Expression) is string expressionError)
                issues.Add(new("expression_invalid", $"{node.Label}: {expressionError}", node.Id));
            if (node.Type == WorkflowNodeTypes.Condition) CheckVariables(issues, node, [node.Config?.Expression], declared);
            if (node.Type == WorkflowNodeTypes.Calculation) CheckCalculation(issues, node, declared);
            if (node.Type == WorkflowNodeTypes.Start && node.Config?.Trigger == WorkflowTriggers.FormSubmitted && node.Config.FormId is Guid formId
                && forms.TryGetValue(formId, out var form) && !form.Published)
                issues.Add(new("form_unpublished", $"{node.Label}: publish the form \"{form.Name}\" before this workflow can start from it.", node.Id));
        }

        // Reachability is checked from the single Start only; with zero or several Starts the issues above apply first.
        if (starts.Count == 1)
        {
            var reachable = Walk(starts[0].Id, id => outgoing.TryGetValue(id, out var links) ? links.Select(x => x.Target) : []);
            foreach (var node in graph.Nodes.Where(x => !reachable.Contains(x.Id)))
                issues.Add(new("unreachable", $"{node.Label} cannot be reached from Start.", node.Id));
        }
        // Nodes that cannot reach any End, reported only when their own outputs are connected (otherwise
        // the unconnected-output issue already explains the problem).
        var reverse = graph.Connections.GroupBy(x => x.Target).ToDictionary(x => x.Key, x => x.Select(c => c.Source).ToList());
        var canFinish = new HashSet<string>();
        foreach (var end in ends) canFinish.UnionWith(Walk(end.Id, id => reverse.TryGetValue(id, out var sources) ? sources : []));
        foreach (var node in graph.Nodes.Where(x => x.Type != WorkflowNodeTypes.End && !canFinish.Contains(x.Id)))
        {
            var connected = outgoing.TryGetValue(node.Id, out var links) ? links.Count : 0;
            if (connected == WorkflowGraphValidation.OutputHandles(node.Type).Count && connected > 0)
                issues.Add(new("no_path_to_end", $"{node.Label} has no path to an End node.", node.Id));
        }

        foreach (var loop in Cycles(graph, outgoing))
            issues.Add(new("cycle", $"Remove the loop through {string.Join(" → ", loop.Select(id => nodes[id].Label))}. Loops are not supported yet.", loop[0]));
        return issues;
    }

    // vars.<name> must be declared. Other roots (request, submission, trigger) are checked at run time.
    private static void CheckVariables(List<WorkflowIssue> issues, WorkflowNode node, IEnumerable<string?> expressions, Dictionary<string, string> declared)
    {
        foreach (var path in expressions.SelectMany(WorkflowExpression.Paths).Distinct())
        {
            var parts = path.Split('.');
            if (parts[0] != "vars") continue;
            if (parts.Length != 2 || !declared.ContainsKey(parts[1]))
                issues.Add(new("variable_undeclared", $"{node.Label}: the variable '{path}' is not declared. Declare it in Variables first.", node.Id));
        }
    }

    private static void CheckCalculation(List<WorkflowIssue> issues, WorkflowNode node, Dictionary<string, string> declared)
    {
        var assignments = node.Config?.Assignments ?? [];
        if (assignments.Count == 0)
        {
            issues.Add(new("calculation_empty", $"{node.Label}: add at least one assignment.", node.Id));
            return;
        }
        CheckVariables(issues, node, assignments.Select(x => x.Expression), declared);
        // Types are checked statically by evaluating against defaults of the declared variables. Values that only exist at
        // run time (request.*) make the check inconclusive, which is accepted.
        var sample = new System.Text.Json.Nodes.JsonObject { ["vars"] = new System.Text.Json.Nodes.JsonObject() };
        foreach (var (name, type) in declared)
            sample["vars"]![name] = type switch { WorkflowVariable.Number => 1m, WorkflowVariable.Boolean => false, _ => "" };
        foreach (var assignment in assignments)
        {
            if (string.IsNullOrWhiteSpace(assignment.Variable) || !declared.TryGetValue(assignment.Variable, out var type))
            {
                issues.Add(new("variable_undeclared", $"{node.Label}: choose a declared variable to assign to.", node.Id));
                continue;
            }
            if (WorkflowExpression.ValidateCalculation(assignment.Expression) is string syntax)
            {
                issues.Add(new("expression_invalid", $"{node.Label}: {assignment.Variable}: {syntax}", node.Id));
                continue;
            }
            var result = WorkflowExpression.Calculate(assignment.Expression, sample);
            if (result.ErrorCode == "type_mismatch")
                issues.Add(new("type_mismatch", $"{node.Label}: {assignment.Variable}: {result.ErrorMessage}", node.Id));
            else if (result.ErrorCode is null && result.Type != type)
                issues.Add(new("type_mismatch", $"{node.Label}: {assignment.Variable} is {type}, but the expression gives {result.Type}.", node.Id));
        }
    }

    private static HashSet<string> Walk(string start, Func<string, IEnumerable<string>> next)
    {
        var seen = new HashSet<string> { start };
        var stack = new Stack<string>([start]);
        while (stack.TryPop(out var id))
            foreach (var target in next(id))
                if (seen.Add(target)) stack.Push(target);
        return seen;
    }

    // Iterative depth-first search; reports each loop once by the path from its first repeated node.
    private static List<List<string>> Cycles(WorkflowGraph graph, Dictionary<string, List<WorkflowConnection>> outgoing)
    {
        var loops = new List<List<string>>();
        var state = new Dictionary<string, int>(); // 1 = on the current path, 2 = finished
        foreach (var root in graph.Nodes.Select(x => x.Id))
        {
            if (state.ContainsKey(root)) continue;
            var path = new List<string>();
            var stack = new Stack<(string Id, IEnumerator<string> Next)>();
            void Enter(string id)
            {
                state[id] = 1;
                path.Add(id);
                stack.Push((id, (outgoing.TryGetValue(id, out var links) ? links.Select(x => x.Target) : []).GetEnumerator()));
            }
            Enter(root);
            while (stack.Count > 0)
            {
                var (id, next) = stack.Peek();
                if (next.MoveNext())
                {
                    var target = next.Current;
                    if (!state.TryGetValue(target, out var targetState)) Enter(target);
                    else if (targetState == 1) loops.Add(path[path.IndexOf(target)..]);
                    continue;
                }
                state[id] = 2;
                path.RemoveAt(path.Count - 1);
                stack.Pop();
            }
        }
        return loops;
    }
}
