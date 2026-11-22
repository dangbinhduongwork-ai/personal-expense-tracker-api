# API Specification

## 1. Conventions

| Topic | Rule |
|---|---|
| Base path | `/api/...` (no versioning in MVP). `GET /health` anonymous. |
| Auth | `Authorization: Bearer <accessToken>` on everything except register / login / refresh-token / health. |
| JSON | camelCase; enums as strings (`"Income"`, `"Expense"`), integers rejected in both JSON bodies and query parameters; dates `yyyy-MM-dd`; timestamps ISO-8601 UTC (`2026-11-17T08:30:00Z`); money as JSON numbers with ≤ 2 decimals. Request strings (`email`, `fullName`, category `name`, transaction `title`, `description`, `search`, `sortBy`, `sortDir`) are trimmed; whitespace-only required strings fail validation and optional whitespace-only `description`/`search` normalize to `null`. **Passwords and refresh tokens are never trimmed.** |
| Success bodies | Plain DTO or `PagedResult<T>`. `201` includes `Location`. `204` has no body. |
| Error bodies | `ProblemDetails` (`application/problem+json`): `type`, `title`, `status`, `detail`, `traceId`, plus extension `code` (machine-readable). Validation errors use `ValidationProblemDetails` (`errors: { field: [messages] }`, `code: "validation_failed"`). |
| Paging | Query `page` (≥1, default 1), `pageSize` (1–100, default 20). Response `{ items, page, pageSize, totalCount, totalPages }`. When `totalCount == 0`, `totalPages == 0`; requesting `page > totalPages` returns `200` with `items: []`. |
| Ownership | Resource of another user ⇒ **404** with the same body as a missing resource. |
| Idempotency | `PUT` replaces; `DELETE` returns 204 on success and 404 if already gone. |

### 1.1 Status codes used

`200 OK` · `201 Created` · `204 No Content` · `400 Bad Request` (validation / business rule) · `401 Unauthorized` (missing/invalid/expired token, bad credentials) · `403 Forbidden` (system category is read-only) · `404 Not Found` (missing or foreign) · `409 Conflict` (duplicate / in use) · `429 Too Many Requests` (rate limit) · `500 Internal Server Error` (generic body) · `503 Service Unavailable` (`/health` DB failure).

### 1.2 Error codes (`code`)

`validation_failed`, `unauthorized`, `forbidden`, `invalid_date_range`, `invalid_credentials`, `email_already_registered`, `current_password_incorrect`, `refresh_token_invalid`, `category_not_found`, `category_type_mismatch`, `category_name_exists`, `category_in_use`, `system_category_readonly`, `transaction_not_found`, `rate_limited`, `internal_error`.

Example:
```json
{
  "type": "https://httpstatuses.io/409",
  "title": "Conflict",
  "status": 409,
  "detail": "Category is used by 3 transaction(s) and cannot be deleted.",
  "code": "category_in_use",
  "traceId": "00-4bf92f35..."
}
```

## 2. Endpoint summary

