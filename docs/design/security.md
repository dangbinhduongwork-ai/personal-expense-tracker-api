# Security Design

## 1. Assets and threats

| Asset | Main threats | Controls (section) |
|---|---|---|
| Financial data of each user | Another user reads/changes it (BOLA); SQL injection; mass assignment | §3, §4, §6 |
| Credentials (password hashes, tokens) | DB leak, brute force, token theft/replay | §2, §5 |
| Secrets (JWT key, DB password) | Committed to Git, logged, baked into Docker images | §7 |
| Service availability | Unbounded queries, login flooding | §8 |
| Internal details | Stack traces, SQL errors, versions in responses | §9 |

## 2. Authentication

| Item | Decision |
|---|---|
| Access token | JWT, **HS256**, lifetime **15 min** (`Jwt:AccessTokenMinutes`). Claims: `sub` (user id), `email`, `jti`, `iat`, `exp`. No password, no roles needed in MVP. |
| Signing key | `Jwt:Key`: random bytes stored as **Base64 text**; the **decoded** length (`Convert.FromBase64String`) must be **≥ 32 bytes** in both startup validation (`ValidateOnStart()`) and signing/validation configuration (the app **refuses to start** otherwise). Dev: `dotnet user-secrets`. Docker/prod: env var `Jwt__Key`. Never in `appsettings*.json` committed to Git. Generate with PowerShell 7: `[Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(48))` |
| Validation (JwtBearer) | `ValidateIssuer`, `ValidateAudience`, `ValidateLifetime`, `ValidateIssuerSigningKey` all `true`; `ClockSkew = TimeSpan.FromSeconds(30)`; `MapInboundClaims = false` so the `sub` claim keeps its name; `ValidAlgorithms = [SecurityAlgorithms.HmacSha256]` (reject `none`, `HS512`, and other algs); `JwtBearerOptions.TimeProvider` bound from DI `TimeProvider`. `OnChallenge` returns `401` (`code: "unauthorized"`); `OnForbidden` returns `403` (`code: "forbidden"`). |
| Refresh token | 32 random bytes (`RandomNumberGenerator`) → Base64Url, returned **once**. DB stores **SHA-256 hex** only. Lifetime **7 days**. **Rotation** on every refresh using a transaction / active-row check (`RevokedAt == null`) to prevent parallel double-issue; **reuse detection** (revoked token presented ⇒ revoke **all** active tokens of that user and commit via `SaveChangesAsync` before returning `401`). Revoked on logout and on password change. |
| Transport of refresh token | JSON body (simple, Swagger-friendly) — documented trade-off: a browser SPA would prefer an HttpOnly cookie; out of scope. |
| Password hashing | `PasswordHasher<User>` (PBKDF2, per-hash salt, versioned format). `VerifyHashedPassword` result `SuccessRehashNeeded` ⇒ re-hash and save. For an unknown email, **MUST** run `VerifyHashedPassword` against a valid pre-generated PBKDF2 dummy hash (`PasswordHasher<User>`) so response timing does not reveal email existence. Never trim passwords. |
| Login errors | Always `401 invalid_credentials`, same message and response body shape. |
| Brute force | Fixed-window rate limiter (`RemoteIpAddress` key, ignoring untrusted `X-Forwarded-For`) on `register`, `login`, `refresh-token`, `logout`, and `change-password` (`RejectionStatusCode = 429` + `Retry-After` header + `ProblemDetails` `code: "rate_limited"`). No account lock-out in v1 (documented residual risk). |

## 3. Authorization and data ownership (the most important section)

