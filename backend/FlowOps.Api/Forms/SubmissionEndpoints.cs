using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using FlowOps.Api.Auth;
using FlowOps.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowOps.Api.Forms;

public static class SubmissionEndpoints
{
    public static void MapSubmissionEndpoints(this WebApplication app)
    {
        var portal = app.MapGroup("/api/portal/forms/{formId:guid}/submissions").WithTags("Submissions").RequireAuthorization(Permissions.PortalAccess);
        portal.MapPost("/", Submit).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(256 * 1024));
        portal.MapPost("/with-files", SubmitFiles).WithMetadata(new UploadRequestLimit(LocalFileStorage.MaxRequestBytes)).WithSummary("Submit a published form with attachments");
        portal.MapGet("/", (Guid formId, int? page, HttpContext context, FlowOpsDbContext db) => History(formId, page, context, db, true));
        portal.MapGet("/{submissionId:guid}", (Guid formId, Guid submissionId, HttpContext context, FlowOpsDbContext db) => Detail(formId, submissionId, context, db, true));
        portal.MapGet("/{submissionId:guid}/files/{fileId:guid}", (Guid formId, Guid submissionId, Guid fileId, HttpContext context, FlowOpsDbContext db, LocalFileStorage storage) => Download(formId, submissionId, fileId, context, db, storage, true));
        var studio = app.MapGroup("/api/forms/{formId:guid}/submissions").WithTags("Submissions").RequireAuthorization(Permissions.StudioAccess);
        studio.MapGet("/", (Guid formId, int? page, HttpContext context, FlowOpsDbContext db) => History(formId, page, context, db, false));
        studio.MapGet("/{submissionId:guid}", (Guid formId, Guid submissionId, HttpContext context, FlowOpsDbContext db) => Detail(formId, submissionId, context, db, false));
        studio.MapGet("/{submissionId:guid}/files/{fileId:guid}", (Guid formId, Guid submissionId, Guid fileId, HttpContext context, FlowOpsDbContext db, LocalFileStorage storage) => Download(formId, submissionId, fileId, context, db, storage, false));
    }

    private static IQueryable<FormSubmission> Authorized(Guid formId, HttpContext context, FlowOpsDbContext db, bool ownOnly)
    {
        var session = AuthEndpoints.CurrentSession(context)!;
        return db.FormSubmissions.AsNoTracking().Where(x => x.FormId == formId && x.OrganizationId == session.ActiveOrganizationId && (!ownOnly || x.UserId == session.UserId));
    }

    private static async Task<IResult> History(Guid formId, int? page, HttpContext context, FlowOpsDbContext db, bool ownOnly)
    {
        var session = AuthEndpoints.CurrentSession(context)!;
        if (!await db.Forms.AnyAsync(x => x.Id == formId && x.OrganizationId == session.ActiveOrganizationId)) return Results.NotFound();
        var pageNumber = page ?? 1;
        if (pageNumber is < 1 or > 100000) return Results.BadRequest(new { error = "Invalid history page." });
        var query = Authorized(formId, context, db, ownOnly);
        var total = await query.CountAsync();
        var rows = await (from submission in query
            join publication in db.FormPublications on submission.PublicationId equals publication.Id
            join user in db.Users on submission.UserId equals user.Id
            orderby submission.SubmittedAt descending, submission.Id
            select new { submission.Id, submission.PublicationId, publication.VersionNumber, publication.Name,
                submission.SubmittedAt, SubmittedBy = ownOnly ? null : user.DisplayName, Email = ownOnly ? null : user.Email })
            .Skip((pageNumber - 1) * 25).Take(25).ToListAsync();
        return Results.Ok(new { items = rows, total, page = pageNumber, pageSize = 25 });
    }

    private static async Task<IResult> Detail(Guid formId, Guid submissionId, HttpContext context, FlowOpsDbContext db, bool ownOnly)
    {
        var submission = await Authorized(formId, context, db, ownOnly).SingleOrDefaultAsync(x => x.Id == submissionId);
        if (submission is null) return Results.NotFound(new { error = "Submission not found." });
        var publication = await db.FormPublications.AsNoTracking().SingleAsync(x => x.Id == submission.PublicationId && x.OrganizationId == submission.OrganizationId);
        var files = await db.FormAttachments.AsNoTracking().Where(x => x.SubmissionId == submission.Id && x.OrganizationId == submission.OrganizationId).ToListAsync();
        return Results.Ok(new { submission, form = publication, files });
    }

    private static async Task<IResult> Download(Guid formId, Guid submissionId, Guid fileId, HttpContext context, FlowOpsDbContext db, LocalFileStorage storage, bool ownOnly)
    {
        var submission = await Authorized(formId, context, db, ownOnly).SingleOrDefaultAsync(x => x.Id == submissionId);
        if (submission is null) return Results.NotFound(new { error = "File not found." });
        var file = await db.FormAttachments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == fileId && x.SubmissionId == submission.Id && x.OrganizationId == submission.OrganizationId);
        if (file is null || !File.Exists(storage.PathFor(file))) return Results.NotFound(new { error = "File not found." });
        context.Response.Headers.CacheControl = "private, no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Results.File(storage.PathFor(file), "application/octet-stream", file.FileName);
    }

    private static async Task<IResult> SubmitFiles(Guid formId, HttpContext context, FlowOpsDbContext db, LocalFileStorage storage)
    {
        if (!context.Request.HasFormContentType || !context.Request.ContentType!.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new { error = "Send form values and files as multipart form data." });
        if (context.Request.ContentLength > LocalFileStorage.MaxRequestBytes) return Results.BadRequest(new { error = "Files must total 20 MB or less." });
        var sizeFeature = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false }) sizeFeature.MaxRequestBodySize = LocalFileStorage.MaxRequestBytes;
        var savedFiles = new List<FormAttachment>();
        var committed = false;
        try
        {
            var multipart = await context.Request.ReadFormAsync(new Microsoft.AspNetCore.Http.Features.FormOptions
            { MultipartBodyLengthLimit = LocalFileStorage.MaxRequestBytes, ValueLengthLimit = 256 * 1024, ValueCountLimit = 51 }, context.RequestAborted);
            if (multipart.Count != 1 || multipart["request"].Count != 1 || multipart["request"][0]!.Length > 256 * 1024)
                return Results.BadRequest(new { error = "Provide one submission request." });
            var request = JsonSerializer.Deserialize<SubmitFormRequest>(multipart["request"][0]!, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (request is null || request.RequestId == Guid.Empty || request.Values is null) return Results.BadRequest(new { error = "Provide valid submission values and a request ID." });
            var session = AuthEndpoints.CurrentSession(context)!;
            var publication = await db.FormPublications.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.PublicationId && x.FormId == formId && x.OrganizationId == session.ActiveOrganizationId);
            if (publication is null) return Results.NotFound(new { error = "Published form not found in the active organization." });
            var fileFields = publication.Fields.Where(x => x.Type == "File").ToDictionary(x => x.Id);
            if (multipart.Files.Count > fileFields.Count || multipart.Files.Sum(x => x.Length) > LocalFileStorage.MaxTotalBytes)
                return Results.BadRequest(new { error = "Upload one file per File field, totaling at most 20 MB." });
            var uploadedFields = new HashSet<Guid>();
            foreach (var file in multipart.Files)
                if (!Guid.TryParse(file.Name, out var fieldId) || !fileFields.ContainsKey(fieldId) || !uploadedFields.Add(fieldId))
                    return Results.BadRequest(new { error = "Each file must belong to a distinct File field in this published form." });
            if (request.Values.Any(x => fileFields.ContainsKey(x.Key) && x.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)))
                return Results.BadRequest(new { error = "File values must come from uploads, not existing file references." });
            var values = new Dictionary<Guid, JsonElement>(request.Values);
            foreach (var fieldId in uploadedFields) values[fieldId] = JsonSerializer.SerializeToElement(fieldId);
            var errors = SubmissionValidation.Validate(publication.Fields, values, uploadedFields);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            foreach (var file in multipart.Files)
            {
                var fieldId = Guid.Parse(file.Name);
                var saved = await storage.SaveAsync(file, fileFields[fieldId], publication.OrganizationId, request.RequestId, context.RequestAborted);
                savedFiles.Add(saved);
                values[fieldId] = JsonSerializer.SerializeToElement(saved.Id);
            }
            var fingerprintJson = JsonSerializer.Serialize(new {
                values = request.Values.OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value),
                files = savedFiles.OrderBy(x => x.FieldId).Select(x => new { x.FieldId, x.FileName, x.Size, x.Sha256 }) });
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintJson)));
            var existing = await db.FormSubmissions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.RequestId);
            if (existing is not null) return RetryFiles(existing, session.UserId, publication.Id, fingerprint);
            var submission = new FormSubmission { Id = request.RequestId, FormId = formId, PublicationId = publication.Id,
                OrganizationId = publication.OrganizationId, UserId = session.UserId,
                ValuesJson = JsonSerializer.Serialize(values.OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value)), RequestFingerprint = fingerprint };
            db.FormSubmissions.Add(submission);
            db.FormAttachments.AddRange(savedFiles);
            AuditService.Append(db, "form.submitted", session.UserId, submission.Id.ToString(), submission.OrganizationId);
            foreach (var file in savedFiles) AuditService.Append(db, "form.file_uploaded", session.UserId, file.Id.ToString(), submission.OrganizationId);
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException)
            {
                var winner = await db.FormSubmissions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.RequestId);
                if (winner is null) throw;
                return RetryFiles(winner, session.UserId, publication.Id, fingerprint);
            }
            committed = true;
            return Results.Created($"/api/portal/forms/{formId}/submissions/{submission.Id}", new { submission.Id, submission.SubmittedAt });
        }
        catch (JsonException) { return Results.BadRequest(new { error = "Invalid submission JSON." }); }
        catch (InvalidDataException error) { return Results.BadRequest(new { error = error.Message }); }
        catch (BadHttpRequestException) { return Results.BadRequest(new { error = "Invalid upload or total upload size exceeded." }); }
        finally { if (!committed) foreach (var file in savedFiles) storage.Delete(file); }
    }

    private static IResult RetryFiles(FormSubmission existing, string userId, Guid publicationId, string fingerprint) =>
        existing.UserId == userId && existing.PublicationId == publicationId && existing.RequestFingerprint == fingerprint
            ? Results.Ok(new { existing.Id, existing.SubmittedAt })
            : Results.Conflict(new { error = "This request ID was already used. Refresh history before submitting another response." });

    private static async Task<IResult> Submit(Guid formId, SubmitFormRequest request, HttpContext context, FlowOpsDbContext db)
    {
        var session = AuthEndpoints.CurrentSession(context)!;
        var publication = await db.FormPublications.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.PublicationId && x.FormId == formId && x.OrganizationId == session.ActiveOrganizationId);
        if (publication is null) return Results.NotFound(new { error = "Published form not found in the active organization." });
        if (request.RequestId == Guid.Empty) return Results.BadRequest(new { error = "A submission request ID is required." });
        var errors = SubmissionValidation.Validate(publication.Fields, request.Values);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        var valuesJson = JsonSerializer.Serialize(request.Values!.OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value));
        var existing = await db.FormSubmissions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.RequestId);
        if (existing is not null) return Retry(existing, session.UserId, publication.Id, valuesJson);
        var submission = new FormSubmission { Id = request.RequestId, FormId = formId, PublicationId = publication.Id,
            OrganizationId = publication.OrganizationId, UserId = session.UserId, ValuesJson = valuesJson };
        db.FormSubmissions.Add(submission);
        AuditService.Append(db, "form.submitted", session.UserId, submission.Id.ToString(), submission.OrganizationId);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            var winner = await db.FormSubmissions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.RequestId);
            if (winner is null) throw;
            return Retry(winner, session.UserId, publication.Id, valuesJson);
        }
        return Results.Created($"/api/portal/forms/{formId}/submissions/{submission.Id}", new { submission.Id, submission.SubmittedAt });
    }

    private static IResult Retry(FormSubmission existing, string userId, Guid publicationId, string valuesJson) =>
        existing.UserId == userId && existing.PublicationId == publicationId && existing.ValuesJson == valuesJson
            ? Results.Ok(new { existing.Id, existing.SubmittedAt })
            : Results.Conflict(new { error = "This request ID was already used. Reload before submitting a new response." });
}

public sealed record SubmitFormRequest(Guid PublicationId, Guid RequestId, Dictionary<Guid, JsonElement>? Values);
public sealed record UploadRequestLimit(long MaxBytes);
