namespace FlowOps.Api.Data;

public static class AuditService
{
    // Record only metadata; credentials and tokens must never be copied into audit events.
    public static void Append(FlowOpsDbContext db, string action, string? actorUserId,
        string? subjectId = null, Guid? organizationId = null)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Action = action,
            ActorUserId = actorUserId,
            SubjectId = subjectId,
            OrganizationId = organizationId
        });
    }
}