| # | Method | Path | Auth | Success |
|---|---|---|---|---|
| 0 | GET | `/health` | anonymous | 200 `Healthy` (plain text; 503 `Unhealthy` on DB failure) |
| 1 | POST | `/api/auth/register` | anonymous | 201 `UserDto` |
| 2 | POST | `/api/auth/login` | anonymous | 200 `AuthResponse` |
| 3 | POST | `/api/auth/refresh-token` | anonymous | 200 `AuthResponse` |
| 4 | POST | `/api/auth/logout` | Bearer | 204 |
| 5 | GET | `/api/auth/me` | Bearer | 200 `UserDto` |
| 6 | PUT | `/api/auth/profile` | Bearer | 200 `UserDto` |
| 7 | PUT | `/api/auth/change-password` | Bearer | 204 |
| 8 | GET | `/api/categories` | Bearer | 200 `CategoryDto[]` |
| 9 | GET | `/api/categories/{id}` | Bearer | 200 `CategoryDto` |
| 10 | POST | `/api/categories` | Bearer | 201 `CategoryDto` |
| 11 | PUT | `/api/categories/{id}` | Bearer | 200 `CategoryDto` |
| 12 | DELETE | `/api/categories/{id}` | Bearer | 204 |
| 13 | GET | `/api/transactions` | Bearer | 200 `PagedResult<TransactionDto>` |
| 14 | GET | `/api/transactions/{id}` | Bearer | 200 `TransactionDto` |
| 15 | POST | `/api/transactions` | Bearer | 201 `TransactionDto` |
| 16 | PUT | `/api/transactions/{id}` | Bearer | 200 `TransactionDto` |
| 17 | DELETE | `/api/transactions/{id}` | Bearer | 204 |
| 18 | GET | `/api/statistics/summary` | Bearer | 200 `SummaryDto` |
| 19 | GET | `/api/statistics/daily` | Bearer | 200 `DailyStatDto[]` |
| 20 | GET | `/api/statistics/monthly` | Bearer | 200 `MonthlyStatDto[]` |
| 21 | GET | `/api/statistics/yearly` | Bearer | 200 `YearlyStatDto[]` |
| 22 | GET | `/api/statistics/categories` | Bearer | 200 `CategoryStatDto[]` |
| 23 | GET | `/api/statistics/top-categories` | Bearer | 200 `CategoryStatDto[]` |
| 24 | GET | `/api/statistics/comparison` | Bearer | 200 `MonthComparisonDto` |

Rate limit: endpoints 1–4 (`register`, `login`, `refresh-token`, `logout`) and 7 (`change-password`) share the `auth` policy (10 requests / minute / IP using `RemoteIpAddress`; configurable via `RateLimiting:PermitLimit`) → `429` with `Retry-After` header (seconds) and `ProblemDetails` `code: "rate_limited"`.

## 3. DTO Shapes

```jsonc
// UserDto
{ "id": 1, "email": "a@b.com", "fullName": "An", "createdAt": "..." }
// AuthResponse
{ "accessToken": "...", "accessTokenExpiresAt": "...", "refreshToken": "...", "refreshTokenExpiresAt": "...", "user": { /* UserDto */ } }
// CategoryDto
{ "id": 1, "name": "Food", "type": "Expense", "isSystem": true, "createdAt": "..." }
// TransactionDto
{ "id": 10, "title": "Lunch", "amount": 45000.00, "type": "Expense", "categoryId": 1, "categoryName": "Food",
  "description": null, "transactionDate": "2026-11-17", "createdAt": "...", "updatedAt": "..." }
// PagedResult<T>
{ "items": [ /* T */ ], "page": 1, "pageSize": 20, "totalCount": 57, "totalPages": 3 }
// SummaryDto
{ "from": "2026-11-01", "to": "2026-11-30", "totalIncome": 0.00, "totalExpense": 0.00, "balance": 0.00, "transactionCount": 0 }
// DailyStatDto / MonthlyStatDto / YearlyStatDto
{ "date": "2026-11-01", "income": 0.00, "expense": 0.00, "net": 0.00 }
{ "year": 2026, "month": 11, "income": 0.00, "expense": 0.00, "balance": 0.00 }
{ "year": 2026, "income": 0.00, "expense": 0.00, "balance": 0.00 }
// CategoryStatDto
{ "categoryId": 1, "categoryName": "Food", "type": "Expense", "total": 0.00, "transactionCount": 0, "percentage": 0.00 } // share of period Type total, 2 dp AwayFromZero
// MonthComparisonDto
{ "current":  { "year": 2026, "month": 11, "income": 0.00, "expense": 0.00, "balance": 0.00 },
  "previous": { "year": 2026, "month": 10, "income": 0.00, "expense": 0.00, "balance": 0.00 },
  "incomeChange": 0.00, "expenseChange": 0.00, "balanceChange": 0.00,
  "incomeChangePercent": null, "expenseChangePercent": null }   // percent = null when previous value is 0 (2 dp AwayFromZero)
```

## 4. Authentication endpoints

