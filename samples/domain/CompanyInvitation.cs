// Copyright (c) 2026 Nkosenhle. Licensed under AGPL-3.0.
// Source: CompliSA — Compliance & Tender Readiness Platform

using complisa.domain.Attributes;
using complisa.domain.Enums;
using complisa.domain.Interfaces;

namespace complisa.domain.Entities;

/// <summary>
/// An emailed invitation to join a company. Only a SHA-256 hash of the token is stored — the raw
/// token exists only in the email link — so a database leak can't be replayed into memberships.
/// </summary>
public sealed class CompanyInvitation : IAuditable, ICompanyScoped
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public CompanyRole CompanyRole { get; private set; }

    [DoNotAudit]
    public string TokenHash { get; private set; } = string.Empty;

    public string InvitedByUserId { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? AcceptedAt { get; private set; }
    public string? AcceptedByUserId { get; private set; }
    public DateTime? RevokedAt { get; private set; }

    public Company Company { get; private set; } = null!;

    private CompanyInvitation()
    {
    }

    public static CompanyInvitation Create(NewCompanyInvitation invitation, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(invitation);

        if (invitation.CompanyId == Guid.Empty)
            throw new ArgumentException("Company id is required.", nameof(invitation));

        if (string.IsNullOrWhiteSpace(invitation.Email))
            throw new ArgumentException("Email is required.", nameof(invitation));

        if (invitation.Role == CompanyRole.Owner)
            throw new ArgumentException(
                "Invitations can't grant Owner. Invite as Admin, then have an Owner change the role.",
                nameof(invitation));

        if (string.IsNullOrWhiteSpace(invitation.TokenHash))
            throw new ArgumentException("Token hash is required.", nameof(invitation));

        if (string.IsNullOrWhiteSpace(invitation.InvitedByUserId))
            throw new ArgumentException("Inviting user id is required.", nameof(invitation));

        return new CompanyInvitation
        {
            Id = Guid.NewGuid(),
            CompanyId = invitation.CompanyId,
            Email = invitation.Email.Trim(),
            CompanyRole = invitation.Role,
            TokenHash = invitation.TokenHash,
            InvitedByUserId = invitation.InvitedByUserId,
            CreatedAt = utcNow,
            ExpiresAt = utcNow.Add(Lifetime)
        };
    }

    public bool IsPending(DateTime utcNow) =>
        AcceptedAt is null && RevokedAt is null && ExpiresAt > utcNow;

    public bool IsFor(string email) =>
        string.Equals(Email, email?.Trim(), StringComparison.OrdinalIgnoreCase);

    public void Accept(string userId, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User id is required.", nameof(userId));

        if (!IsPending(utcNow))
            throw new InvalidOperationException("This invitation has expired or is no longer valid.");

        AcceptedAt = utcNow;
        AcceptedByUserId = userId;
    }

    public void Revoke(DateTime utcNow)
    {
        if (!IsPending(utcNow))
            throw new InvalidOperationException("Only a pending invitation can be revoked.");

        RevokedAt = utcNow;
    }
}

/// <summary>What's needed to issue a <see cref="CompanyInvitation"/>.</summary>
public sealed record NewCompanyInvitation(
    Guid CompanyId,
    string Email,
    CompanyRole Role,
    string TokenHash,
    string InvitedByUserId);
