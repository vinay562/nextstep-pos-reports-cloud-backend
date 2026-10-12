using System.Net;
using System.Text;
using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using NextStep.PosReports.Infrastructure.Serialization;

namespace NextStep.PosReports.Infrastructure.Http;

public sealed record ErrorResponse(string Message);

/// <summary>Helpers for API Gateway HTTP API (payload format 2.0) requests and responses.</summary>
public static class ApiResponses
{
    private static readonly Dictionary<string, string> JsonHeaders = new() { ["Content-Type"] = "application/json" };

    public static APIGatewayHttpApiV2ProxyResponse Json<T>(HttpStatusCode statusCode, T body) =>
        new()
        {
            StatusCode = (int)statusCode,
            Headers = JsonHeaders,
            Body = JsonSerializer.Serialize(body, JsonDefaults.Options),
        };

    public static APIGatewayHttpApiV2ProxyResponse Error(HttpStatusCode statusCode, string message) =>
        Json(statusCode, new ErrorResponse(message));

    /// <summary>Returns the request body as text, decoding it if API Gateway delivered it base64-encoded.</summary>
    public static string? GetBodyText(this APIGatewayHttpApiV2ProxyRequest request) =>
        request.IsBase64Encoded && request.Body is not null
            ? Encoding.UTF8.GetString(Convert.FromBase64String(request.Body))
            : request.Body;
}
