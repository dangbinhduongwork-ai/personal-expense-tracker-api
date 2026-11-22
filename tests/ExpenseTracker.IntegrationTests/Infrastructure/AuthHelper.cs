using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ExpenseTracker.Application.Dtos.Auth;
using ExpenseTracker.Application.Dtos.Users;

namespace ExpenseTracker.IntegrationTests.Infrastructure;

/// <summary>
/// Test identity helper generating isolated user accounts and authenticated clients per test.
/// </summary>
public static class AuthHelper
{
    public const string DefaultTestPassword = "Password1!";

    public static string CreateUniqueEmail(string prefix = "user") =>
        $"{prefix}-{Guid.NewGuid():N}@expensetracker.test";

    public static async Task<AuthenticatedUser> RegisterAndLoginAsync(
        HttpClient client,
        string? email = null,
        string password = DefaultTestPassword,
        string fullName = "Test User",
        CancellationToken cancellationToken = default)
    {
        var userEmail = email ?? CreateUniqueEmail();

        using var registerResponse = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { email = userEmail, password, fullName },
            cancellationToken);

        if (registerResponse.StatusCode != HttpStatusCode.Created)
        {
            var body = await registerResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Failed to register test user '{userEmail}': {(int)registerResponse.StatusCode} {body}");
        }

        var userDto = await registerResponse.Content.ReadFromJsonAsync<UserDto>(cancellationToken)
            ?? throw new InvalidOperationException("Register response did not deserialize to UserDto.");

        using var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = userEmail, password },
            cancellationToken);

        if (loginResponse.StatusCode != HttpStatusCode.OK)
        {
            var body = await loginResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Failed to log in test user '{userEmail}': {(int)loginResponse.StatusCode} {body}");
        }

        var auth = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Login response did not deserialize to AuthResponse.");

        return new AuthenticatedUser(userDto.Id, userDto.Email, userDto.FullName, password, auth);
    }

    public static HttpClient CreateAuthenticatedClient(this ApiFactory factory, string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    public sealed record AuthenticatedUser(
        int Id,
        string Email,
        string FullName,
        string Password,
        AuthResponse Auth);
}
