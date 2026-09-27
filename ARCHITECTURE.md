# Clean Architecture Reference

A portable architecture reference for .NET clean-architecture solutions. It's written to seed
new projects, not to document one specific codebase, but every example below is real code from
CompliSA (a .NET 10 / ASP.NET Core Web API + Angular solution), cited by file path, so you can
see the rule and the rule actually followed in the same place.

If you're joining a project that already follows this shape: skim "Solution Structure" and
"Dependency Rule" first, then read whichever layer section you're about to touch. If you're
starting a new project from scratch: read it top to bottom once, then treat it as a checklist
during code review.

> **About this copy.** This is the architecture reference from the CompliSA repository, included
> in the portfolio as-is except that the sections describing the tender ingestion pipeline and the
> tender matching and scoring rules are omitted. File paths refer to the full application; the
> files reproduced here are under [`samples/`](samples/).

## Solution Structure

Four layers, dependencies point inward only:

```
+--------------------------------------------------------------------+
|  API                                                                |
|  Controllers . Middleware . DI wiring                               |
|  (composition root -- the only layer allowed to depend on all 3)    |
+--------------------------------+-----------------------------------+
                                  |
                depends on both  |  of these
                  +---------------------------------+
                  |                                   |
+-----------------v------------------+   +-------------v----------------------+
|  APPLICATION                        |   |  INFRASTRUCTURE                    |
|  Services . DTOs . Validators        |   |  EF Core DbContext . Repositories  |
|  Background job classes              |   |  Email, file storage, Hangfire     |
|  depends on Domain (+ Shared)        |   |  depends on Domain only            |
+-----------------+------------------+   +-------------+----------------------+
                  |                                     |
                  +------------------+------------------+
                             both depend on
                                     |
                       +-------------v--------------+
                       |  DOMAIN                      |
                       |  Entities . Value objects      |
                       |  Repository interfaces          |
                       |  depends on nothing               |
                       +---------------------------------+
```

Note the shape: **Application and Infrastructure are siblings, not a chain.** Infrastructure
does not depend on Application, and Application does not depend on Infrastructure. Both depend
on Domain only. Only API sits above both, wiring concrete Infrastructure implementations to
Application's interfaces at startup. This is the detail people get wrong most often; see
"Dependency Rule" below.

In CompliSA this maps to four projects and an Angular frontend
that talks to the API over HTTP:

| Project | Role |
|---|---|
| `complisa.domain` | Entities, value objects, enums, repository interfaces, `IUnitOfWork` |
| `complisa.application` | Services + interfaces, DTOs, FluentValidation validators, background job classes, and the API contracts in `Common/` (`ApiResponse<T>`, `PagedResult<T>`, `PaginationDefaults`, `[SkipSanitization]`) |
| `complisa.infrastructure` | EF Core `DbContext`, repository implementations, email/file-storage/Hangfire setup |
| `complisa.api` | Controllers, JWT/middleware, composition-root DI wiring |
| `complisa.web.client` | The Angular SPA, the only UI, including company and system administration (outside the .NET solution) |

### What goes in each layer

**Domain**: entities with behavior (not property bags), value objects, enums, repository
*interfaces* (never implementations), `IUnitOfWork`, marker interfaces (`IAuditable`,
`IConcurrencyAware`, `ISoftDeletable`), external-system client contracts (`ICipcClient`,
`IeTenderClient`, `IEmailService`) with their plain data models, `IClock`, and domain-specific
exceptions (`ConcurrencyException`). Nothing here should
require a database, a web framework, or any external package beyond the language itself (an
Identity base class is a rare, explicit exception; see below).

**Application**: service interfaces and implementations that *orchestrate* domain logic,
DTOs, FluentValidation validators, mappers, background job classes. Depends on Domain's
entities and repository interfaces, calls them through those interfaces, never touches EF Core
or any concrete infrastructure.

