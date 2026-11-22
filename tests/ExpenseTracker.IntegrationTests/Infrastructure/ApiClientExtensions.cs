using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExpenseTracker.IntegrationTests.Infrastructure;

/// <summary>
/// Shared HTTP client helpers matching the API's JSON serializer conventions (camelCase + string enums).
/// </summary>
public static class ApiClientExtensions
{
    public static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static Task<T?> GetApiJsonAsync<T>(
        this HttpClient client,
        string requestUri,
        CancellationToken cancellationToken) =>
        client.GetFromJsonAsync<T>(requestUri, JsonOptions, cancellationToken);

    public static Task<HttpResponseMessage> PostApiJsonAsync<TBody>(
        this HttpClient client,
        string requestUri,
        TBody body,
        CancellationToken cancellationToken) =>
        client.PostAsJsonAsync(requestUri, body, JsonOptions, cancellationToken);

    public static Task<HttpResponseMessage> PutApiJsonAsync<TBody>(
        this HttpClient client,
        string requestUri,
        TBody body,
        CancellationToken cancellationToken) =>
        client.PutAsJsonAsync(requestUri, body, JsonOptions, cancellationToken);

    public static Task<T?> ReadApiJsonAsync<T>(
        this HttpContent content,
        CancellationToken cancellationToken) =>
        content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);

    public static async Task<ProblemResponse> ReadProblemDetailsAsync(
        this HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken);
        var status = json.TryGetProperty("status", out var statusElement) ? statusElement.GetInt32() : (int)response.StatusCode;
        var detail = json.TryGetProperty("detail", out var detailElement) ? detailElement.GetString() : null;
        var code = json.TryGetProperty("code", out var codeElement) ? codeElement.GetString() : null;
        var traceId = json.TryGetProperty("traceId", out var traceElement) ? traceElement.GetString() : null;

        return new ProblemResponse(status, code, detail, traceId, json);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        return options;
    }

    public sealed record ProblemResponse(
        int Status,
        string? Code,
        string? Detail,
        string? TraceId,
        JsonElement Raw);
}
