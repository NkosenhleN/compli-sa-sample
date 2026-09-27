// Copyright (c) 2026 Nkosenhle. Licensed under AGPL-3.0.
// Source: CompliSA — Compliance & Tender Readiness Platform

using complisa.domain.Enums;
using complisa.domain.Interfaces;

namespace complisa.domain.Entities;

/// <summary>
/// A user's membership of a company. Removing a member soft-deletes the row, so the global
/// query filter immediately revokes their access everywhere while the history is kept; a later
/// re-invitation restores the same row.
/// </summary>
public sealed class CompanyUser : IAuditable, ICompanyScoped, ISoftDeletable
{
    public Guid CompanyId { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public CompanyRole CompanyRole { get; private set; }
    public DateTime JoinedAt { get; private set; }

    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAt { get; private set; }
    public string? DeletedByUserId { get; private set; }

    public Company Company { get; private set; } = null!;
    public ApplicationUser User { get; private set; } = null!;

    private CompanyUser()
    {
    }

    public static CompanyUser Create(Guid companyId, string userId, CompanyRole role)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("Company id is required.", nameof(companyId));

        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User id is required.", nameof(userId));

        return new CompanyUser
        {
            CompanyId = companyId,
            UserId = userId,
            CompanyRole = role,
            JoinedAt = DateTime.UtcNow
        };
    }

    public void ChangeRole(CompanyRole role)
    {
        if (IsDeleted)
            throw new InvalidOperationException("Cannot change the role of a removed member.");

        CompanyRole = role;
    }

    /// <summary>Re-admits a previously removed member, as if they had just joined.</summary>
    public void Restore(CompanyRole role, DateTime utcNow)
    {
        if (!IsDeleted)
            throw new InvalidOperationException("Member is not removed.");

        IsDeleted = false;
        DeletedAt = null;
        DeletedByUserId = null;
        CompanyRole = role;
        JoinedAt = utcNow;
    }

    public void MarkDeleted(string? deletedByUserId, DateTime utcNow)
    {
        if (IsDeleted)
            return;

        IsDeleted = true;
        DeletedAt = utcNow;
        DeletedByUserId = deletedByUserId;
    }
}
