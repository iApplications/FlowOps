using System.Text.Json.Nodes;
using FlowOps.Api.Data;
using FlowOps.Api.Forms;
using Microsoft.EntityFrameworkCore;

namespace FlowOps.Api.Workflows;

// Starts one run per workflow whose latest Published version starts from the submitted form (PHASE4 slice 3).
// Runs are idempotent per submission: a submission retry starts only the runs that are still missing, and the
// unique (version, submission) index stops a concurrent retry from starting a second one.
public sealed class FormWorkflowTrigger(FlowOpsDbContext db, WorkflowEngine engine, ILogger<FormWorkflowTrigger> logger)
{
    public async Task StartRunsAsync(FormSubmission submission, FormPublication publication, string correlationId)
    {
        // The latest version of each workflow as published when the submission was saved, so a later retry never
        // starts a workflow published after it.
        var submittedAt = new DateTimeOffset(DateTime.SpecifyKind(submission.SubmittedAt, DateTimeKind.Utc));
        var published = await db.WorkflowVersions.AsNoTracking()
            .Where(x => x.OrganizationId == submission.OrganizationId && x.Status == WorkflowVersionStatus.Published).ToListAsync();
        var started = await db.WorkflowInstances.AsNoTracking().Where(x => x.SourceSubmissionId == submission.Id)
            .Select(x => x.WorkflowDefinitionId).ToListAsync();
        var triggered = published.Where(x => x.PublishedAt <= submittedAt).GroupBy(x => x.WorkflowDefinitionId)
            .Select(x => x.MaxBy(version => version.VersionNumber)!)
            .Where(x => !started.Contains(x.WorkflowDefinitionId) && x.Graph.Nodes.SingleOrDefault(node => node.Type == WorkflowNodeTypes.Start)?.Config is
                { Trigger: WorkflowTriggers.FormSubmitted } config && config.FormId == submission.FormId)
            .OrderBy(x => x.WorkflowDefinitionId).ToList();
        if (triggered.Count == 0) return;
        var workflowIds = triggered.Select(x => x.WorkflowDefinitionId).ToList();
        var workflows = await db.WorkflowDefinitions.AsNoTracking().Where(x => workflowIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var request = Request(publication.Fields, submission);
        var metadata = new JsonObject
        {
            ["id"] = submission.Id.ToString(), ["formId"] = submission.FormId.ToString(), ["formVersion"] = publication.VersionNumber,
            ["submittedAt"] = submission.SubmittedAt.ToString("O")
        };
        foreach (var version in triggered)
        {
            try
            {
                await engine.StartAsync(new(workflows[version.WorkflowDefinitionId], version, WorkflowTriggers.FormSubmitted, submission.UserId,
                    request, correlationId, SourceSubmissionId: submission.Id, Submission: metadata));
            }
            catch (DbUpdateException exception)
            {
                // Usually a concurrent retry of the same submission that already started this run. The submission itself is
                // saved, so one failed start does not stop the others; a retry of the submission starts whatever is missing.
                db.ChangeTracker.Clear();
                logger.LogWarning(exception, "Form-triggered run of version {VersionId} for submission {SubmissionId} was not started ({CorrelationId})",
                    version.Id, submission.Id, correlationId);
            }
        }
    }

    // request.<fieldKey> for every field of the submitted version: numbers, text, dates and options as text, checkboxes
    // as true/false, files as the attachment ID, and unanswered fields as empty values.
    public static JsonObject Request(List<FormField> fields, FormSubmission submission)
    {
        var values = submission.Values;
        var request = new JsonObject();
        foreach (var field in FormFieldKeys.Assign(fields))
            request[field.Key!] = values.TryGetValue(field.Id, out var value) ? JsonNode.Parse(value.GetRawText()) : null;
        return request;
    }
}