### 1. `POST /api/auth/register`
* **Auth:** anonymous, rate-limited. **Request:** `RegisterRequest { email, password, fullName }`.
* **Validation:** email required, valid format, ≤ 256, trimmed + lower-cased; password 8–100 with ≥ 1 letter (`char.IsLetter`) and ≥ 1 digit (`char.IsDigit`), never trimmed; fullName 1–100 (trimmed, non-whitespace).
* **Response:** `201 UserDto` (+ `Location: /api/auth/me`). **No tokens** — the client logs in next.
* **Errors:** `400 validation_failed`, `409 email_already_registered`, `429`.
* **Authorization rule:** none. **Never** return the hash.

### 2. `POST /api/auth/login`
* **Auth:** anonymous, rate-limited. **Request:** `LoginRequest { email, password }` (`email` trimmed, ≤ 256; `password` 1–100, never trimmed).
* **Response:** `200 AuthResponse` (`Cache-Control: no-store`) — access token (JWT, 15 min) + refresh token (opaque random, 7 days).
* **Errors:** `400 validation_failed`, `401 invalid_credentials` (same message/body shape and PBKDF2 dummy-hash verification for unknown email and wrong password), `429`.
* Rehash the password if `PasswordHasher` reports `SuccessRehashNeeded`.

### 3. `POST /api/auth/refresh-token`
* **Auth:** anonymous, rate-limited. **Request:** `RefreshTokenRequest { refreshToken }` (required, ≤ 256).
* **Behaviour:** token must exist (by SHA-256 hash), not revoked (`RevokedAt == null`), not expired (`ExpiresAt > utcNow`) → atomically revoke it (`RevokedAt`, `ReplacedByTokenHash`) and issue a **new** access + refresh pair (`Cache-Control: no-store`). If a **revoked** token (`RevokedAt != null`, checked before `ExpiresAt`) is presented again → revoke **all** active refresh tokens of that user, commit (`SaveChangesAsync`), and return `401 refresh_token_invalid`.
* **Response:** `200 AuthResponse`. **Errors:** `400`, `401 refresh_token_invalid`, `429`.

### 4. `POST /api/auth/logout`
* **Auth:** Bearer, rate-limited. **Request:** `RefreshTokenRequest` (≤ 256). **Response:** `204` (strictly scoped to `TokenHash == hash && UserId == currentUserId`; also returns `204` when the token is unknown/foreign/already revoked, preserving existing `RevokedAt` — idempotent, no information leak).
* **Authorization rule:** only revokes a token that belongs to the caller.

### 5. `GET /api/auth/me`
* **Auth:** Bearer. **Response:** `200 UserDto` for the user in the token. **Errors:** `401`.

### 6. `PUT /api/auth/profile`
* **Auth:** Bearer. **Request:** `UpdateProfileRequest { fullName }` (1–100, trimmed, non-whitespace). **Response:** `200 UserDto`. **Errors:** `400`, `401`.
* Email cannot be changed in MVP. A request containing `userId` / `email` is ignored (those fields do not exist on the DTO).

### 7. `PUT /api/auth/change-password`
* **Auth:** Bearer, rate-limited. **Request:** `ChangePasswordRequest { currentPassword, newPassword }` (`currentPassword` 1–100; `newPassword` 8–100 per BR-12 and must differ from `currentPassword`, else `400 validation_failed`; never trimmed).
* **Response:** `204`; revokes **all** active refresh tokens of the user (BR-16).
* **Errors:** `400 validation_failed`, `400 current_password_incorrect` (not 401: the token itself is fine), `401`, `429`.

## 5. Category endpoints

### 8. `GET /api/categories?type=Income|Expense`
* **Auth:** Bearer. **Response:** `200 CategoryDto[]` = system + own, ordered deterministically: `isSystem` desc (`UserId == null` first), `name` asc (case-insensitive), `type` asc, `id` asc. Not paged (small list).
* **Validation:** `type` optional, must be a defined string enum value (`"Income"` or `"Expense"`; integer values rejected with `400 validation_failed`). **Errors:** `400`, `401`.
* **Authorization rule:** never returns another user's personal categories.

