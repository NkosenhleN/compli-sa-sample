// Copyright (c) 2026 Nkosenhle. Licensed under AGPL-3.0.
// Source: CompliSA — Compliance & Tender Readiness Platform

using Asp.Versioning;
using System.Net.Mime;
using complisa.application.DTOs.CompanyAdmin;
using complisa.application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using complisa.application.Common;

namespace complisa.api.Controller;

/// <summary>Company admin: a company's members. The service requires the caller to be Owner or Admin.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/companies/{companyId:guid}/members")]
[Authorize]
[Produces(MediaTypeNames.Application.Json)]
public sealed class CompanyMembersController(ICompanyMemberService memberService) : ControllerBase
{
    private readonly ICompanyMemberService _memberService = memberService;

    private string GetUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();

    /// <summary>
    /// Current members, paged, optionally searched by name or email.
    /// GET /api/v1/companies/{companyId}/members
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetMembersAsync(
        Guid companyId,
        [FromQuery] string? search = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = PaginationDefaults.DefaultPageSize,
        CancellationToken ct = default)
    {
        var filter = new CompanyMemberFilterDto(search, pageNumber, pageSize);
        var result = await _memberService.GetMembersAsync(companyId, filter, GetUserId(), ct);

        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// Changes a member's company role.
    /// PUT /api/v1/companies/{companyId}/members/{memberUserId}/role
    /// </summary>
    [HttpPut("{memberUserId}/role")]
    public async Task<IActionResult> ChangeRoleAsync(
        Guid companyId,
        string memberUserId,
        [FromBody] ChangeMemberRoleDto dto,
        CancellationToken ct)
    {
        var command = new ChangeMemberRoleCommand(companyId, memberUserId, dto.Role);
        var result = await _memberService.ChangeRoleAsync(command, GetUserId(), ct);

        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// Removes a member (soft delete) — their access to the company ends immediately.
    /// DELETE /api/v1/companies/{companyId}/members/{memberUserId}
    /// </summary>
    [HttpDelete("{memberUserId}")]
    public async Task<IActionResult> RemoveAsync(
        Guid companyId,
        string memberUserId,
        CancellationToken ct)
    {
        var result = await _memberService.RemoveAsync(companyId, memberUserId, GetUserId(), ct);

        return result.Success ? Ok(result) : BadRequest(result);
    }
}
