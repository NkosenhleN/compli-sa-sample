// Copyright (c) 2026 Nkosenhle. Licensed under AGPL-3.0.
// Source: CompliSA — Compliance & Tender Readiness Platform

namespace complisa.domain.Interfaces;

/// <summary>
/// Entities implementing this are never physically deleted. Removing one through a repository
/// is turned into <see cref="MarkDeleted"/> by the SoftDeleteInterceptor, and a global EF Core
/// query filter hides deleted rows from every query (use IncludingDeleted() to see them).
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; }
    DateTime? DeletedAt { get; }
    string? DeletedByUserId { get; }

    /// <summary>Flags the entity as deleted. Idempotent — a second call keeps the original stamp.</summary>
    void MarkDeleted(string? deletedByUserId, DateTime utcNow);
}
