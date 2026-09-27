// Copyright (c) 2026 Nkosenhle. Licensed under AGPL-3.0.
// Source: CompliSA — Compliance & Tender Readiness Platform

using complisa.application.DTOs.CompanyAdmin;
using complisa.application.Interfaces;
using complisa.domain.Entities;
using complisa.domain.Enums;
using complisa.domain.Exceptions;
using complisa.domain.Interfaces;
using complisa.domain.Interfaces.Repositories;
using complisa.application.Common;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace complisa.application.Services;

/// <summary>
/// Rules: Owner or Admin may manage members; only an Owner may grant, change or remove the Owner
/// role; nobody changes or removes themselves (so an admin can't accidentally lock themselves
/// out); and a company always keeps at least one Owner.
/// </summary>
public sealed class CompanyMemberService(
    ICompanyMemberRepository memberRepository,
    ICompanyAccessService companyAccessService,
    IUnitOfWork unitOfWork,
    IValidator<ChangeMemberRoleCommand> changeRoleValidator,
    ILogger<CompanyMemberService> logger) : ICompanyMemberService
{
    private const string NoAccessMessage = "You do not have permission to manage members of this company.";
    private const string MemberNotFoundMessage = "Member was not found in this company.";

    private readonly ICompanyMemberRepository _memberRepository = memberRepository;
    private readonly ICompanyAccessService _companyAccessService = companyAccessService;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly IValidator<ChangeMemberRoleCommand> _changeRoleValidator = changeRoleValidator;
    private readonly ILogger<CompanyMemberService> _logger = logger;

    public async Task<ApiResponse<PagedResult<CompanyMemberDto>>> GetMembersAsync(
        Guid companyId,
        CompanyMemberFilterDto filter,
        string userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await _companyAccessService.HasMinimumRoleAsync(companyId, userId, CompanyRole.Admin, cancellationToken))
            {
                _logger.LogWarning(
                    "Access denied: user {UserId} attempted to list members of company {CompanyId}.", userId, companyId);
                return ApiResponse<PagedResult<CompanyMemberDto>>.Fail(NoAccessMessage);
            }

            var pageNumber = Math.Max(filter.PageNumber, 1);
            var pageSize = PaginationDefaults.Clamp(filter.PageSize);

            var (items, totalCount) = await _memberRepository.GetPagedAsync(
                new CompanyMemberQuery(companyId, filter.Search, pageNumber, pageSize),
                cancellationToken);

            return ApiResponse<PagedResult<CompanyMemberDto>>.Ok(
                PagedResult<CompanyMemberDto>.Create(items.Select(ToDto).ToList(), totalCount, pageNumber, pageSize));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load members of company {CompanyId} for user {UserId}.", companyId, userId);
            return ApiResponse<PagedResult<CompanyMemberDto>>.Fail("Failed to load members.");
        }
    }

    public async Task<ApiResponse<CompanyMemberDto>> ChangeRoleAsync(
        ChangeMemberRoleCommand command,
        string userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var validation = await _changeRoleValidator.ValidateAsync(command, cancellationToken);
            if (!validation.IsValid)
                return ApiResponse<CompanyMemberDto>.Fail(
                    string.Join("; ", validation.Errors.Select(e => e.ErrorMessage)));

            var caller = await _companyAccessService.GetAccessAsync(command.CompanyId, userId, cancellationToken);
            if (!IsAdmin(caller))
            {
                _logger.LogWarning(
                    "Access denied: user {UserId} attempted to change a member role in company {CompanyId}.",
                    userId, command.CompanyId);
                return ApiResponse<CompanyMemberDto>.Fail(NoAccessMessage);
            }

            if (command.MemberUserId == userId)
                return ApiResponse<CompanyMemberDto>.Fail("You can't change your own role.");

            var member = await _memberRepository.GetAsync(command.CompanyId, command.MemberUserId, cancellationToken);
            if (member is null)
                return ApiResponse<CompanyMemberDto>.Fail(MemberNotFoundMessage);

            if (member.CompanyRole == command.Role)
                return ApiResponse<CompanyMemberDto>.Ok(ToDto(member), "Role unchanged.");

            var touchesOwner = member.CompanyRole == CompanyRole.Owner || command.Role == CompanyRole.Owner;
            if (touchesOwner && caller!.CompanyRole != CompanyRole.Owner)
            {
                _logger.LogWarning(
                    "Role change rejected: admin {UserId} attempted to change Owner role for {MemberUserId} in company {CompanyId}.",
                    userId, command.MemberUserId, command.CompanyId);
                return ApiResponse<CompanyMemberDto>.Fail("Only an Owner can grant or change the Owner role.");
            }

            if (member.CompanyRole == CompanyRole.Owner &&
                await _memberRepository.CountOwnersAsync(command.CompanyId, cancellationToken) <= 1)
                return ApiResponse<CompanyMemberDto>.Fail("A company must keep at least one Owner.");

            var previousRole = member.CompanyRole;
            member.ChangeRole(command.Role);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Member role changed: {MemberUserId} in company {CompanyId} from {PreviousRole} to {NewRole} by {UserId}.",
                command.MemberUserId, command.CompanyId, previousRole, command.Role, userId);

            return ApiResponse<CompanyMemberDto>.Ok(ToDto(member), "Role updated.");
        }
        catch (ConcurrencyException ex)
        {
            _logger.LogWarning(
                "Concurrency conflict changing role of {MemberUserId} in company {CompanyId}: {Reason}.",
                command.MemberUserId, command.CompanyId, ex.Message);
            return ApiResponse<CompanyMemberDto>.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to change role of {MemberUserId} in company {CompanyId} for user {UserId}.",
                command.MemberUserId, command.CompanyId, userId);
            return ApiResponse<CompanyMemberDto>.Fail("Failed to change member role.");
        }
    }

    public async Task<ApiResponse> RemoveAsync(
        Guid companyId,
        string memberUserId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var caller = await _companyAccessService.GetAccessAsync(companyId, userId, cancellationToken);
            if (!IsAdmin(caller))
            {
                _logger.LogWarning(
                    "Access denied: user {UserId} attempted to remove a member from company {CompanyId}.",
                    userId, companyId);
                return ApiResponse.Fail(NoAccessMessage);
            }

            if (memberUserId == userId)
                return ApiResponse.Fail("You can't remove yourself from the company.");

            var member = await _memberRepository.GetAsync(companyId, memberUserId, cancellationToken);
            if (member is null)
                return ApiResponse.Fail(MemberNotFoundMessage);

            if (member.CompanyRole == CompanyRole.Owner)
            {
                if (caller!.CompanyRole != CompanyRole.Owner)
                {
                    _logger.LogWarning(
                        "Removal rejected: admin {UserId} attempted to remove Owner {MemberUserId} from company {CompanyId}.",
                        userId, memberUserId, companyId);
                    return ApiResponse.Fail("Only an Owner can remove another Owner.");
                }

                if (await _memberRepository.CountOwnersAsync(companyId, cancellationToken) <= 1)
                    return ApiResponse.Fail("A company must keep at least one Owner.");
            }

            _memberRepository.Remove(member);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Member removed: {MemberUserId} from company {CompanyId} by {UserId}.", memberUserId, companyId, userId);

            return ApiResponse.Ok("Member removed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to remove member {MemberUserId} from company {CompanyId} for user {UserId}.",
                memberUserId, companyId, userId);
            return ApiResponse.Fail("Failed to remove member.");
        }
    }

    private static bool IsAdmin(CompanyUser? companyUser) =>
        companyUser?.CompanyRole is CompanyRole.Owner or CompanyRole.Admin;

    private static CompanyMemberDto ToDto(CompanyUser member) =>
        new(
            member.UserId,
            member.User?.UserName ?? member.UserId,
            member.User?.Email,
            member.CompanyRole.ToString(),
            member.JoinedAt,
            member.User?.IsActive ?? true);
}
