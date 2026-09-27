// Copyright (c) 2026 Nkosenhle. Licensed under AGPL-3.0.
// Source: CompliSA — Compliance & Tender Readiness Platform

namespace complisa.application.Common
{

    /// <summary>
    /// Standard API response wrapper used across all endpoints.
    /// </summary>
    public sealed class ApiResponse<T>
    {
        public bool Success { get; init; }
        public T? Data { get; init; }
        public string Message { get; init; } = string.Empty;
        public IReadOnlyList<string> Errors { get; init; } = [];

        public static ApiResponse<T> Ok(T data, string message = "") =>
            new() { Success = true, Data = data, Message = message };

        public static ApiResponse<T> Fail(string error) =>
            new() { Success = false, Errors = [error] };

    }

    /// <summary>
    /// Non-generic version for responses with no data payload.
    /// </summary>
    public sealed class ApiResponse
    {
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public IReadOnlyList<string> Errors { get; init; } = [];

        public static ApiResponse Ok(string message = "") =>
            new() { Success = true, Message = message };

        public static ApiResponse Fail(string error) =>
            new() { Success = false, Errors = [error] };
    }
}
