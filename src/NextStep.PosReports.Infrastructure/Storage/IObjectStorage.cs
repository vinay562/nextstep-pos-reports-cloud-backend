using System.Text.Json;
using NextStep.PosReports.Infrastructure.Serialization;

namespace NextStep.PosReports.Infrastructure.Storage;

public interface IObjectStorage
{
    Task PutTextAsync(string key, string content, string contentType, CancellationToken cancellationToken = default);

    Task<string> GetTextAsync(string key, CancellationToken cancellationToken = default);
}

public static class ObjectStorageJsonExtensions
{
    private const string JsonContentType = "application/json";

    public static Task PutJsonAsync<T>(this IObjectStorage storage, string key, T document, CancellationToken cancellationToken = default) =>
        storage.PutTextAsync(key, JsonSerializer.Serialize(document, JsonDefaults.Options), JsonContentType, cancellationToken);

    public static async Task<T> GetJsonAsync<T>(this IObjectStorage storage, string key, CancellationToken cancellationToken = default)
    {
        var content = await storage.GetTextAsync(key, cancellationToken);

        return JsonSerializer.Deserialize<T>(content, JsonDefaults.Options)
               ?? throw new InvalidDataException($"Object '{key}' does not contain a JSON document.");
    }
}
