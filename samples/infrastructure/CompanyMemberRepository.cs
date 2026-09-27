// Copyright (c) 2026 Nkosenhle. Licensed under AGPL-3.0.
// Source: CompliSA — Compliance & Tender Readiness Platform

using complisa.domain.Entities;
using complisa.domain.Enums;
using complisa.domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace complisa.infrastructure.Persistence.Repositories;

public sealed class CompanyMemberRepository(AppContext context) : ICompanyMemberRepository
{
    private readonly AppContext _context = context;

    public async Task<(IReadOnlyList<CompanyUser> Items, int TotalCount)> GetPagedAsync(
        CompanyMemberQuery query,
        CancellationToken cancellationToken = default)
    {
        var members = _context.CompanyUsers
            .AsNoTracking()
            .Include(cu => cu.User)
            .Where(cu => cu.CompanyId == query.CompanyId);

        if (!string.IsNullOrWhiteSpace(query.Search))
            members = members.Where(cu =>
                (cu.User.UserName != null && cu.User.UserName.Contains(query.Search)) ||
                (cu.User.Email != null && cu.User.Email.Contains(query.Search)));

        var totalCount = await members.CountAsync(cancellationToken);

        var items = await members
            .OrderBy(cu => cu.User.UserName)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<CompanyUser?> GetAsync(
        Guid companyId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        return await _context.CompanyUsers
            .Include(cu => cu.User)
            .FirstOrDefaultAsync(cu => cu.CompanyId == companyId && cu.UserId == userId, cancellationToken);
    }

    public async Task<CompanyUser?> GetIncludingRemovedAsync(
        Guid companyId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        // IncludingDeleted lifts every global filter, but the lookup is by full primary key,
        // so no other scoping can be lost.
        return await _context.CompanyUsers
            .IncludingDeleted()
            .FirstOrDefaultAsync(cu => cu.CompanyId == companyId && cu.UserId == userId, cancellationToken);
    }

    public async Task<bool> IsMemberByEmailAsync(
        Guid companyId,
        string email,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToUpperInvariant();

        return await _context.CompanyUsers
            .AsNoTracking()
            .AnyAsync(cu =>
                cu.CompanyId == companyId &&
                cu.User.NormalizedEmail == normalizedEmail,
                cancellationToken);
    }

    public async Task<int> CountOwnersAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        return await _context.CompanyUsers
            .AsNoTracking()
            .CountAsync(cu => cu.CompanyId == companyId && cu.CompanyRole == CompanyRole.Owner, cancellationToken);
    }

    public async Task AddAsync(CompanyUser member, CancellationToken cancellationToken = default)
    {
        await _context.CompanyUsers.AddAsync(member, cancellationToken);
    }

    public void Remove(CompanyUser member)
    {
        _context.CompanyUsers.Remove(member);
    }
}
