# ADR-0004 — Controllers, DataAnnotations, and Manual Mapping

* **Status:** ACCEPTED
* **Date:** 2026-11-22

## Context
We need predictable REST endpoint routing, request validation without extra licensed dependencies, and explicit DTO mapping that a junior developer can read and step through in a debugger.

## Decision
Use **ASP.NET Core Controllers** (`[ApiController]`), **DataAnnotations** (`+ IValidatableObject` where cross-field checks apply) for request shape validation, and **manual `ToDto()` extension methods** instead of Minimal APIs, FluentValidation, MediatR, or AutoMapper (which adopted commercial licensing in v15+ / v13+).

## Consequences
* Explicit attributes (`[Authorize]`, `[AllowAnonymous]`, `[ProducesResponseType]`) keep API contracts and OpenAPI metadata transparent.
* Zero hidden reflection mapping; compile-time safety when DTO properties change.
