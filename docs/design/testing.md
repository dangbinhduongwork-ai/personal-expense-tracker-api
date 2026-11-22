# Testing Strategy

## 1. Pyramid and tools

| Layer | Project | Tools | DB | Purpose | Needs Docker |
|---|---|---|---|---|---|
| Unit | `tests/ExpenseTracker.UnitTests` | xUnit v3, Moq, `FakeTimeProvider`, EF Core **InMemory** | InMemory (fresh per test) | Business rules, branching, error mapping inputs | no |
| Integration | `tests/ExpenseTracker.IntegrationTests` | xUnit v3, `WebApplicationFactory<Program>`, Testcontainers.MsSql | Real SQL Server | HTTP contract, auth pipeline, constraints, LINQ→SQL, security | yes (or `TEST_SQL_CONNECTION`) |
| Manual/API | `postman/` | Postman collection | dev DB | Exploratory testing and local API demos | n/a |

**InMemory provider caveats (teach this):** no foreign keys, no unique indexes, no CHECK constraints, no SQL translation. So it can hide bugs. Therefore anything about constraints, translation, `decimal` arithmetic or statistics is verified in **integration** tests. Unit tests must not "prove" those.
**Why not SQLite:** its provider has limitations with `decimal` aggregates/ordering and differs from SQL Server.

## 2. Conventions