**Infrastructure** is everything that talks to the outside world: the EF Core `DbContext` and
entity configurations, repository *implementations*, file storage, email sending, background
job scheduling setup (Hangfire's storage/server registration). Implements Domain's repository
interfaces; never references Application.

**API**: thin controllers, authentication/authorization middleware, exception handling,
request logging, and the composition root that wires everything together (`Program.cs`). This
is the only layer allowed to know about all three others, because DI wiring has to.

### What does NOT go in each layer (common mistakes)

- **Domain**: no DTOs, no `ApiResponse<T>`-style wrappers, no `using Microsoft.EntityFrameworkCore`,
  no HTTP status codes, no `ILogger` calls (entities throw, callers decide how to log; see
  "Logging" below), no repository *implementations*.
- **Application**: no `DbContext`, no SQL, no EF Core `Include()`/`Where()` LINQ against a
  context: that's Infrastructure's job behind a repository interface. No controller/HTTP
  concepts (`HttpContext`, `[FromBody]`).
- **Infrastructure**: no business rules ("is this obligation allowed to transition to
  Compliant?" belongs in the `Obligation` entity, not a repository method), no calls into
  Application services (that would create the cycle the Dependency Rule exists to prevent).
- **API**: no business logic, no validation, no manual DTO mapping, no direct `DbContext`
  usage, no try/catch around expected failures (that belongs in the service, returning
  `ApiResponse.Fail(...)`).

## Dependency Rule

**Dependencies point inward only.**

- Domain depends on nothing (inside the solution).
- Application depends on Domain.
- Infrastructure depends on Domain only, **not Application.** This is the rule people bend
  first, usually by having a repository call an Application service "just this once." Don't.
  In CompliSA, `complisa.infrastructure.csproj` has exactly one `ProjectReference`
  (`complisa.domain`); grep for it if you're ever unsure a change has stayed inside the lines.
- API depends on all three, because it's the composition root: it's the only place that can
  legally `new UnitOfWork(...)`-style wire a concrete Infrastructure class to an interface
  Application declared.

### Why this matters

- **Testability**: Application services take repository *interfaces* via constructor
  injection, so unit tests mock them with something like Moq and never touch a real database.
  Domain entities have zero dependencies, so testing a state transition is just
  `obligation.MarkCompliant(date)` and an assertion; no test double needed at all.
- **Swappability**: because Infrastructure only *implements* Domain's contracts, you can
  replace `LocalFileStorageService` with an Azure Blob implementation, or `FileEmailService`
  with `SmtpEmailService`, without touching Application or Domain at all. The interface is
  the seam. CompliSA already does exactly this for email (`IEmailService` → `FileEmailService`
  or `SmtpEmailService`, chosen in DI by the `EmailDelivery` setting, which defaults by
  environment) and for CIPC data (`ICipcClient` → a fallback decorator over the Datanamix client).
- **Enforced boundaries**: a junior developer cannot accidentally put a SQL query in a
  controller if the controller only has an `IObligationService` in its constructor: the
  architecture makes the wrong thing hard to do, not just documented as wrong.

A concrete trap this rule prevents: if a background job class needs to live in Application
(because it orchestrates domain logic through repository interfaces, like any other service),
but its Hangfire *registration* code (`RecurringJob.AddOrUpdate<T>`) is written where it seems
natural (Infrastructure, since that's where Hangfire's storage setup lives), you'll hit a
compile-time dependency violation the moment Infrastructure tries to reference the job class.
The fix is never "let Infrastructure depend on Application just this once"; it's to put the
registration call in the composition root (API) instead, which already legitimately depends on
both. CompliSA's `RecurringJobScheduler` lives in `complisa.api/BackgroundJobs/` for exactly
this reason, even though the Hangfire *storage* setup (`AddHangfireServices`,
`UseHangfireDashboard`) stays in `complisa.infrastructure/BackgroundJobs/HangfireConfiguration.cs`
since that half only needs a connection string, not a reference to Application.

## Domain Layer Conventions

**Rich entities, not anaemic models.** State changes go through methods with guard clauses,
not public setters. Example, `src/complisa.domain/Entities/Obligation.cs`:

```csharp
public sealed class Obligation : IAuditable, IConcurrencyAware, ISoftDeletable
{
    public ObligationStatus Status { get; private set; }   // private setter: outside code
                                                             // cannot do `obligation.Status = x`

    public void MarkCompliant(DateTime completedDate)
    {
        if (Status is ObligationStatus.Compliant or ObligationStatus.Expired)
            throw new InvalidOperationException(
                $"Cannot mark obligation as compliant: current status is '{Status}'.");

        Status = ObligationStatus.Compliant;
        CompletedDate = completedDate;
        UpdatedAt = DateTime.UtcNow;
    }
}
```

Construction goes through a static factory, never a public constructor. This is how the
entity guarantees it can never exist in an invalid state:

```csharp
private Obligation() { }   // for EF Core only

public static Obligation Create(Guid companyId, ObligationType type, string name,
    DateTime deadline, string createdByUserId, string? description = null, string? metadata = null)
{
    if (companyId == Guid.Empty)
        throw new ArgumentException("Company id is required.", nameof(companyId));
    // ... more guard clauses ...
    return new Obligation { Id = Guid.NewGuid(), CompanyId = companyId, /* ... */ };
}
```

**Value objects for validated domain concepts.** A registration number isn't a `string`; it's
a CIPC-format string, and that rule belongs in the type, not scattered across every place a
registration number gets used. `src/complisa.domain/ValueObjects/RegistrationNumber.cs`:

```csharp
public readonly record struct RegistrationNumber
{
    private static readonly Regex Pattern = new(@"^(\d{4})/(\d{6})/(\d{2})$", RegexOptions.Compiled);
    public string Value { get; }
    private RegistrationNumber(string value) { Value = value; }

    public static RegistrationNumber Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Registration number is required.", nameof(value));
        var match = Pattern.Match(value);
        if (!match.Success)
            throw new ArgumentException($"'{value}' is not a valid CIPC registration number...");
        return new RegistrationNumber(value);
    }
}
```

Once a `RegistrationNumber` exists, every layer above Domain can trust it's valid, with no
re-validation needed at every call site.

**Enums for finite domain value sets** (`ObligationStatus`, `DocumentStatus`, `CompanyRole`),
not string constants, not magic numbers. They get persisted as `nvarchar` strings in
Infrastructure (see below), but stay real C# enums in Domain and above.

**Marker interfaces** carry cross-cutting behavior without inheritance:

- `IAuditable`: flags an entity for automatic before/after change capture (see "Audit Trail").
- `IConcurrencyAware`: declares `byte[] RowVersion`, picked up by a reflection-based
  convention in Infrastructure so every implementer automatically gets SQL Server row-version
  optimistic concurrency, with zero per-entity configuration needed.
- `ISoftDeletable`: `IsDeleted`/`DeletedAt`/`DeletedByUserId` and `MarkDeleted()`; implementers
  are never physically deleted and are hidden by a convention-based global query filter (see
  "Soft Delete").

**Time is a dependency.** Entity methods whose outcome depends on "now" take it as a parameter
(`obligation.RecalculateStatus(now)`, `Tender.Create(release, now)`,
`refreshToken.IsActiveAt(now)`); services and jobs get `now` from an injected `IClock`. Tests then
pass any instant instead of reflection-hacking stored dates. Plain `CreatedAt`/`UpdatedAt` stamps
may still use `DateTime.UtcNow`.

**Repository interfaces are per-aggregate, not generic.** `IObligationRepository`, not
`IRepository<Obligation>`. A generic repository either exposes a lowest-common-denominator API
that can't express real query needs (`GetDueSoonAsync(int daysThreshold)`,
`GetOverdueAsync()`), or grows an `IQueryable<T>` escape hatch that leaks EF Core concerns
above Infrastructure. Both defeat the point. Interfaces return domain entities only, never
DTOs (see `src/complisa.domain/Interfaces/Repositories/IObligationRepository.cs`).

**`IUnitOfWork` is the only place that saves.** Repositories add/update/remove entities against
an in-memory change tracker; nothing but `IUnitOfWork.SaveChangesAsync()` commits:

```csharp
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

This means a service can call three different repositories and commit all three changes in one
transaction with a single `SaveChangesAsync()` call at the end; repositories don't each get
their own save method, which would make that impossible.

**No DTOs, no API concepts, no external library references.** Domain doesn't know
`ApiResponse<T>` exists. The one deliberate exception in CompliSA: `complisa.domain` references
`Microsoft.Extensions.Identity.Stores` so `ApplicationUser` can inherit `IdentityUser`, a
narrow, explicit trade-off (real per-user accounts need *some* base to sit on), not a crack
that widens over time. If you take this exception, document why, the way CLAUDE.md does here.

## Application Layer Conventions

**Service interfaces and implementations orchestrate domain logic.** A service's job is:
validate the request, call one or more repository methods, call entity methods to change state,
save via `IUnitOfWork`, map to a response DTO. It should not contain business *rules* that
belong on the entity: `if (obligation.Status == Compliant) throw` belongs in
`Obligation.MarkCompliant()`, not duplicated in the service.

**DTOs per aggregate**: `CreateObligationDto`, `UpdateObligationDto`, plus list/detail shapes as
needed. Keep them as records: immutable, structural equality, and they read as a contract.

**FluentValidation validators, one per DTO**, in `complisa.application/Validators/{Aggregate}/`
(shared rules as extension methods alongside, e.g. `CompanyValidationExtensions`). Validators own
*declarative* rules (required fields, format, length); rules that need a database round trip
(duplicate checks, foreign-key existence) stay in the service, because a validator shouldn't be
issuing queries:

```csharp
public sealed class CreateCompanyDtoValidator : AbstractValidator<CreateCompanyDto>
{
    public CreateCompanyDtoValidator()
    {
        RuleFor(x => x.RegistrationNumber).IsValidCipcNumber();   // shared extension method
        RuleFor(x => x.ContactEmail).NotEmpty().EmailAddress();
    }
}
```

**Static mapper classes, not AutoMapper.** Explicit `static XDto ToDto(X entity)` methods mean
the compiler catches a missing/renamed property at build time instead of silently `null`ing a
field at runtime. CompliSA keeps them in `complisa.application/Mappings/` (`CompanyMapper`,
`ObligationMapper`, `DocumentMapper`, `TenderMapper`). Resist the urge to add an `AutoMapper`
dependency for what a five-line static method does more visibly.

**`CancellationToken` on every async method**, always the last parameter, never counted toward
the parameter limit below.

**Parameter limit: 3 (plus `CancellationToken`).** A fourth parameter beyond `CancellationToken`
means "make a DTO":

```csharp
// Bad
CreateAsync(string name, string regNumber, string taxRef, string province, string email,
    string userId, CancellationToken ct)

// Good
CreateAsync(CreateCompanyDto dto, string userId, CancellationToken ct)
```

Applies to constructors too: more than ~4 injected dependencies is a signal the class is
doing too much and should split. (A background-job orchestrator that fans out to several
collaborators for one cohesive responsibility ("resolve recipients, dedupe, send, record") is
the honest edge case here; extracting that cluster into its own service so the job class itself
stays small is usually the right call, rather than stretching the limit or the job's scope.)

**Never throw for expected failures.** Not-found, validation failure, forbidden, duplicate:
all of these are `ApiResponse.Fail(...)`, not an exception. Exceptions are for the unexpected
(database down, network timeout) and get caught by the API's exception middleware.

**Catch specific Domain exceptions and translate them**:

```csharp
try
{
    obligation.MarkCompliant(dto.CompletedDate);
    await _unitOfWork.SaveChangesAsync(ct);
}
catch (InvalidOperationException ex)
{
    // A domain guard clause fired: this is an expected business-rule rejection, not a bug.
    _logger.LogWarning("Obligation {Id} rejected: {Reason}", obligation.Id, ex.Message);
    return ApiResponse.Fail(ex.Message);
}
```

`ConcurrencyException` (thrown by `IUnitOfWork` when EF Core's `DbUpdateConcurrencyException`
fires; see "Concurrency" below) gets the same treatment, translated to a 409-style
`ApiResponse.Fail(...)`.

## Infrastructure Layer Conventions

**The `DbContext` inherits from `IdentityDbContext<TUser>`** when the project uses ASP.NET Core
Identity for accounts: `AppContext : IdentityDbContext<ApplicationUser>` in CompliSA. Watch
for a specific gotcha: `IdentityDbContext` already declares `Roles`/`UserRoles` `DbSet`
properties for *its own* `IdentityRole`/`IdentityUserRole` join system. If the project also has
its own custom role/permission system (see "Auth" below), those names collide. CompliSA hides
Identity's with `new DbSet<Role> Roles` / `new DbSet<UserRole> UserRoles`, pointing them at its
own entities instead, with a comment explaining why the `new` is intentional rather than a
mistake a future reader "fixes."

**One `IEntityTypeConfiguration<T>` per entity, applied explicitly, not data annotations.**
Explicit configuration is greppable, keeps validation attributes off the domain entity, and
puts all the mapping detail (column types, indexes, cascade behavior) in one place per entity:

```csharp
public sealed class ObligationConfiguration : IEntityTypeConfiguration<Obligation>
{
    public void Configure(EntityTypeBuilder<Obligation> builder)
    {
        builder.ToTable("Obligations");
        builder.Property(o => o.Status).HasConversion<string>().HasColumnType("nvarchar(20)");
        builder.HasIndex(o => new { o.CompanyId, o.Status });
        builder.HasIndex(o => new { o.Deadline, o.Status });
    }
}
```
(`src/complisa.infrastructure/Persistence/Configurations/ObligationConfiguration.cs`)

**Value objects convert via `HasConversion`**, so the database stores a plain validated string
while Domain keeps working with the real type:

```csharp
builder.Property(c => c.RegistrationNumber)
    .HasConversion(v => v.Value, v => RegistrationNumber.Create(v))
    .HasColumnType("nvarchar(15)");
```
(`src/complisa.infrastructure/Persistence/Configurations/CompanyConfiguration.cs`)

**Enums store as `nvarchar`, not `int`.** `.HasConversion<string>()` on every enum column: a
DBA reading `Status = 'Overdue'` directly in SQL beats `Status = 2` every time, and it survives
enum member reordering without silently corrupting existing rows.

**Strategic indexing: composite indexes matching actual query shape, not one index per
column.** `Obligations` has `(CompanyId, Status)` and `(Deadline, Status)` because those are the
literal predicates its background jobs and list queries filter on, not because "index
everything" is a rule to follow blindly.

**Repository implementations do the EF Core work** (`Include()`, pagination, `Where()`
translating Domain's query intent into actual queries) behind the interface Domain declared.
This is also where `AsNoTracking()` belongs for read-only queries, and where bulk operations
(`ExecuteDeleteAsync` for a cleanup job purging thousands of expired rows) live, since Domain's
interface only promises *what* happens, not *how*.

**External service implementations are swappable in DI**, with zero change to any code that
depends on the interface:

- `IFileStorageService` → `LocalFileStorageService` (the only implementation today; an S3/Azure
  Blob implementation is a drop-in later).
- `IEmailService` → `FileEmailService` (writes rendered HTML to `./emails/`, no real send) or
  `SmtpEmailService`, chosen by the `EmailDelivery` setting, which defaults to File in
  Development and Smtp elsewhere.
- `IeTenderClient` → `MockETenderClient` in Development, `ETenderApiClient` elsewhere.
- `ICipcClient` → `FallbackCipcClient`, a decorator over `DatanamixCipcClient` that answers from
  `MockCipcClient` (flagged as sample data) only when Datanamix is unavailable, in every
  environment.

External HTTP clients get Polly retry, timeout and a circuit breaker. Circuit breakers are
stateful, so each API's breaker is a single shared instance from a policy registry
(`AddPolicyRegistry` + `AddPolicyHandlerFromRegistry`); a breaker created inside
`AddPolicyHandler((sp, request) => ...)` is rebuilt per request and never opens.

**Hangfire setup lives here**: storage configuration (`UseSqlServerStorage`, connection string,
schema name) and the dashboard's authorization filter both belong in Infrastructure, since they
only need a connection string and don't touch Application-layer job classes. *Registering* the
actual recurring jobs is a different story; see the Dependency Rule section above for why that
call has to move to the composition root instead.

**Cascade delete rules: `NoAction` where a diamond dependency would otherwise form.** SQL
Server rejects multiple cascade paths to the same table. A self-referencing parent/child FK
(`NavigationItem.ParentId` referencing `NavigationItem.Id`) combined with another cascading FK
into the same table is exactly that shape. Use `DeleteBehavior.NoAction` there and handle
cleanup in code if needed, rather than fighting SQL Server's cascade-cycle detection.

## API Layer Conventions

**Thin controllers.** A controller's only job is: extract data from the HTTP request (route/query
params, body, JWT claims), call one service method, return the response. 5–10 lines at most:

```csharp
[HttpPost]
public async Task<IActionResult> CreateAsync(CreateCompanyDto dto, CancellationToken ct)
{
    var result = await _companyService.CreateAsync(dto, GetUserId(), ct);
    return result.Success ? Ok(result) : BadRequest(result);
}
```

No business logic, no validation, no manual mapping, no try/catch (unhandled exceptions are
`ExceptionMiddleware`'s job), no branching beyond checking `result.Success`.

**JWT auth via `[Authorize]`**, with route constraints on ID parameters:
`[HttpGet("{id:guid}")]`, not `[HttpGet("{id}")]`, so a malformed ID 404s at routing instead of
reaching the action and failing a manual parse.

**Verb mapping**: `POST` create, `GET` read, `PUT` full update, `PATCH` partial state change
(e.g. "mark compliant" is a `PATCH`, not a `PUT`, since it's not replacing the whole resource),
`DELETE` remove.

**Every response wrapped in `ApiResponse<T>`** (or the non-generic `ApiResponse`), consistently,
so a client never has to guess the response shape based on which endpoint it called.

**Swagger with JWT bearer configured**: a security scheme plus a global security requirement,
so "Authorize" in the Swagger UI actually attaches the bearer token to try-it-out requests.

**`ExceptionMiddleware` catches everything unhandled** and returns a consistent error shape;
this is the *only* place a raw, un-translated exception should ever produce a response.

**Serilog request logging middleware** covers the "a request came in, here's the status/timing"
concern globally, which is why controllers themselves log nothing (see "Logging" below).

**URL-segment API versioning.** Every controller declares `[ApiVersion("1.0")]` and
`[Route("api/v{version:apiVersion}/[controller]")]` (Asp.Versioning), so every route is
`/api/v1/...`; a breaking change adds a v2 controller alongside v1. Swagger groups by version and
shows concrete `/api/v1/` paths.

**Global input sanitisation.** An action filter (`InputSanitizationFilter` → `InputSanitizer`)
trims and strips HTML from every bound string after model binding, including init-only record
properties and string lists. It deliberately does *not* HTML-encode (that corrupts stored data and
double-encodes on display, since the Angular frontend already escapes output). Values that must
arrive byte-for-byte opt out with `[SkipSanitization]`: passwords, tokens, secrets, JSON.

**Rate limiting.** A global per-IP sliding window (100/min) plus named policies on sensitive or
public endpoints (`login` 10/min, `register` 5/hour, `auth` 5/min, `cipc-lookup` 10/min,
`tenders-public` 60/min), applied with `[EnableRateLimiting(RateLimitPolicies.…)]`; the policy
names are constants in `complisa.api/RateLimiting/RateLimitPolicies.cs`.

**CORS and security headers.** One named `CompliSA` CORS policy with explicit origins from
configuration (credentials are allowed, so no wildcards). `SecurityHeadersMiddleware` sets CSP,
`X-Content-Type-Options`, `X-Frame-Options` and `Strict-Transport-Security`.

**Health checks** at `/health`, reported per dependency as JSON: the database, the Datanamix and
eTenders circuit breakers (state only, no outbound calls, since a Datanamix enquiry is billable)
and the Hangfire server heartbeat. Only the database can make the endpoint Unhealthy; the rest
degrade.

## Cross-Cutting Concerns

### Authentication & Authorization

Two separable concerns, easy to conflate:

- **Authentication** (who is this user?): ASP.NET Core Identity (`UserManager<TUser>`,
  password hashing, lockout, token providers) plus JWT: a short-lived access token (15 min,
  held in memory/a short-lived client store, never `localStorage` for an access token) and a
  longer-lived refresh token (7 days, rotated on every use: the old token is revoked and
  `ReplacedByToken` recorded the moment a new one is issued, so a stolen-but-unused refresh
  token becomes detectable/invalidated by the rotation chain). The refresh token never appears
  in a response body: it's an httpOnly, Secure, SameSite=Strict cookie scoped to `/api/v1/auth`,
  so page scripts (and any XSS) can't read it. Because the browser sends that cookie
  automatically, the endpoints that use it (`refresh-token`, `logout`) require a CSRF token,
  double-submit: `GET /auth/csrf-token` sets the antiforgery cookie plus a readable
  `XSRF-TOKEN` cookie, which the client echoes in the `X-XSRF-TOKEN` header (Angular's
  `HttpClient` does this automatically for same-origin requests).
- **Authorization** (what can this user do?): a custom `Role → RolePermission → Permission →
  NavigationItemPermission` graph is the sole source of truth in CompliSA, *not* Identity's own
  `IdentityRole`. If a project registers `AddIdentity<TUser, IdentityRole>(...)` purely because
  the generic parameter is required, and never actually assigns or checks `IdentityRole`
  anywhere. Say so explicitly in a comment where it's registered. Future readers will
  otherwise assume it's live and go looking for where roles get assigned.
- **Platform administration is a policy, checked against the database.** Endpoints for the whole
  platform (tenants, global roles/permissions/navigation, users, system settings, jobs, the
  all-tenant audit log) carry `[Authorize(Policy = AuthorizationPolicies.SystemAdmin)]`. Its
  handler asks `IAdminAccessService` whether the caller holds the seeded `SystemAdmin` role,
  per request, not from a JWT claim, so revoking the role takes effect immediately rather than
  when the 15-minute token expires. A bare `[Authorize]` on an admin controller is a bug: it
  means "any self-registered account".
- **Company administration is *not* a policy**, because the answer depends on the `companyId`
  in the route. Tenant services check `ICompanyAccessService.HasMinimumRoleAsync(companyId,
  userId, CompanyRole.Admin)` themselves (see "Multi-Tenancy Pattern").

**Multi-tenancy** sits on top of both: a join entity (`CompanyUser`, user ↔ company ↔ role)
plus a single access-check service (`ICompanyAccessService`) that every tenant-scoped operation
calls before touching that tenant's data; see "Multi-Tenancy Pattern" below.

### Audit Trail

**Append-only.** No update or delete endpoint, no EF Core `Update`/`Remove` configuration
against the audit table, ever. An audit trail that can be edited isn't one.

**An EF Core `SaveChangesInterceptor` captures changes automatically**, in two phases:

1. `SavingChangesAsync`: walk `ChangeTracker.Entries()` for any `IAuditable` entity, diff
   Modified entries' before/after property values, serialize the diff as JSON.
2. `SavedChangesAsync`: persist the audit rows *after* the save, so database-generated keys
   (identity columns) are already populated and can be recorded correctly.

Store the diff as JSON in a single `Changes` column rather than one row per changed property:
cheaper to write, and a UI can render "3 fields changed" without a join.

**A separate `IAuditLogger` for explicit action logging**: things that aren't an entity state
change at all (login attempts, password resets, role assignments) but are still audit-worthy.
Called directly from services (`_auditLogger.LogActionAsync(AuditAction.Login, ...)`), distinct
from the automatic interceptor above.

**Correlate with request logs.** A `TraceId` column on the audit row, populated from the
current request's Serilog/`Activity` trace ID, lets you go from "this audit row" to "the full
request log for that action" in one lookup.

**Scope rows to a tenant at write time.** A shared audit table can only be shown to a tenant's
admins if every row says which tenant it belongs to. Entities owned by one company implement
`ICompanyScoped` (`Obligation`, `Document`, `CompanyUser`, `CompanyInvitation`,
`CompanyDocumentSettings`), and the interceptor stamps `AuditLog.CompanyId` from it (or from
`Company.Id` itself); platform-level rows (logins, global roles, tenders) leave it null. Deriving
the tenant later by joining on `EntityId` doesn't survive deletes or entity types added later.
Composite keys are recorded as `part1:part2` (e.g. `companyId:userId` for a membership).

**Exports are data leaving the system.** The company audit CSV export caps rows (10,000, newest
first), and prefixes any cell starting with `=`, `+`, `-`, `@`, tab or CR with an apostrophe;
audit rows contain user-typed text, and a cell like `=HYPERLINK(...)` would otherwise run as a
formula when an auditor opens the file in Excel.

### Concurrency

**Optimistic, via `RowVersion`.** `IConcurrencyAware` entities get a `byte[] RowVersion`
mapped as a SQL Server `ROWVERSION` column, not by manually configuring it on each entity, but
by a one-time reflection loop over the EF model in `OnModelCreating`:

```csharp
foreach (var entityType in modelBuilder.Model.GetEntityTypes())
{
    if (typeof(IConcurrencyAware).IsAssignableFrom(entityType.ClrType))
        modelBuilder.Entity(entityType.ClrType).Property(nameof(IConcurrencyAware.RowVersion)).IsRowVersion();
}
```

**`IUnitOfWork` translates the EF exception to a domain one**, so nothing above Infrastructure
needs to know EF Core exists:

```csharp
catch (DbUpdateConcurrencyException ex)
{
    throw new ConcurrencyException(entityName, entityId);
}
```

The client sends the `RowVersion` it last read back on every update; if it's stale, the update
fails with a translated `ConcurrencyException` → `ApiResponse.Fail(...)` conflict response,
instead of silently overwriting someone else's concurrent change.

### Soft Delete

Obligations and documents are never physically deleted. The mechanism is convention-based, the
same shape as concurrency:

- **`ISoftDeletable` in Domain**: `IsDeleted`, `DeletedAt`, `DeletedByUserId`, and an idempotent
  `MarkDeleted(userId, now)`.
- **A global query filter per implementer**, added by a reflection loop in `OnModelCreating`, so
  every query hides deleted rows without any repository remembering to. A dependent with a
  required navigation to a filtered entity needs a matching filter of its own:
  `ObligationDocument` filters on both ends, so links to a deleted obligation or document
  disappear with it.
- **`SoftDeleteInterceptor`** (a `SaveChangesInterceptor`) turns `EntityState.Deleted` into
  `Modified` + `MarkDeleted(...)`, so services keep calling `repository.Delete(entity)` as usual.
  It's registered *after* the audit interceptor, so the audit trail still records the operation
  as a Delete.
- **`IncludingDeleted()` / `OnlyDeleted()`** query extensions for admin views. They lift *all*
  global filters (EF Core ignores filters per query, not per filter), so tenant scoping has to be
  re-applied explicitly.

Deleting a document keeps its stored file, so the document can be restored; purging old files is
a separate retention concern.

### UTC Everywhere

`datetime2` stores no offset, so EF Core materialises values as `DateTimeKind.Unspecified` and
the API would serialise them without a trailing `Z`, which browsers then read as *local* time.
A convention in `AppContext` gives every `DateTime`/`DateTime?` property a value converter:
UTC-stamped on read, normalised to UTC on write. Combined with `IClock` (see "Domain Layer
Conventions"), every time in the system is UTC until the Angular UI formats it for display.

### File Upload Validation

Content type and file extension both come from the client, so neither proves anything.
`UploadedFileValidator` (FluentValidation, run by `DocumentService` after the access check and
before anything is stored) checks size, an allowed extension (configurable), a content type
consistent with that extension, and (via `FileSignatureValidator`) the file's actual leading
bytes. DOCX/XLSX are ZIP containers, so for those the archive must also contain the part that
makes it a Word document or workbook, not just any ZIP.

### Logging

Serilog, structured always: `{CompanyId}`, `{UserId}` as named properties, never string
interpolation (`$"Company {id}"` defeats structured querying and is the one thing to catch in
review every time it slips back in). Level and *where* logging happens is layer-specific, by
design, not by accident:

| Layer | What gets logged | Level |
|---|---|---|
| Controllers | Nothing; request middleware covers HTTP-level logging | None |
| Services | Business decisions and outcomes (not method entry/exit) | `Information` state changes, `Warning` expected failures, `Error` unexpected failures |
| Infrastructure | Query-level detail, failures | `Debug` / `Error` |
| Domain | Nothing; entities throw, callers decide how (or whether) to log | None |

### Background Jobs

Hangfire with SQL Server storage (its own schema, auto-created, with no coupling to the app's own
EF Core migrations/schema management). The shape that keeps this consistent with the
Dependency Rule:

- **Job classes are Application-layer**, one `ExecuteAsync(CancellationToken)` method each,
  constructor-injected with the same repository interfaces any other service would use.
- **Storage/server setup is Infrastructure-layer** (connection string, schema name, dashboard
  auth filter); it doesn't need to know the job classes exist.
- **Recurring-job *registration*** happens in the **composition root** (API), because it's the
  only layer allowed to reference both the Application-layer job classes and Infrastructure's
  Hangfire configuration in the same place. The jobs themselves (id, class, display name and
  default cron, in run order) are listed once in `RecurringJobCatalog` (Application);
  `RecurringJobScheduler` (API) registers them, applying any schedule a SystemAdmin saved
  (`SystemSettings` key `Jobs:{id}:Cron`) and falling back to the default if that override no
  longer parses or the database isn't reachable at startup.
- **Monitoring goes through a Domain abstraction.** `IBackgroundJobMonitor` (Domain) exposes
  recurring-job status, manual triggers, rescheduling and the failed-job queue; `HangfireJobMonitor`
  (Infrastructure) implements it over Hangfire's storage and client APIs. Application's
  `JobMonitoringService` adds the catalog's names and descriptions, so no Hangfire types leak above
  Infrastructure.

**Idempotency is non-negotiable.** Every job must produce the same result whether it runs once
or is accidentally triggered twice in a row (a retry, a manual dashboard trigger, a clock skew
between server instances). Two techniques cover most cases:

- A *recalculation* job is naturally idempotent if its output is a pure function of current
  state (today's date vs. a stored deadline); running it twice just recomputes the same answer.
- A *notification* job needs an explicit dedup record: an entity like `SentAlert` (one row per
  entity + recipient + alert type + threshold actually sent) checked *before* every send, so a
  second run sees "already sent" and skips rather than emailing someone twice.

**Ordering matters when jobs depend on each other's output.** A status-recalculation job must
run (and finish) before an alert job that reads those statuses, or the alert job sends
alerts based on yesterday's stale status. Schedule them with enough of a gap (e.g. 30 minutes
apart) that one reliably completes before the next starts, rather than relying on both finishing
"fast enough."

**A failed item shouldn't fail the batch.** One obligation with bad data, or one recipient whose
email send throws, should be caught, logged, and skipped, not let an exception abort the loop
and leave the other 500 obligations unprocessed:

```csharp
foreach (var recipient in recipients)
{
    try { await SendAsync(recipient); }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to send alert to {UserId}", recipient.Id);
        continue;   // one bad recipient doesn't stop the rest
    }
}
```

**Batch the database work in high-volume jobs.** The tender jobs touch hundreds to thousands of
rows per run, so they follow a stricter shape than the per-obligation jobs:

- **Save every N changes, not per item and not once at the end.** Per-item saves are one round
  trip per row; a single save at the end holds every change in the change tracker and loses the
  whole run if anything fails late. `TenderIngestionJob`, `TenderMatchingJob` and
  `TenderReadinessJob` save every 100 changes, plus a final save for the remainder.
- **Look up in batches, too.** Ingestion processes releases in `Chunk(100)` batches with one
  `GetByOcidsAsync(ocids)` query per chunk (an `IN (...)` over the batch) rather than one
  lookup per release: the same N+1 problem as per-item saves, on the read side.
  Matching loads every existing match for active tenders once into a dictionary keyed by
  `(CompanyId, TenderId)`; readiness loads every relevant company's documents once and groups
  them with `ToLookup`.
- **Load tracked, mutate through entity methods, let EF detect changes.** Job-only repository
  methods skip `AsNoTracking`, so the job just calls `tender.UpdateFromSource(...)` or
  `match.UpdateScore(...)` and the next batch save picks up whatever changed, with no `Update()`
  calls marking every column dirty.
- **"No change" must write nothing.** Entity methods return `bool changed` and only touch
  `UpdatedAt` on a real change. The ingestion window deliberately overlaps previous runs (7 days
  re-fetched daily), so without this every unchanged tender would be rewritten (and audited)
  every night. Idempotency here means "a second run is a no-op", not just "a second run gives the
  same end state".

```csharp
foreach (var batch in releases.Chunk(BatchSize))
{
    var existing = (await _tenderRepository.GetByOcidsAsync(batch.Select(r => r.Ocid).ToList(), ct))
        .ToDictionary(t => t.Ocid);
    var newTenders = new List<Tender>();

    foreach (var release in batch)
    {
        try
        {
            if (existing.TryGetValue(release.Ocid, out var tender)) tender.UpdateFromSource(release);
            else newTenders.Add(Tender.Create(release));
        }
        catch (Exception ex) { /* log and skip this release; the rest of the batch continues */ }
    }

    await _tenderRepository.AddRangeAsync(newTenders, ct);
    await _unitOfWork.SaveChangesAsync(ct);   // one round trip per 100 releases
}
```

**Log one summary line per run**, not one line per item: `"Recalculated {Total} obligations,
{Changed} status changes"`. A daily job that logs 10,000 lines a day defeats the purpose of
logging.

