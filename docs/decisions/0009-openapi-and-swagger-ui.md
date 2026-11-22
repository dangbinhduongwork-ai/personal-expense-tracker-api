# ADR-0009 — OpenAPI Generation & Swagger UI on .NET 10

* **Status:** ACCEPTED
* **Date:** 2026-11-22
* **Verified Against:** Microsoft Learn OpenAPI in ASP.NET Core 10 docs & NuGet `Microsoft.AspNetCore.OpenApi 10.0.15` (`Microsoft.OpenApi [2.13.0, 3.0.0)`) + `Swashbuckle.AspNetCore.SwaggerUI 10.3.1` (accessed 2026-11-22).

## Context
In .NET 10, `Microsoft.AspNetCore.OpenApi` generates `/openapi/v1.json` natively using **`Microsoft.OpenApi` v2.x**, which flattened `Microsoft.OpenApi.Models` into `Microsoft.OpenApi` and replaced inline `OpenApiSecurityScheme.Reference` with `OpenApiSecuritySchemeReference`. Legacy Swashbuckle `v6/v7` or `Microsoft.OpenApi 1.x` tutorials fail to compile (`CS0234`). Mixing `AddOpenApi()` and `AddSwaggerGen()` must also be avoided.

## Decision
Use **`Microsoft.AspNetCore.OpenApi` (`10.0.15`)** (`builder.Services.AddOpenApi(...)` + `app.MapOpenApi()`) paired with **`Swashbuckle.AspNetCore.SwaggerUI` (`10.3.1`)** (`app.UseSwaggerUI(o => o.SwaggerEndpoint("/openapi/v1.json", "Expense Tracker API v1"))`), gated behind `OpenApi:EnableUi`.

### Compile-Level Plan (`src/ExpenseTracker.Api/Extensions/OpenApiExtensions.cs`)
1. Namespaces: `using Microsoft.AspNetCore.Authorization;`, `using Microsoft.AspNetCore.OpenApi;`, `using Microsoft.OpenApi;` (NO `Microsoft.OpenApi.Models`).
2. `IOpenApiDocumentTransformer`: registers `"Bearer"` in `document.Components.SecuritySchemes` (`IDictionary<string, IOpenApiSecurityScheme>`) as `new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header, Description = "..." }`.
3. `IOpenApiOperationTransformer`: checks `context.Description.ActionDescriptor.EndpointMetadata` for `IAllowAnonymous` (and endpoint authorization attributes); adds `new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = [] }` only to non-anonymous operations so Swagger UI displays lock icons and sends `Authorization: Bearer <token>` via **Authorize**.

## Consequences
* Uses Microsoft's first-party OpenAPI generator without `AddSwaggerGen` conflicts while preserving the familiar Swagger UI **Authorize** button and Postman-importable `/openapi/v1.json`.
