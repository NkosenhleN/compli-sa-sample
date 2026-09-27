// Copyright (c) 2026 Nkosenhle. Licensed under AGPL-3.0.
// Source: CompliSA — Compliance & Tender Readiness Platform

using complisa.application.DTOs.CompanyAdmin;
using complisa.domain.Enums;
using FluentValidation;

namespace complisa.application.Validators.CompanyAdmin;

public sealed class InviteMemberDtoValidator : AbstractValidator<InviteMemberDto>
{
    public InviteMemberDtoValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .MaximumLength(256)
            .EmailAddress();

        RuleFor(x => x.Role)
            .IsInEnum()
            .NotEqual(CompanyRole.Owner)
            .WithMessage("Invitations can't grant Owner. Invite as Admin, then have an Owner change the role.");
    }
}

public sealed class ChangeMemberRoleCommandValidator : AbstractValidator<ChangeMemberRoleCommand>
{
    public ChangeMemberRoleCommandValidator()
    {
        RuleFor(x => x.MemberUserId).NotEmpty();
        RuleFor(x => x.Role).IsInEnum();
    }
}

public sealed class InvitationTokenDtoValidator : AbstractValidator<InvitationTokenDto>
{
    public InvitationTokenDtoValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty()
            .MaximumLength(100);
    }
}
