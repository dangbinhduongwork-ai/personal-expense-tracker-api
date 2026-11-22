using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.IntegrationTests.Infrastructure;

/// <summary>
/// Test-only controller registered explicitly by SEC-22 integration tests to trigger
/// an unhandled exception inside the real middleware pipeline without polluting src/.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("test-diagnostics")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class TestFaultController : ControllerBase
{
    [HttpGet("unhandled")]
    public IActionResult ThrowUnhandled() =>
        throw new InvalidOperationException("Sensitive internal SQL exception text.");
}

