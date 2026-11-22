# Requirements: Personal Expense Tracker API

## 1. Goal and audience

A RESTful API where a signed-in user records income/expenses, organises them in categories and reads financial statistics.
Priorities: correctness, security of user data, readable code, testability. **Not** priorities: scale-out, microservices, UI.

## 2. Functional requirements

### 2.1 Authentication and user management (AUTH)

| ID | Requirement |
|---|---|
| AUTH-01 | Register with email, password, full name. Email unique (case-insensitive). |
| AUTH-02 | Login with email + password → access token (JWT) + refresh token. |
| AUTH-03 | Refresh: exchange a valid refresh token for a new access + refresh token (rotation: old refresh token is revoked). |
| AUTH-04 | Logout: revoke the presented refresh token. |
| AUTH-05 | Get current user (`me`). |
| AUTH-06 | Update profile (full name only in MVP). |
| AUTH-07 | Change password (needs current password; revokes all the user's refresh tokens). |
| AUTH-08 | Every non-auth endpoint requires a valid access token. |
| AUTH-09 | The user id used by the server is **always** taken from the validated token, never from request body/query/route. |

### 2.2 Transactions (TXN)

| ID | Requirement |
|---|---|
| TXN-01 | Create an Income or Expense transaction (Title, Amount, Type, CategoryId, Description?, TransactionDate). |
| TXN-02 | List own transactions with paging. |
| TXN-03 | Filters: `type`, `categoryId`, `from`/`to` date range. |
| TXN-04 | Search by title (`search`, case-insensitive contains). |
| TXN-05 | Sort by date (default `date desc`); also `amount`, `title`; deterministic tie-breaker by Id. |
| TXN-06 | Get one transaction by id (own only). |
| TXN-07 | Update a transaction (full replace via PUT). |
| TXN-08 | Delete a transaction (hard delete in MVP). |
| TXN-09 | `CreatedAt` / `UpdatedAt` maintained by the server (UTC). |

### 2.3 Categories (CAT)

| ID | Requirement |
|---|---|
| CAT-01 | List categories available to the user = system categories + own personal categories; optional `type` filter. |
| CAT-02 | Get one category by id (system or own). |
| CAT-03 | Create personal category (Name, Type). |
| CAT-04 | Update own personal category (Name; Type only if unused). |
| CAT-05 | Delete own personal category only if no transaction uses it. |
| CAT-06 | System categories are seeded, read-only for users. |
| CAT-07 | Seeded Expense: Food, Transportation, Shopping, Entertainment, Education, Healthcare, Bills. Seeded Income: Salary, Bonus, Freelance, Investment, Other Income. |

### 2.4 Statistics (STAT) — all computed from the signed-in user's rows

| ID | Requirement | Endpoint (see [api-specification.md](api-specification.md)) |
|---|---|---|
| STAT-01 | Total income | `summary` |
| STAT-02 | Total expense | `summary` |
| STAT-03 | Balance = income − expense | `summary` |
| STAT-04 | By day | `daily` |
| STAT-05 | By month (12 rows for a year) | `monthly` |
| STAT-06 | By year | `yearly` |
| STAT-07 | By category (with percentage of total) | `categories` |
| STAT-08 | Top N expense categories | `top-categories` |
| STAT-09 | Month-vs-month comparison | `comparison` |
| STAT-10 | Transaction history in a period | **reuse** `GET /api/transactions?from&to` (no separate endpoint) |

## 3. Business rules (BR)

| ID | Rule | Violation → |
|---|---|---|
| BR-01 | `Amount` > 0, ≤ 1,000,000,000,000, at most 2 decimal places. Type is C# `decimal`, SQL `decimal(18,2)`. Never float/double. | 400 |
| BR-02 | `Type` ∈ {`Income`,`Expense`} (JSON strings; numeric values rejected). | 400 |
| BR-03 | `CategoryId` must refer to a system category or one of the user's own categories. Someone else's category behaves as "not found". | 400 `category_not_found` |
| BR-04 | `Category.Type` must equal `Transaction.Type`. | 400 `category_type_mismatch` |
| BR-05 | `TransactionDate` is a calendar date (`yyyy-MM-dd`), ≥ 2000-01-01 and ≤ *today in the app time zone* + 1 day. | 400 |
| BR-06 | Ownership: a user can read/write only own transactions and own personal categories. Other users' resources → **404** (never 403, never 200). | 404 |
| BR-07 | System categories cannot be created/edited/deleted through the API. | 403 `system_category_readonly` |
| BR-08 | Category name: 1–100 chars, trimmed, unique per (owner, Type), case-insensitive; also must not equal a system category name of the same Type. | 409 |
| BR-09 | A category used by ≥ 1 transaction cannot be deleted. | 409 `category_in_use` |
| BR-10 | A category used by ≥ 1 transaction cannot change `Type`. | 409 `category_in_use` |
| BR-11 | Email: valid format, max 256, stored trimmed + lower-case, unique. | 400 / 409 `email_already_registered` |
| BR-12 | Password: 8–100 chars, at least one letter and one digit. Stored only as a salted hash. | 400 |
| BR-13 | Date ranges need `from` ≤ `to`. `daily` max range 366 days. | 400 `invalid_date_range` |
| BR-14 | A period with no data returns zeros / empty lists with 200 — not an error. | 200 |
| BR-15 | Paging: `page` ≥ 1 (default 1), `pageSize` 1–100 (default 20). | 400 |
| BR-16 | Changing the password revokes all refresh tokens of the user. | — |
| BR-17 | Login failure message is generic ("Invalid email or password") — no user enumeration through login. | 401 `invalid_credentials` |

## 4. Non-functional requirements (NFR)

| ID | Requirement |
|---|---|
| NFR-01 | Windows 10/11 friendly: build, test, run without WSL (Docker Desktop optional but recommended). |
| NFR-02 | Statistics use database aggregation (`GroupBy` + `Sum` translated to SQL). Never load all rows then sum in memory. |
| NFR-03 | List endpoints are always paged; read-only queries use `AsNoTracking()`. |
| NFR-04 | Consistent errors: RFC 9457 `ProblemDetails` for every non-2xx response (see [api-specification.md](api-specification.md)). |
| NFR-05 | No secrets in Git. Configuration via user-secrets (dev) and environment variables (Docker/prod). |
| NFR-06 | Structured logging with `ILogger`; never log passwords, tokens, or full request bodies. |
| NFR-07 | Clean, readable code with small methods, clear names, and comments explaining non-obvious invariants. |
| NFR-08 | Zero compiler warnings (`TreatWarningsAsErrors` in Release/CI) and full automated test verification. |
| NFR-09 | Soft performance target (local, 10 000 transactions): list and statistics endpoints respond < 300 ms. Measured, not assumed. |

## 5. Out of scope for v1

Frontend/UI, multi-currency, shared/family accounts, roles/admin, email verification and password reset by email, OAuth/social login,
file attachments, soft delete/restore, bank import, API versioning, caching (Redis), message queues, microservices.

## 6. Planned enhancements (post-v1)

Monthly budget + spending limit + warning, CSV export, recurring transactions.

## 7. Definition of Done

1. All endpoints in [api-specification.md](api-specification.md) implemented, visible and callable in Swagger UI (with the Authorize button for the Bearer token).
2. All BR rules enforced and covered by tests.
3. Security tests in [testing.md](testing.md) and [security.md](security.md) pass, especially "User A cannot read/update/delete User B's data".
4. `dotnet build` clean and `dotnet test` green on a fresh clone (Windows) following the README only.
5. `docker compose up` starts API + SQL Server; Swagger reachable.
6. No secrets in Git history; `.env.example` provided.
7. README + `docs/design/` + `docs/decisions/` explain the system clearly.
