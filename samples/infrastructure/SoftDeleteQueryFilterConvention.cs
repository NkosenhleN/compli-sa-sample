// Copyright (c) 2026 Nkosenhle. Licensed under AGPL-3.0.
// Source: CompliSA — Compliance & Tender Readiness Platform

// Extracted for this sample: in the application this loop runs inline in AppContext.OnModelCreating,
// after the entity configurations are applied.

using System.Linq.Expressions;
using complisa.domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace complisa.infrastructure.Persistence;

public static class SoftDeleteQueryFilterConvention
{
    /// <summary>
    /// Convention, not per-entity configuration: every <see cref="ISoftDeletable"/> entity gets a
    /// global "not deleted" query filter, so a new soft-deletable entity is hidden once deleted
    /// without anyone remembering to add a filter. Pairs with SoftDeleteInterceptor (which turns
    /// deletes into flagged updates) and SoftDeleteQueryExtensions (which lift the filter for
    /// admin views).
    /// </summary>
    public static void ApplySoftDeleteQueryFilters(this ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(t => typeof(ISoftDeletable).IsAssignableFrom(t.ClrType)))
        {
            var entity = Expression.Parameter(entityType.ClrType, "e");
            var notDeleted = Expression.Lambda(
                Expression.Not(Expression.Property(entity, nameof(ISoftDeletable.IsDeleted))),
                entity);

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(notDeleted);
        }
    }
}