### 9. `GET /api/categories/{id}`
* **Response:** `200 CategoryDto`. **Errors:** `404 category_not_found` (missing **or** someone else's personal category), `401`.

### 10. `POST /api/categories`
* **Request:** `CreateCategoryRequest { name, type }`. `name` 1–100 (trimmed, non-whitespace), `type` string enum (`Income` = 1, `Expense` = 2).
* **Response:** `201 CategoryDto` (`isSystem=false`) + `Location: /api/categories/{id}`. **Errors:** `400`, `409 category_name_exists` (BR-08, case-insensitive against system + own), `401`.

### 11. `PUT /api/categories/{id}`
* **Request:** `UpdateCategoryRequest { name, type }` (same shape validation as create).
* **Rules (error precedence `404` → `403` → `409`):** load `c.Id == id && (c.UserId == null || c.UserId == userId)`: missing/foreign → `404 category_not_found`; `c.UserId == null` → `403 system_category_readonly`; changing `type` while caller transactions use it (`t.UserId == userId && t.CategoryId == id`) → `409 category_in_use`; duplicate name (excluding current `c.Id != id`) → `409 category_name_exists`. Updates `UpdatedAt`.
* **Response:** `200 CategoryDto`.

### 12. `DELETE /api/categories/{id}`
* **Rules (error precedence `404` → `403` → `409`):** load `c.Id == id && (c.UserId == null || c.UserId == userId)`: missing/foreign → `404`; system → `403 system_category_readonly`; in use (`t.UserId == userId && t.CategoryId == id`) → `409 category_in_use` (message includes the count). **Response:** `204`.

## 6. Transaction endpoints

### 13. `GET /api/transactions`
* **Query:** `page` (≥ 1, default 1), `pageSize` (1–100, default 20; invalid returns `400 validation_failed`), `type` (`Income`|`Expense`, integers rejected), `categoryId`, `from`, `to` (inclusive `yyyy-MM-dd`), `search` (trimmed, whitespace-only ignored, ≤ 100, literal substring match — case-insensitive, `%`/`_`/`[` escaped), `sortBy` (`date` default | `amount` | `title`, case-insensitive), `sortDir` (`desc` default | `asc`, case-insensitive; invalid `sortBy`/`sortDir` returns `400 validation_failed`).
* **Behaviour:** always `WHERE UserId = currentUser`; default order `TransactionDate DESC, Id DESC` (and generally ties break by `Id` matching `sortDir`: `DESC` when `desc`, `ASC` when `asc`); projection straight to `TransactionDto` with `AsNoTracking()`. A `categoryId` that is foreign/non-existent simply yields an empty page (no probing).
* **Response:** `200 PagedResult<TransactionDto>`. **Errors:** `400 validation_failed` / `invalid_date_range` (`from` > `to`), `401`.
* This endpoint also fulfils **STAT-10** (history in a period).

### 14. `GET /api/transactions/{id}`
* **Response:** `200 TransactionDto`. **Errors:** `404 transaction_not_found` (missing **or** another user's), `401`.

### 15. `POST /api/transactions`
* **Request:** `CreateTransactionRequest { title, amount, type, categoryId, description?, transactionDate }` (all required properties nullable value types on DTO with `[Required]` so omitted fields fail with `400`).
* **Validation:** `title` 1–200 (trimmed, non-whitespace); `amount` > 0, ≤ `1e12` (`1_000_000_000_000`), ≤ 2 decimals (`decimal.Round(amount, 2) == amount`, else `400 validation_failed` on field `amount`); `type` defined string enum; `categoryId` > 0; `description` trimmed, ≤ 1000 (whitespace-only normalized to `null`); `transactionDate` `2000-01-01` ≤ date ≤ `today + 1` in `App:TimeZoneId` (BR-05).
* **Business:** category must be usable by the caller (`400 category_not_found`) and `Category.Type == type` (`400 category_type_mismatch`). `UserId` set from the token; the DTO has **no** `userId` field.
* **Response:** `201 TransactionDto` + `Location: /api/transactions/{id}`. **Errors:** `400`, `401`.

### 16. `PUT /api/transactions/{id}`
* **Request:** `UpdateTransactionRequest` (same fields and shape/business validation as create; full-replace semantics — omitted or whitespace `description` sets `Description = null`).
* **Rules:** load own transaction first (`t.Id == id && t.UserId == userId`: null → `404 transaction_not_found`) before checking `categoryId` (`400`). Preserves `CreatedAt`, updates `UpdatedAt`. **Response:** `200 TransactionDto`.

### 17. `DELETE /api/transactions/{id}`
* **Rules:** foreign/missing → `404`. **Response:** `204`.

## 7. Statistics endpoints (all scoped to the caller; zero-filled where stated)

| # | Query parameters | Rules | Notes |
|---|---|---|---|
| 18 `summary` | `year?`, `month?` | `month` without `year` → `400 validation_failed`; year 2000–2100, month 1–12. `year+month` ⇒ month bounds; `year` only ⇒ Jan 1..Dec 31; neither ⇒ all-time (`from=null, to=null`). | totals via one grouped query by `Type` |
| 19 `daily` | `from`, `to` (required) | `from` ≤ `to`, inclusive calendar days (`to.DayNumber - from.DayNumber + 1`) ≤ 366 (`invalid_date_range`) | one row **per day** in range, zero-filled in memory after SQL grouping |
| 20 `monthly` | `year` (required) | 2000–2100 | always 12 rows (`1..12`), zero-filled; SARGable `DateOnly` bounds |
| 21 `yearly` | `fromYear?`, `toYear?` | bounds 2000–2100; defaults `toYear ??= currentYear`, `fromYear ??= Math.Max(2000, toYear - 4)`; `fromYear <= toYear` and inclusive span (`toYear - fromYear + 1`) ≤ 20 (`invalid_date_range`) | one row per year in `[fromYear..toYear]`, zero-filled |
| 22 `categories` | `type` (required, string enum), `from?`, `to?` | `from` and `to` independently optional (`from <= to` if both present); percentage = category total / period Type total × 100 (`Math.Round(..., 2, AwayFromZero)` per row); empty period ⇒ `[]`; only categories with ≥ 1 tx returned | ordered by `total` desc, `categoryName` asc (case-insensitive), `categoryId` asc |
| 23 `top-categories` | `type` (required, string enum), `limit?` (1–20, default 5), `from?`, `to?` | same as above (`percentage` stays share of full period Type total, not top-N sum), first `limit` rows | |
| 24 `comparison` | `year`, `month`, `compareToYear?`, `compareToMonth?` | previous = month before `year/month` unless `compareTo*` supplied (both or none, else `400 validation_failed`); year 2000–2100, month 1–12 (`year=2000&month=1` without `compareTo*` compares against `1999-12` with zero totals) | `*ChangePercent = null` when the previous value is 0; otherwise rounded `AwayFromZero` 2 dp (no `balanceChangePercent`) |

Common errors for 18–24: `400 validation_failed`, `400 invalid_date_range`, `401`. Empty data is **never** an error (BR-14).

## 8. Authorization matrix (summary)

| Resource | Anonymous | Authenticated owner | Authenticated other user |
|---|---|---|---|
| Health (`GET /health`) | ✅ | ✅ | ✅ |
| Register/Login/Refresh | ✅ | ✅ | ✅ |
| Own profile / password | ❌ 401 | ✅ | n/a |
| System category | ❌ 401 | read ✅, write ❌ 403 | read ✅, write ❌ 403 |
| Personal category | ❌ 401 | full | **404** |
| Transaction | ❌ 401 | full | **404** |
| Statistics | ❌ 401 | own data only | own data only (never sees others) |

## 9. OpenAPI requirements

* Every action has `[ProducesResponseType]` for each status in this document, plus a summary/description (XML comments or attributes).
* JWT `Bearer` security scheme + requirement on all non-anonymous endpoints → Swagger UI **Authorize** button works.
* Both `/openapi/v1.json` and `/swagger` are active only when `OpenApi:EnableUi == true` (returning `404` when `false`); `/openapi/v1.json` can be imported into Postman.
