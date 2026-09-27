// Copyright (c) 2026 Nkosenhle. Licensed under AGPL-3.0.
// Source: CompliSA — Compliance & Tender Readiness Platform

using complisa.domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace complisa.infrastructure.Persistence.Configurations;

public sealed class CompanyInvitationConfiguration : IEntityTypeConfiguration<CompanyInvitation>
{
    public void Configure(EntityTypeBuilder<CompanyInvitation> builder)
    {
        builder.ToTable("CompanyInvitations");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Email)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(i => i.CompanyRole)
            .HasConversion<string>()
            .HasColumnType("nvarchar(20)")
            .IsRequired();

        // Hex-encoded SHA-256 of the emailed token.
        builder.Property(i => i.TokenHash)
            .HasColumnType("char(64)")
            .IsRequired();

        builder.Property(i => i.InvitedByUserId)
            .HasMaxLength(450)
            .IsRequired();

        builder.Property(i => i.AcceptedByUserId)
            .HasMaxLength(450);

        builder.HasOne(i => i.Company)
            .WithMany()
            .HasForeignKey(i => i.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(i => i.TokenHash).IsUnique();
        builder.HasIndex(i => new { i.CompanyId, i.Email });
    }
}