1. **Identity comes from the token only.** `ICurrentUserService.UserId` parses the `sub` claim and requires `int.TryParse(sub, out var id) && id > 0` (else throws `UnauthorizedException` `401`). No request DTO has `UserId`. Any `userId`/`isSystem`/`email` in a body/query is ignored (those fields do not exist on DTOs).
2. **Every query is scoped.** Transactions: `db.Transactions.Where(t => t.UserId == userId)`. Categories: `Where(c => c.UserId == null || c.UserId == userId)` for reads; writes load `Where(c => c.Id == id && (c.UserId == null || c.UserId == userId))` (`null` → `404`, `c.UserId == null` → `403 system_category_readonly`) and usage checks filter `t.UserId == userId && t.CategoryId == id`. RefreshTokens (`logout`): strictly scope `Where(rt => rt.TokenHash == hash && rt.UserId == userId)` (`null` / already revoked → `204` no-op; never revoke another user's token). Statistics: `t.UserId == userId` as the *first* clause of every query.
3. **Foreign = missing.** Loading a user-owned entity by id uses one query `FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId)`; `null` ⇒ `NotFoundException` (404). Never load by id first and compare owner afterwards (leaks existence, easy to forget).
4. **Related ids are checked too.** Creating or updating a transaction (`POST` and `PUT /api/transactions/{id}`) validates `CategoryId` with the rule "system OR owned by the caller" (`400 category_not_found` if missing/foreign).
5. **No EF global query filter** in MVP (explicit filters are easier to read and review). Compensation: integration tests `SEC-06`…`SEC-14` on every endpoint.
6. **Function-level rule:** system categories (`UserId == null`) are read-only (`403 system_category_readonly`), enforced before duplicate/in-use `409` checks.
7. `[Authorize]` on all controllers except `AuthController` anonymous actions and `GET /health` (both marked explicitly with `[AllowAnonymous]`). Configure `AuthorizationOptions.FallbackPolicy` = `RequireAuthenticatedUser` so any new route is protected by default, and gate **both** `MapOpenApi()` (`/openapi/v1.json`) and `UseSwaggerUI()` (`/swagger`) behind `OpenApi:EnableUi == true` (returning `404` when `false`).

## 4. Input validation and injection

* DataAnnotations on every request DTO (`[StringLength]` upper bounds on all auth/category/transaction fields including `LoginRequest` / `ChangePasswordRequest` password ≤ 100 and `RefreshTokenRequest` token ≤ 256; `[Range]`, `[EnumDataType]` on nullable value types), plus strict string enum query binding and `[ApiController]` auto-400 (`code: "validation_failed"`).
* Business validation in services (BR-xx), throwing `ValidationFailedException` / `BusinessRuleException`.
* **SQL injection:** only EF Core LINQ / parameterised queries. **Forbidden:** string-concatenated SQL, `FromSqlRaw`/`ExecuteSqlRaw` with interpolated user input. If raw SQL is ever needed, use `FromSql`/`FromSqlInterpolated` with parameters and justify in an ADR.
* Sort fields come from a **whitelist** (`date|amount|title`, `asc|desc`; invalid ⇒ `400 validation_failed`), never from raw strings into `OrderBy` text.
* `search` ≤ 100 chars; use parameterized EF `Contains` (**never** unescaped `EF.Functions.Like`) so SQL wildcards `%`, `_`, `[` match literally and do not trigger pattern-matching abuse.
* Request size: keep Kestrel default body limit; no file uploads.

## 5. Sensitive data

* Never return `PasswordHash`, `TokenHash`, internal ids of tokens. DTOs are explicit allow-lists (no entity serialization).
* Add `Cache-Control: no-store` header on `/api/auth/login` and `/api/auth/refresh-token` responses.
* Never log passwords, tokens, `Authorization` headers, connection strings, or full request bodies. Forbid `EnableSensitiveDataLogging()` in all non-test configs and forbid `IncludeErrorDetails = true` outside Development.
* Log user id and ids of touched entities for traceability.

## 6. Mass assignment / object-property level authorization

Request DTOs contain only fields the client may set. Entities are created via explicit mapping (`new Transaction { UserId = currentUserId, … }`), never by binding JSON to an entity.

## 7. Secrets and configuration

| Rule | Detail |
|---|---|
| Dev | `dotnet user-secrets init/set` in `ExpenseTracker.Api` for `Jwt:Key`, `ConnectionStrings:DefaultConnection` (if it has a password). `Seed:DemoData=true` is allowed **only** when `env.IsDevelopment()`. |
| Docker/prod | Environment variables or a `.env` file that is **git-ignored**. Commit only `.env.example` with fake values. |
| Git | `.gitignore` covers `.env`, `*.user`, `appsettings.*.local.json`, `bin/`, `obj/`, `secrets.json`. If a secret was ever committed, rotate it immediately; deleting a commit is not sufficient. |
| Images | Secrets are **not** baked into the Docker image (no `ENV Jwt__Key=` in the Dockerfile). |
| Startup validation | Options validated with `ValidateOnStart`; missing/short decoded `Jwt:Key` (< 32 bytes) ⇒ fail fast. |

## 8. Availability and abuse

* Paging limits (`pageSize ≤ 100`), `daily` range ≤ 366 days, `yearly` span ≤ 20, `top-categories` limit ≤ 20, `search` ≤ 100.
* Fixed-window rate limiter (`RemoteIpAddress` key) on auth endpoints (`register`, `login`, `refresh-token`, `logout`, `change-password`) → `429` (`code: "rate_limited"`).
* `CancellationToken` flows from the request into EF calls.

## 9. Transport, CORS, errors, exposure

* Conditionally enable `UseHttpsRedirection` and `UseHsts` outside Development when `HttpsRedirection:Enabled` is `true` (set `HttpsRedirection:Enabled=false` in local HTTP Docker demo and `ApiFactory`).
* **CORS:** named policy from `Cors:AllowedOrigins`; empty list ⇒ no cross-origin access. `"*"` / `AllowAnyOrigin` and `AllowCredentials` are strictly forbidden in non-Development configuration.
* **Errors & Health:** production-like environments never return stack traces/exception messages; 500 body is generic `ProblemDetails` (`code: "internal_error"`) with `traceId`. `GET /health` returns only `200 Healthy` / `503 Unhealthy` without connection strings or SQL exceptions.
* Swagger UI (`/swagger`) and OpenAPI JSON (`/openapi/v1.json`) only when `OpenApi:EnableUi=true` (default true in Development). Document that enabling it in production is a conscious choice.
* Add `X-Content-Type-Options: nosniff` on all HTTP responses via middleware (**required**).

## 10. Dependencies

`dotnet list package --vulnerable --include-transitive` runs in CI; fix or justify every finding. Central package management (`Directory.Packages.props`) keeps versions explicit and auditable.

## 11. OWASP API Security Top 10 (2023) mapping

| OWASP | Risk | Our mitigation |
|---|---|---|
| API1 | Broken Object Level Authorization | §3 (scoped queries, 404 for foreign) + SEC-06…SEC-14 |
| API2 | Broken Authentication | §2 (JWT validation, hashing, refresh rotation, rate limit) |
| API3 | Broken Object Property Level Authorization | §6 (DTO allow-lists, no entity binding) |
| API4 | Unrestricted Resource Consumption | §8 (paging caps, range caps, rate limit) |
| API5 | Broken Function Level Authorization | system categories read-only; fallback `[Authorize]` |
| API6 | Unrestricted Access to Sensitive Business Flows | rate limit (`RemoteIpAddress`) on register/login/refresh/logout/change-password |
| API7 | SSRF | Not applicable (no outbound calls from user input) |
| API8 | Security Misconfiguration | §7, §9 (secrets, CORS, errors, `OpenApi:EnableUi`, `HttpsRedirection:Enabled`, `nosniff`) |
| API9 | Improper Inventory Management | single versionless API; `OpenApi:EnableUi` gates Swagger & OpenAPI JSON; no hidden endpoints |
| API10 | Unsafe Consumption of APIs | Not applicable |

## 12. Security test catalogue (implemented in `tests/ExpenseTracker.IntegrationTests/Security/`)

| ID | Test |
|---|---|
| SEC-01 | Every protected endpoint without a token → 401 `code: "unauthorized"` (data-driven over all routes; `/health` exempt) |
| SEC-02 | Token expired > 30s (e.g. 45s past `exp`) → 401 (`ClockSkew = 30s` enforced) |
| SEC-03 | Token with tampered signature → 401 |
| SEC-04 | Token with wrong issuer / wrong audience → 401 |
| SEC-05 | Token using `alg=none` OR different algorithm (e.g. `HS512` signed with valid key) → 401 |
| SEC-06 | User A `GET /transactions/{B's id}` → 404 |
| SEC-07 | User A `PUT` B's transaction → 404 and B's row unchanged |
| SEC-08 | User A `DELETE` B's transaction → 404 and B's row still exists |
| SEC-09 | User A transaction list (`with filters/search`) and category list never contain B's rows |
| SEC-10 | A creates (`POST`) or updates (`PUT /api/transactions/{id}`) a transaction with B's personal `categoryId` → 400 `category_not_found` |
| SEC-11 | A sends extra fields (`userId = B`, `isSystem = true`, `email`) on transaction/category POST & PUT and profile PUT → ignored; created/updated rows remain caller-owned and non-system |
| SEC-12 | A `GET/PUT/DELETE` B's personal category → 404 |
| SEC-13 | A `PUT/DELETE` a system category → 403 |
| SEC-14 | A's statistics (all 7 endpoints) exclude B's rows (assert exact numbers) |
| SEC-15 | Responses never contain `passwordHash`/`tokenHash`; stored hash ≠ password |
| SEC-16 | Re-using a rotated refresh token → 401 and **all** active refresh tokens of that user (including sibling sessions) become revoked |
| SEC-17 | After logout own refresh token returns `401`, AND User A submitting User B's refresh token to `logout` returns `204` without revoking User B's token |
| SEC-18 | After change-password all refresh tokens are revoked; old password no longer logs in |
| SEC-19 | Login with unknown email and with wrong password return identical status + `ProblemDetails` body shape (unit test asserts dummy PBKDF2 `VerifyHashedPassword` call) |
| SEC-20 | `search` containing `' OR 1=1 --`, `%`, `_`, `[` returns only literal matching **own** rows (never wildcard-all or 500) |
| SEC-21 | `pageSize=100000` → 400 `validation_failed`, `page=0` → 400 `validation_failed`, plus `daily > 366`, `yearly > 20`, `limit > 20`, `search > 100` caps enforced |
| SEC-22 | Forced exception in a non-Development environment → 500 `ProblemDetails` (`code: "internal_error"`, `traceId` present, no stack trace / exception text) |
| SEC-23 | > 10 attempts/min/IP on auth endpoints (`login` / `change-password`) → 429 `ProblemDetails` (`code: "rate_limited"`) |
| SEC-24 | Starting the host with a `Jwt:Key` shorter than 32 **decoded** bytes (e.g. 32 Base64 chars = 24 bytes) fails fast at startup |
| SEC-25 | With `OpenApi:EnableUi=false`, both `/swagger` and `/openapi/v1.json` return 404 |

## 13. Residual risks (documented, accepted for a personal project)

Access tokens cannot be revoked before they expire (15 min); `POST /api/auth/register` returns `409 email_already_registered` (email existence is enumerable via registration because email verification is out of scope for v1); local Docker demo binds plain HTTP on `localhost:8080` (`HttpsRedirection__Enabled=false`, assuming TLS termination at reverse proxy in real deployments); no background cleanup job purges old expired/revoked refresh-token rows; no global rate limit on non-auth endpoints; no MFA; no password reset by email; no account lock-out beyond rate limiting; symmetric JWT key shared across instances; refresh token transported in JSON body.
