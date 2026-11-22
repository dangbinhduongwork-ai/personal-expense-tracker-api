using ExpenseTracker.Application.Dtos.Common;
using ExpenseTracker.Application.Dtos.Transactions;
using ExpenseTracker.Application.Services.Transactions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.Api.Controllers;

/// <summary>
/// Endpoints for managing the authenticated user's income and expense transactions.
/// </summary>
[ApiController]
[Route("api/transactions")]
[Authorize]
public sealed class TransactionsController(ITransactionService transactionService) : ControllerBase
{
    /// <summary>Lists the caller's transactions with optional filtering, searching, sorting, and paging.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<TransactionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PagedResult<TransactionDto>>> GetAll(
        [FromQuery] TransactionListQuery query,
        CancellationToken cancellationToken)
    {
        var result = await transactionService.GetAllAsync(query, cancellationToken);
        return Ok(result);
    }

    /// <summary>Returns a single transaction owned by the caller.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(TransactionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TransactionDto>> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        var transaction = await transactionService.GetByIdAsync(id, cancellationToken);
        return Ok(transaction);
    }

    /// <summary>Creates a new transaction for the caller.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(TransactionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<TransactionDto>> Create(
        [FromBody] CreateTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var created = await transactionService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Replaces an existing transaction owned by the caller.</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(TransactionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TransactionDto>> Update(
        int id,
        [FromBody] UpdateTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await transactionService.UpdateAsync(id, request, cancellationToken);
        return Ok(updated);
    }

    /// <summary>Deletes a transaction owned by the caller.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        int id,
        CancellationToken cancellationToken)
    {
        await transactionService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
