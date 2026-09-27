# CompliSA: Code Samples

**A multi-tenant compliance and tender readiness SaaS platform built with Clean Architecture.**

CompliSA helps small businesses track their statutory obligations and supporting documents, get
alerted before deadlines and expiries, and see which government tenders they're ready to bid on.
Companies are tenants: users belong to one or more companies with a per-company role, and every
piece of company data is scoped to the company that owns it.

> This is a curated code sample. The full application is in active development.

The samples below were chosen to show how the system is built, not what it knows: they follow one
feature (company membership and email invitations) through every layer, plus the cross-cutting
pieces around it (soft delete, API versioning, error handling, token refresh). Domain-specific
compliance rules, tender matching logic, integration details and seed data are not included.

## Tech stack

| Area | Technology |
|---|---|
| Backend | .NET 10, C# 14, ASP.NET Core Web API |
| Data | EF Core 10, SQL Server |
| Frontend | Angular 20 (standalone components, signals, functional guards and interceptors), Angular Material 20, RxJS, TypeScript |
| Auth | ASP.NET Core Identity, JWT access tokens, rotating refresh tokens in an httpOnly cookie with CSRF protection |
| Validation | FluentValidation 12 |
| Background jobs | Hangfire |
| Resilience | Polly (retry, timeout and shared circuit breakers for external APIs) |
| Logging | Serilog, structured throughout |
| API | URL-segment versioning (`Asp.Versioning`), Swagger/OpenAPI |
| Testing | xUnit v3, Moq, EF Core InMemory for infrastructure tests |

## Architecture

Clean Architecture with dependencies pointing inward only:

```
                 ┌──────────────────────────────┐
                 │  API (composition root)      │
                 └──────┬─────────────────┬─────┘
                        │                 │
          ┌─────────────▼──────┐   ┌──────▼───────────────┐
          │  Application       │   │  Infrastructure      │
          └─────────────┬──────┘   └──────┬───────────────┘
                        │                 │
                 ┌──────▼─────────────────▼─────┐
                 │  Domain                      │
                 └──────────────────────────────┘
```

- **Domain**: entities that own their behaviour and invariants (factory methods, private setters,
  guard clauses), value objects, enums, and the repository and unit-of-work interfaces. It depends
  on nothing else in the solution.
- **Application**: services that orchestrate use cases: validate input, check the caller's access,
  call domain behaviour, persist through the unit of work, and return an `ApiResponse` instead of
  throwing for expected failures. Also holds DTOs, validators and background job classes.
- **Infrastructure**: EF Core (`DbContext`, per-entity configurations, interceptors), repository
  implementations, email, file storage, and clients for external APIs. It implements Domain's
  interfaces and never references Application.
- **API**: thin controllers (read the request, call one service method, return the result),
  authentication and authorization, middleware, and all dependency-injection wiring.
- **Angular client**: a separate single-page app that talks to the API over HTTP.

[`ARCHITECTURE.md`](ARCHITECTURE.md) is the full architecture reference, with the conventions for
each layer and the reasoning behind them.

## Key patterns demonstrated

- **Clean Architecture** with an enforced dependency rule (Infrastructure has no reference to
  Application; job registration lives in the composition root for that reason).
- **Rich domain model**: entities change state only through methods that check their own rules
  (`CompanyInvitation.Accept`, `CompanyUser.ChangeRole`); value objects validate on creation.
- **Command and query objects**: use cases take a single command or filter object
  (`ChangeMemberRoleCommand`, `CompanyMemberFilterDto`) rather than long parameter lists, and reads
  are kept separate from writes in services and repositories.
- **Result pattern**: `ApiResponse<T>` for expected failures (not found, forbidden, validation);
  exceptions only for unexpected ones, caught once by global middleware.
- **Soft delete**: an interceptor turns deletes into flagged updates, and a model-building
  convention gives every soft-deletable entity a global query filter automatically.
- **API versioning**: every route is `/api/v{version}/...`, so a breaking change becomes a new
  controller version alongside the old one.
- **JWT + refresh tokens**: short-lived access tokens kept in memory, a rotating refresh token in an
  httpOnly cookie, and an Angular interceptor that refreshes once and replays queued requests.
- **Multi-tenancy**: row-level isolation; every tenant-scoped operation checks the caller's
  company role, and company-owned entities are tagged (`ICompanyScoped`) so the audit trail can be
  shown per tenant.
- **Secure tokens**: emailed invitation tokens are stored only as SHA-256 hashes, so a database
  leak can't be replayed.

## Samples

| Folder | File | What it shows |
|---|---|---|
| `samples/domain` | `CompanyInvitation.cs` | Factory method with guard clauses, a hashed single-use token, expiry and state transitions |
| | `CompanyUser.cs` | Tenant membership as a soft-deletable entity that can be restored on re-invitation |
| | `TenderMatchScore.cs` | Value object: validated on creation, with value equality over a list |
| | `ISoftDeletable.cs`, `ICompanyScoped.cs` | Marker interfaces that drive conventions in Infrastructure |
| `samples/application` | `CompanyMemberService.cs` | Use-case orchestration: validation, access checks, business rules (a company always keeps an Owner), unit of work, structured logging |
| | `CompanyAdminValidators.cs` | Declarative FluentValidation rules |
| | `ApiResponse.cs` | The result wrapper every endpoint returns |
| | `SecureTokens.cs` | Random URL-safe tokens and their SHA-256 hashes |
| `samples/infrastructure` | `SoftDeleteInterceptor.cs` | `SaveChangesInterceptor` that turns deletes into soft deletes |
| | `SoftDeleteQueryFilterConvention.cs` | Global query filter for every `ISoftDeletable` entity, by convention |
| | `SoftDeleteQueryExtensions.cs` | Opting out of the filter for admin views |
| | `CompanyMemberRepository.cs` | Tenant-scoped repository with paging and search |
| | `CompanyInvitationConfiguration.cs` | `IEntityTypeConfiguration` with enum-as-string, indexes and relationships |
| `samples/api` | `CompanyMembersController.cs` | Versioned, thin controller |
| | `ApiVersioningSetup.cs` | URL-segment API versioning and Swagger integration |
| | `ExceptionMiddleware.cs` | Global exception handling that returns an `ApiResponse` |
| `samples/angular` | `interceptors/auth-interceptor.ts` | Functional interceptor: attaches the JWT, refreshes once on 401 and replays waiting requests |
| | `interceptors/error-interceptor.ts`, `http-errors.ts` | Global HTTP error handling with a clear split of responsibilities |
| | `register/register.ts`, `register.html` | Standalone component with a typed reactive form and a cross-field validator |

The samples are excerpts of a larger codebase, so they reference types that aren't included here.
Two files are small extractions made for readability (`ApiVersioningSetup.cs` and
`SoftDeleteQueryFilterConvention.cs`); their code runs inline in the application's startup and
`DbContext`.

## License

© 2026 Nkosenhle. Licensed under AGPL-3.0. See [`LICENSE`](LICENSE).
