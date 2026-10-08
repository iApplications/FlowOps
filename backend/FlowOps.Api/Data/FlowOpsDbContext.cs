using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FlowOps.Api.Data;

public sealed class FlowUser : IdentityUser
{
    public string AccountSurface { get; set; } = "Portal";
    public string DisplayName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsDisabled { get; set; }
}

public sealed class Organization
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public bool RequireMfaForPrivileged { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<OrganizationMember> Members { get; set; } = [];
}

public static class OrganizationRoles
{
    public const string Owner = "Owner";
    public const string Admin = "Admin";
    public const string Member = "Member";

    public static bool IsValid(string role) => role is Owner or Admin or Member;
    public static bool IsPrivileged(string role) => role is Owner or Admin;
}

public sealed class OrganizationMember
{
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public required string UserId { get; set; }
    public FlowUser User { get; set; } = null!;
    public required string Role { get; set; }
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class BrowserSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string UserId { get; set; }
    public Guid? ActiveOrganizationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddHours(8);
    public DateTimeOffset? RevokedAt { get; set; }
    public bool MfaSatisfied { get; set; }
}

public sealed class AuditEvent
{
    public long Id { get; set; }
    public Guid? OrganizationId { get; set; }
    public string? ActorUserId { get; set; }
    public required string Action { get; set; }
    public string? SubjectId { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class FlowOpsDbContext(DbContextOptions<FlowOpsDbContext> options)
    : IdentityDbContext<FlowUser>(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMember> OrganizationMembers => Set<OrganizationMember>();
    public DbSet<BrowserSession> BrowserSessions => Set<BrowserSession>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<FlowOps.Api.Forms.FormDefinition> Forms => Set<FlowOps.Api.Forms.FormDefinition>();
    public DbSet<FlowOps.Api.Forms.FormPublication> FormPublications => Set<FlowOps.Api.Forms.FormPublication>();
    public DbSet<FlowOps.Api.Forms.FormSubmission> FormSubmissions => Set<FlowOps.Api.Forms.FormSubmission>();
    public DbSet<FlowOps.Api.Forms.FormAttachment> FormAttachments => Set<FlowOps.Api.Forms.FormAttachment>();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnsureAuditAppendOnly();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        EnsureAuditAppendOnly();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnsureAuditAppendOnly();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureAuditAppendOnly();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    private void EnsureAuditAppendOnly()
    {
        if (ChangeTracker.Entries<FlowOps.Api.Forms.FormAttachment>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Submitted attachments are immutable.");
        if (ChangeTracker.Entries<FlowOps.Api.Forms.FormSubmission>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Form submissions are immutable.");
        if (ChangeTracker.Entries<FlowOps.Api.Forms.FormPublication>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Published forms are immutable.");
        if (ChangeTracker.Entries<AuditEvent>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Audit events are append-only.");
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<FlowOps.Api.Forms.FormAttachment>(entity =>
        {
            entity.Property(x => x.FileName).HasMaxLength(180);
            entity.Property(x => x.Sha256).HasMaxLength(64);
            entity.HasIndex(x => new { x.SubmissionId, x.FieldId }).IsUnique();
            entity.HasOne<FlowOps.Api.Forms.FormSubmission>().WithMany().HasForeignKey(x => x.SubmissionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<FlowOps.Api.Forms.FormSubmission>(entity =>
        {
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.RequestFingerprint).HasMaxLength(64);
            entity.HasIndex(x => new { x.OrganizationId, x.FormId, x.UserId });
            entity.HasOne<FlowOps.Api.Forms.FormPublication>().WithMany().HasForeignKey(x => x.PublicationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<FlowOps.Api.Forms.FormDefinition>().WithMany().HasForeignKey(x => x.FormId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<FlowUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<FlowOps.Api.Forms.FormPublication>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.HasIndex(x => new { x.FormId, x.VersionNumber }).IsUnique();
            entity.HasIndex(x => x.OrganizationId);
            entity.HasOne<FlowOps.Api.Forms.FormDefinition>().WithMany().HasForeignKey(x => x.FormId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<FlowOps.Api.Forms.FormDefinition>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.DraftRevision).IsConcurrencyToken();
            entity.HasIndex(x => new { x.OrganizationId, x.Name });
            entity.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<FlowUser>().Property(x => x.DisplayName).HasMaxLength(120);
        builder.Entity<FlowUser>().Property(x => x.AccountSurface).HasMaxLength(16);
        builder.Entity<FlowUser>().HasIndex(x => new { x.AccountSurface, x.NormalizedEmail }).IsUnique();
        builder.Entity<Organization>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(120);
        });
        builder.Entity<OrganizationMember>(entity =>
        {
            entity.HasKey(x => new { x.OrganizationId, x.UserId });
            entity.Property(x => x.Role).HasMaxLength(32);
            entity.HasOne(x => x.Organization).WithMany(x => x.Members)
                .HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.UserId);
        });
        builder.Entity<BrowserSession>(entity =>
        {
            entity.HasIndex(x => x.UserId);
            entity.HasOne<FlowUser>().WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<AuditEvent>(entity =>
        {
            entity.Property(x => x.Action).HasMaxLength(100);
            entity.Property(x => x.SubjectId).HasMaxLength(128);
            entity.HasIndex(x => new { x.OrganizationId, x.OccurredAt });
        });
    }
}
