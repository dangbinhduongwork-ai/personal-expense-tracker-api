using ExpenseTracker.Application.Dtos.Categories;
using ExpenseTracker.Application.Enums;
using ExpenseTracker.Application.Services.Categories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.Api.Controllers;

/// <summary>
/// Endpoints for reading system categories and managing the authenticated user's personal categories.
/// </summary>
[ApiController]
[Route("api/categories")]
[Authorize]
public sealed class CategoriesController(ICategoryService categoryService) : ControllerBase
{
    /// <summary>Lists all system categories and personal categories owned by the caller.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CategoryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> GetAll(
        [FromQuery] TransactionType? type,
        CancellationToken cancellationToken)
    {
        var categories = await categoryService.GetAllAsync(type, cancellationToken);
        return Ok(categories);
    }

    /// <summary>Returns a single system or caller-owned personal category.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(CategoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CategoryDto>> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        var category = await categoryService.GetByIdAsync(id, cancellationToken);
        return Ok(category);
    }

    /// <summary>Creates a new personal category for the authenticated caller.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(CategoryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CategoryDto>> Create(
        [FromBody] CreateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var created = await categoryService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Updates a personal category owned by the caller.</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(CategoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CategoryDto>> Update(
        int id,
        [FromBody] UpdateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await categoryService.UpdateAsync(id, request, cancellationToken);
        return Ok(updated);
    }

    /// <summary>Deletes an unused personal category owned by the caller.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(
        int id,
        CancellationToken cancellationToken)
    {
        await categoryService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
