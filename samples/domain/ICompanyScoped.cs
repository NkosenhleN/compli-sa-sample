// Copyright (c) 2026 Nkosenhle. Licensed under AGPL-3.0.
// Source: CompliSA — Compliance & Tender Readiness Platform

namespace complisa.domain.Interfaces;

/// <summary>
/// An entity owned by a single company (tenant). AuditSaveChangesInterceptor stamps the
/// CompanyId onto the audit rows it writes for these, which is what scopes the company audit
/// log viewer.
/// </summary>
public interface ICompanyScoped
{
    Guid CompanyId { get; }
}
