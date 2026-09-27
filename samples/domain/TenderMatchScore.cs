// Copyright (c) 2026 Nkosenhle. Licensed under AGPL-3.0.
// Source: CompliSA — Compliance & Tender Readiness Platform

namespace complisa.domain.ValueObjects;

/// <summary>
/// How relevant a tender is to a company: a 0–100 score plus the machine-readable reasons
/// that produced it. Create() is the only path to a validated instance.
/// </summary>
public sealed record TenderMatchScore
{
    public const int MinScore = 0;
    public const int MaxScore = 100;

    public int Score { get; }
    public IReadOnlyList<string> Reasons { get; }

    private TenderMatchScore(int score, IReadOnlyList<string> reasons)
    {
        Score = score;
        Reasons = reasons;
    }

    public static TenderMatchScore Create(int score, IEnumerable<string> reasons)
    {
        if (score is < MinScore or > MaxScore)
            throw new ArgumentOutOfRangeException(
                nameof(score), score, $"Match score must be between {MinScore} and {MaxScore}.");

        ArgumentNullException.ThrowIfNull(reasons);

        var normalised = reasons
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new TenderMatchScore(score, normalised);
    }

    // Value equality on the reasons list, not reference equality — so an unchanged score
    // recalculated by a later job run compares equal to the stored one.
    public bool Equals(TenderMatchScore? other) =>
        other is not null && Score == other.Score && Reasons.SequenceEqual(other.Reasons);

    public override int GetHashCode() => HashCode.Combine(Score, Reasons.Count);
}
