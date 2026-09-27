// Copyright (c) 2026 Nkosenhle. Licensed under AGPL-3.0.
// Source: CompliSA — Compliance & Tender Readiness Platform

using complisa.domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace complisa.infrastructure.Persistence;

public static class SoftDeleteQueryExtensions
{
    /// <summary>
    /// For admin/audit scenarios only: includes soft-deleted rows. Note this lifts every global
    /// query filter on the query (EF Core ignores filters per query, not per filter), so apply
    /// any other scoping — tenant access in particular — explicitly.
    /// </summary>
    public static IQueryable<T> IncludingDeleted<T>(this IQueryable<T> query)
        where T : class, ISoftDeletable =>
        query.IgnoreQueryFilters();

    /// <summary>Only the soft-deleted rows.</summary>
    public static IQueryable<T> OnlyDeleted<T>(this IQueryable<T> query)
        where T : class, ISoftDeletable =>
        query.IgnoreQueryFilters().Where(e => e.IsDeleted);
}
