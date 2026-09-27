// Copyright (c) 2026 Nkosenhle. Licensed under AGPL-3.0.
// Source: CompliSA — Compliance & Tender Readiness Platform

using complisa.domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace complisa.infrastructure.Persistence.Interceptors;

/// <summary>
/// Turns deletes of <see cref="ISoftDeletable"/> entities into updates: the entry's state goes
/// from Deleted to Modified and the entity is stamped via <see cref="ISoftDeletable.MarkDeleted"/>.
/// Registered after AuditSaveChangesInterceptor, so the audit trail still records the operation
/// as a Delete (it captures entry states before this interceptor rewrites them).
/// </summary>
public sealed class SoftDeleteInterceptor(ICurrentActor currentActor, IClock clock) : SaveChangesInterceptor
{
    private readonly ICurrentActor _currentActor = currentActor;
    private readonly IClock _clock = clock;

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ConvertDeletes(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ConvertDeletes(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ConvertDeletes(DbContext? context)
    {
        if (context is null)
            return;

        var now = _clock.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries<ISoftDeletable>()
                     .Where(e => e.State == EntityState.Deleted)
                     .ToList())
        {
            entry.State = EntityState.Modified;
            entry.Entity.MarkDeleted(_currentActor.UserId, now);
        }
    }
}