**Cleanup jobs run less often** (weekly/monthly, not daily) and purge data whose only purpose
was short-term bookkeeping (expired refresh tokens, old dedup records), so a table that exists
purely to prevent duplicate sends doesn't itself grow unbounded forever.

### Email

Layered like everything else: `IEmailService` (the raw "send this message" contract) in
Domain, `IEmailTemplateService` (builds a branded HTML email from a typed DTO, calls
`IEmailService`) in Application, concrete senders in Infrastructure:
`FileEmailService`/`SmtpEmailService`, chosen by the `EmailDelivery` setting (see "Infrastructure
Layer Conventions"). SMTP sends retry transient failures (4xx replies, dropped connections) with
backoff and fail fast on permanent ones.

**Development writes to disk, not a real inbox.** `FileEmailService` renders the email to
`./emails/{timestamp}_{recipient}.html`. Open it in a browser to see exactly what would have
been sent, with zero SMTP setup needed to develop or test the feature.

**A failed send is never allowed to fail the operation that triggered it.** Registering a user
whose welcome email fails to send should still succeed; the failure gets logged at `Error` and
swallowed at the boundary, not propagated up as an exception that rolls back an otherwise-valid
operation.

**Templates stay inline in the template service until there's a real reason not to.** Don't
reach for Razor views or external template files the moment you have two emails; the
complexity is worth it once the number of templates makes inline HTML genuinely hard to
maintain (a rough threshold: once you're past about 10), not before.

**Previews render the real templates.** `EmailTemplatePreviewService` runs `EmailTemplateService`
against a capturing `IEmailService` with sample DTOs, so the System settings preview can never
drift from what's actually sent, and no template markup is duplicated.

**Single-use links carry a secret only in the email.** Invitations store a SHA-256 of a 256-bit
random token (`SecureTokens`); the raw token exists only in the recipient's inbox and travels back
in a POST body (never a query string, so it stays out of request logs). Possession of the link
isn't enough: accepting also requires the signed-in account's email to match the invited address.

### Multi-Tenancy Pattern

Shared database, shared schema, row-level isolation, not a database-per-tenant or
schema-per-tenant split. The mechanism:

- A join entity (`CompanyUser`: `CompanyId`, `UserId`, `CompanyRole`) links users to the
  tenants (companies) they belong to, with a role scoped to *that* tenant.
- **Every tenant-scoped query filters through that join**: a repository method fetching a
  user's obligations joins through `CompanyUser` to scope results to companies they actually
  belong to, never trusts a `companyId` parameter alone.
- **One service centralizes the access check** (`ICompanyAccessService`) so "does this user
  have access to this company, and at what role level" is answered in exactly one place,
  called before every tenant-scoped write or sensitive read, not reimplemented per service
  with subtly different logic each time.
- **System-level admin roles stay separate from tenant roles.** "Can manage this specific
  company" (`CompanyRole.Owner`/`Admin`/`Contributor`/`Viewer`) and "can administer the whole system"
  (the `SystemAdmin` role, unrelated to any one company) are different questions. Don't let a
  tenant-scoped `Owner` role accidentally imply system-wide access anywhere in the permission
  graph. In CompliSA the two admin surfaces are separate modules: `/admin` (company-scoped, via
  `ICompanyAccessService`) and `/system-admin` (the `SystemAdmin` policy, deliberately *not*
  tenant-scoped: `ITenantRepository` queries across companies and must never be reachable by a
  company-level caller).
- **Membership removal is a soft delete.** `CompanyUser` is `ISoftDeletable`, so removing a
  member flips a flag and the global query filter immediately drops them from every access check,
  while the history survives; re-inviting the same person restores the same row. Rules the
  service enforces: only an Owner grants, changes or removes the Owner role; nobody changes or
  removes themselves; a company always keeps at least one Owner.

### State Machine Pattern

Any entity whose lifecycle is a finite set of states with real rules about which transitions
are valid (`Obligation`, `Document` here) follows the same shape:

- **`Status` has a private setter**: the only way to change it is through a named method
  (`MarkCompliant()`, `MarkExpired()`), never `entity.Status = x` from outside.
- **Guard clauses reject invalid transitions** by throwing `InvalidOperationException`, caught
  and translated by the calling service (see "Application Layer Conventions" above); the
  entity itself doesn't know or care about HTTP status codes.
- **Two ways a transition happens**: event-driven (a user action calls `MarkCompliant()`
  directly) and time-driven (a nightly background job calls `RecalculateStatus()`, which is a
  pure function of "how much time is left until the deadline": deterministic, so re-running it
  is always safe).
- **Status is a persisted, indexed column, not derived on every read.** Recalculating "is this
  overdue" from a raw deadline comparison on every list-page load doesn't scale and can't be
  indexed; storing the computed status and refreshing it on a schedule (or on write) does both.

## Testing Strategy

- **Domain tests**: entity behavior, value object validation, state transitions, with no mocks
  needed at all, since Domain has no dependencies. `RegistrationNumber.Create("bad-format")`
  should throw; `obligation.MarkCompliant(date)` twice should throw the second time.
- **Application tests**: service orchestration, access control, validation wiring, error
  translation. Mock every repository interface (Moq), Arrange-Act-Assert, one test class per
  service, methods named `MethodName_Scenario_ExpectedResult`
  (`TryDispatchObligationDeadlineAlertAsync_WhenAlertAlreadySentToAllRecipients_SkipsSendingAndReturnsFalse`,
  from `tests/complisa.application.tests/Services/AlertDispatchServiceTests.cs`).
- **Don't test controllers**: if a controller genuinely stays 3–5 lines with no branching, an
  integration/end-to-end test covering the full request pipeline is a better use of the effort
  than a unit test asserting a mock got called once.
- **Don't unit-test repositories**: they're thin EF Core query wrappers; correctness there is
  an integration-test concern (a real or in-memory database), not something Moq can meaningfully
  verify.
- **Don't test thin CRUD wrappers at all**: a service method that's purely "validate, map,
  save" with no branching adds a test that breaks on every unrelated refactor and catches
  nothing real. Spend the test budget on business logic, branching, and failure paths instead.

## Frontend Architecture (Angular)

- **Standalone components, no `NgModule` anywhere.** Bootstrap via `bootstrapApplication` with
  an `ApplicationConfig` (`provideRouter`, `provideHttpClient`, etc.), not an `AppModule`.
- **Three top-level folders under `src/app/`**:
  - `core/` (`guards/`, `interceptors/`, `models/`, `services/`): singleton, app-wide concerns.
  - `features/`: one folder per feature (`auth/`, `companies/`, `obligations/`, `documents/`,
    `dashboard/`, `company-admin/`, `system-admin/`, `invitations/`, `cipc-check/`, `tenders/`,
    `tender-matches/`, `shell/`): everything that feature needs, colocated.
  - `shared/` (`components/`, `directives/`, `pipes/`, `styles/`, `utils/`): genuinely reusable
    across features, with no feature-specific knowledge baked in.
- **Reactive forms for all data entry**: not template-driven forms, for the same reason
  FluentValidation lives server-side: declarative, testable validation rules in one place.
- **Functional interceptors and guards** (Angular's modern pattern, not the older class-based
  `HttpInterceptor`/`CanActivate` implementations):
  ```ts
  export const authInterceptor: HttpInterceptorFn = (req, next) => { /* ... */ };
  export const authGuard: CanActivateFn = (route, state) => { /* ... */ };
  ```
  registered via `provideHttpClient(withInterceptors([errorInterceptor, authInterceptor]),
  withXsrfConfiguration(...))`. Order matters: `errorInterceptor` is outermost, so it only sees a
  response after `authInterceptor` has tried its 401 refresh-and-retry. The auth interceptor owns
  401s; the error interceptor toasts 403, 5xx and network failures; components handle 4xx
  business errors themselves.
- **`BehaviorSubject` for reactive state**, exposed as a read-only observable: a service holds
  the subject privately and only publishes `.asObservable()`:
  ```ts
  private readonly isAuthenticatedSubject = new BehaviorSubject<boolean>(false);
  readonly isAuthenticated$ = this.isAuthenticatedSubject.asObservable();
  ```
  Components/guards subscribe reactively rather than polling a getter.
- **`provideAppInitializer` for session restore on page reload**: runs before the app renders:
  fetches a CSRF token, then asks the API to refresh from the httpOnly refresh-token cookie
  (the app never stores a token itself; the access token lives in memory only), re-establishing
  the session and anything that depends on it, like the permission-scoped navigation menu:
  ```ts
  provideAppInitializer(() => {
    const authService = inject(AuthService);
    return authService.tryRestoreSession();
  })
  ```
- **Angular Material with a custom theme, CSS custom properties for design tokens**: `:root`
  variables in a global stylesheet (`--color-accent`, `--color-accent-green`, ...) layered with
  Material's own theming API, so component styles reference the variables rather than hardcoded
  hex values anywhere.
- **Dynamic navigation from the backend, scoped to the user's actual permissions**: the nav
  menu isn't a static route list in the frontend; it's fetched per-user (`GET /api/v1/me/permissions`,
  then `GET /api/v1/navigation/menu?permissionIds=...`) so a user without a permission never even
  sees the nav item, rather than seeing it and hitting a 403 on click. Company-admin items
  (`/admin/*`) carry no permission (company admin is per company, not a platform role), so the
  client hides them unless `GET /api/v1/me/admin-access` lists at least one company the user
  administers. Hiding is cosmetic; every admin endpoint re-checks on the server.
- **Admin areas are lazy-loaded route modules behind functional guards**: `loadChildren` for
  `/admin` (`COMPANY_ADMIN_ROUTES`, `companyAdminGuard`) and `/system-admin`
  (`SYSTEM_ADMIN_ROUTES`, `systemAdminGuard`), each page a `loadComponent` route, so regular users
  never download admin code. Both guards read `AdminAccessService`, which caches
  `/me/admin-access` for the session (reset on sign-in/out) and remembers the company chosen in
  the company-admin picker.

## Design Patterns Used

| Pattern | Where |
|---|---|
| **Factory Method** | Entity creation: `Obligation.Create(...)`, private constructor, no way to construct an invalid instance |
| **Observer** | Angular `BehaviorSubject` state (`AuthService.isAuthenticated$`), components subscribe rather than poll |
| **Strategy** | `IEmailService` → `FileEmailService`/`SmtpEmailService`, swapped by configuration with zero change to any caller |
| **Decorator** | `FallbackCipcClient` wraps the Datanamix client and falls back to the mock (flagged as sample data) only when Datanamix is unavailable |
| **Middleware Pipeline** | Both directions: .NET's `ExceptionMiddleware`/Serilog request logging, and Angular's functional HTTP interceptors |
| **Repository** | One interface per aggregate (`IObligationRepository`), implemented in Infrastructure, never generic |
| **Unit of Work** | `IUnitOfWork.SaveChangesAsync()`: one transaction boundary, however many repositories were touched |
| **State Machine** | Private-setter `Status`, guarded transition methods (`Obligation.MarkCompliant()`) |
| **Marker Interface** | `IAuditable`, `IConcurrencyAware`: behavior attached by interface, picked up by convention-based reflection in Infrastructure, not per-entity boilerplate |
| **Value Object** | `RegistrationNumber`, `TaxReference`, `TenderMatchScore`: validation rules encoded in the type, not re-checked at every call site |
| **Adapter** | `ETenderApiClient` / `CipcApiClient`: translate an external API's shape into a Domain model (`OcdsRelease`, `CipcCompanyData`) behind a Domain interface, so nothing past Infrastructure knows the wire format |
| **ETL Pipeline** | Tender ingestion → matching → readiness, three idempotent Hangfire jobs |
| **Circuit Breaker / Retry** | Polly policies on the eTenders and CIPC `HttpClient`s, with shared breaker instances from a policy registry |