* Names: `Method_Scenario_ExpectedResult`, e.g. `CreateAsync_AmountIsZero_ThrowsBusinessRuleException`.
* Arrange-Act-Assert with blank lines; one logical assertion group per test; no logic (loops/ifs) in tests.
* Plain xUnit `Assert.*` (no FluentAssertions). Use `Assert.Equal(expected, actual)` on `decimal` with exact values.
* Time: inject `FakeTimeProvider`; tests set "now" to **2026-12-15T03:00:00Z** unless stated.
* Test independence: no shared mutable state; each integration test **registers its own fresh user(s)** via a helper → data is naturally isolated because all data is user-scoped. The database is created once per fixture by running **migrations** (this also tests the migrations).
* xUnit v3: respect `TestContext.Current.CancellationToken` analyzer hints; run with `dotnet test` (Microsoft.Testing.Platform runner from the template's `global.json`). If `dotnet test` prints an MTP-related error, follow the message/aka.ms link — do not downgrade silently.
* No `Thread.Sleep`; no network access other than the test container.
* **Skipped tests count as failures** unless a documented reason + linked issue exists.

## 3. Integration test infrastructure

| Piece | Responsibility |
|---|---|
| `SqlServerFixture` (`IAsyncLifetime`, collection fixture) | If env var `TEST_SQL_CONNECTION` is set → use it (create a unique database name per run). Else start `Testcontainers.MsSql` (image tag from one constant, default `mcr.microsoft.com/mssql/server:2022-latest`). Expose the connection string. |
| `ApiFactory : WebApplicationFactory<Program>` | `public partial class Program { }` is exposed from Api. Replaces connection string, sets `Jwt:*` test values (key ≥ 32 bytes), sets `Database:MigrateOnStartup=true`, sets `HttpsRedirection:Enabled=false`, sets `OpenApi:EnableUi`, configures `RateLimiting:PermitLimit` (high limit for normal suites; 10 for `SEC-23`), optionally replaces `TimeProvider` with `FakeTimeProvider` (also wired to `JwtBearerOptions.TimeProvider`). |
| `AuthHelper` | `RegisterAndLoginAsync()` → returns a `HttpClient` with Bearer token + the user's id/email. |
| `ApiClientExtensions` | Typed helpers: create category/transaction, read `ProblemDetails`. |
| Cleanup | Container disposed by the fixture; no manual deletes needed. |

## 4. Unit test catalogue (minimum)

**AuthService** — register: success, duplicate email (case-insensitive), email normalised, password stored hashed (≠ plaintext); login: success, unknown email, wrong password (same exception), rehash-needed path; refresh: success rotates (old revoked, new stored hashed), expired, revoked-reuse ⇒ all revoked, unknown; logout: revokes own token, ignores unknown; `UserService`: update profile, change password (wrong current ⇒ `current_password_incorrect`, success revokes all refresh tokens).
**CategoryService** — list = system + own (not others'); create ok; duplicate own name; same name as system; get foreign ⇒ NotFound; update system ⇒ Forbidden; update type while used ⇒ Conflict; delete used ⇒ Conflict `category_in_use`; delete unused ok; delete system ⇒ Forbidden.
**TransactionService** — create: amount `-1`, `0`, `0.001` (3 dp), above max, valid; date: before 2000, tomorrow+1, `default(DateOnly)`; category of wrong type ⇒ `category_type_mismatch`; foreign category ⇒ `category_not_found`; `UserId` comes from `ICurrentUserService`; update: foreign ⇒ NotFound; `UpdatedAt` changes, `CreatedAt` doesn't; delete foreign ⇒ NotFound; list: filter type/category/date/search, sort asc/desc, stable paging (tie-breaker), page beyond last ⇒ empty items, invalid range ⇒ `invalid_date_range`.
**StatisticsService (logic only)** — zero-fill of days/months, percentage rounding (2 dp, sums to ~100), comparison change & `null` percent when previous = 0, default previous-month computation across year boundary (Jan → Dec previous year), range validation (daily > 366 days). *(SQL translation is tested in integration.)*

## 5. Integration test catalogue (minimum)

| Area | Tests |
|---|---|
| Auth | Register (201, no tokens), duplicate (409), weak password (400); Login (200, tokens), bad credentials (401); Refresh rotation; Logout; Me; Profile; Change password; JWT end-to-end: protected endpoint with/without token |
| Categories | CRUD happy path; list includes 12 seeded system categories; 403 on system edit/delete; 409 in-use delete; 409 duplicate; type filter |
| Transactions | CRUD happy path; filters (type, category, from/to, search); sort + paging (page 1/2/last/beyond); amount/date/type validation → 400 with `errors`; category mismatch → 400; DB CHECK/FK reached only through API-level rules |
| Statistics | All 7 endpoints against **DS-1** with exact expected values (§7); empty month → zeros; invalid ranges → 400; user B's noise excluded |
| Security | SEC-01 … SEC-25 from [security.md](security.md) |
| Errors | ProblemDetails shape for 400/401/403/404/409/500; `code` present; `traceId` present; no stack trace in non-Development |
| Infra | `/health` returns 200 with DB up; migrations apply on empty DB; seed has 12 rows |
| DB constraints (direct DbContext in integration project) | CHECK `Amount > 0`, FK restrict, unique indexes |

## 6. Business-rule test matrix (from the brief)

| Case | Layer | Expected |
|---|---|---|
| Amount negative / zero | unit + integration | 400 `validation_failed` (field `amount`) |
| Invalid date (default, 1999, far future) | unit + integration | 400 |
| Category of wrong type | unit + integration | 400 `category_type_mismatch` |
| Delete category in use | unit + integration | 409 `category_in_use` |
| Statistics for a month with no data | integration | 200, zeros, 0 count |
| Invalid date range (`from` > `to`, > 366 days) | unit + integration | 400 `invalid_date_range` |
| Unauthenticated access | integration | 401 |
| User A → User B's data | integration | 404 (SEC-06…14) |

## 7. Statistics dataset DS-1 (deterministic; expected numbers are pre-computed)

"Now" = 2026-12-15 (fake clock). Amounts in VND-like numbers, 2 dp. Category ids refer to the seed (1 Food, 3 Shopping, 7 Bills, 8 Salary, 10 Freelance).

**User A**
| Date | Type | Category | Amount |
|---|---|---|---|
| 2026-10-05 | Income | Salary (8) | 20,000,000.00 |
| 2026-10-10 | Expense | Food (1) | 1,000,000.00 |
| 2026-10-20 | Expense | Bills (7) | 2,000,000.00 |
| 2026-11-01 | Income | Salary (8) | 22,000,000.00 |
| 2026-11-01 | Expense | Food (1) | 150,000.50 |
| 2026-11-03 | Expense | Food (1) | 349,999.50 |
| 2026-11-03 | Expense | Shopping (3) | 500,000.00 |
| 2026-11-15 | Income | Freelance (10) | 3,000,000.00 |
| 2026-11-30 | Expense | Bills (7) | 1,500,000.00 |

**User B (noise, must never leak):** 2026-11-02 Income Salary 99,999,999.00; 2026-11-02 Expense Food 88,888,888.00.

**Expected for User A**
| Endpoint | Expected |
|---|---|
| `summary?year=2026&month=11` | income 25,000,000.00 · expense 2,500,000.00 · balance 22,500,000.00 · count 6 |
| `summary?year=2026&month=10` | income 20,000,000.00 · expense 3,000,000.00 · balance 17,000,000.00 · count 3 |
| `summary` (all time) | income 45,000,000.00 · expense 5,500,000.00 · balance 39,500,000.00 · count 9 |
| `summary?year=2026&month=9` | zeros, count 0 |
| `daily?from=2026-11-01&to=2026-11-03` | 11-01: income 22,000,000.00 · expense 150,000.50 · net 21,849,999.50; 11-02: all 0; 11-03: income 0 · expense 849,999.50 · net −849,999.50 |
| `monthly?year=2026` | 12 rows; Oct = (20,000,000.00 / 3,000,000.00 / 17,000,000.00); Nov = (25,000,000.00 / 2,500,000.00 / 22,500,000.00); other months 0 |
| `yearly?fromYear=2026&toYear=2026` | 2026 = income 45,000,000.00 · expense 5,500,000.00 · balance 39,500,000.00 |
| `categories?type=Expense&from=2026-11-01&to=2026-11-30` | Bills 1,500,000.00 (60.00 %, 1 tx) · Food 500,000.00 (20.00 %, 2 tx) · Shopping 500,000.00 (20.00 %, 1 tx). Order: Bills first; Food/Shopping tie → tie-break by name asc (Food, Shopping) |
| `top-categories?type=Expense&limit=1&from=2026-11-01&to=2026-11-30` | Bills only (total 1,500,000.00, 1 tx, percentage **60.00 %** of total period Expense) |
| `categories?type=Income&from=2026-11-01&to=2026-11-30` | Salary 22,000,000.00 (88.00 %) · Freelance 3,000,000.00 (12.00 %) |
| `comparison?year=2026&month=11` | previous = Oct; incomeChange +5,000,000.00 (+25.00 %); expenseChange −500,000.00 (−16.67 %, `AwayFromZero`); balanceChange +5,500,000.00 (no `balanceChangePercent`) |
| `comparison?year=2026&month=1` | previous = 2025-12 (zeros) ⇒ percents `null` |

Summary tests also assert `from` and `to` bounds on `SummaryDto`, and category rankings break equal totals by category name ascending, then `categoryId` ascending.

## 8. Postman collection

`postman/ExpenseTracker.postman_collection.json` + `postman/local.postman_environment.json` with variables `baseUrl`, `accessToken`, `refreshToken`, `categoryId`, `transactionId`.
Folders: Auth → Categories → Transactions → Statistics → Security-negatives. The login request includes a post-response test script saving tokens; each request asserts expected status codes.

## 9. Coverage and CI

* CI runs `dotnet restore` → `dotnet build -c Release -warnaserror` → `dotnet test -c Release --no-build` → `dotnet list package --vulnerable --include-transitive`. GitHub `ubuntu-latest` runners have Docker preinstalled for Testcontainers.
