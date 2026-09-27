// Copyright (c) 2026 Nkosenhle. Licensed under AGPL-3.0.
// Source: CompliSA — Compliance & Tender Readiness Platform

// Extracted for this sample: in the application this registration sits inline in Program.cs.

using Asp.Versioning;

namespace complisa.api;

public static class ApiVersioningSetup
{
    /// <summary>
    /// URL-segment API versioning: every route is /api/v{version}/..., default and only version 1.0.
    /// Controllers declare [ApiVersion("1.0")] with [Route("api/v{version:apiVersion}/...")]
    /// (see CompanyMembersController); a breaking change adds a v2 controller alongside v1.
    /// </summary>
    public static IServiceCollection AddUrlSegmentApiVersioning(this IServiceCollection services)
    {
        services.AddApiVersioning(options =>
            {
                options.DefaultApiVersion = new ApiVersion(1, 0);
                options.ReportApiVersions = true; // api-supported-versions response header
                options.ApiVersionReader = new UrlSegmentApiVersionReader();
            })
            .AddMvc()
            .AddApiExplorer(options =>
            {
                options.GroupNameFormat = "'v'VVV"; // "v1" — matches the Swagger document name
                options.SubstituteApiVersionInUrl = true; // Swagger shows /api/v1/..., not /api/v{version}/...
            });

        return services;
    }
}
