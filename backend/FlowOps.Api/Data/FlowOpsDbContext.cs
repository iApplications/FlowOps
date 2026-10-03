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

    private void EnsureAuditAppendOnly()
    {
        if (ChangeTracker.Entries<AuditEvent>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Audit events are append-only.");
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<FlowOps.Api.Forms.FormDefinition>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.Property(x => x.Description).HasMaxLength(2000);
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
