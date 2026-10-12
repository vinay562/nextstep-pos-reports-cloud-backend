using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using NextStep.PosReports.Domain.Models;
using NextStep.PosReports.Domain.Time;

namespace NextStep.PosReports.Infrastructure.Persistence;

/// <summary>
/// Index entry for a calculation result; the full result lives in S3 at <see cref="AttachmentKey"/>.
/// The headline figures are copied onto the item so the status API can answer from DynamoDB alone.
/// </summary>
public sealed record CalculationResultRecord(
    string FlightId,
    DateTimeOffset Timestamp,
    string AttachmentKey,
    string Status,
    int RemainingFlightTimeMinutes,
    int EstimatedFuelAtArrivalKg,
    bool LowFuelWarning)
{
    public static CalculationResultRecord From(FlightCalculationResult result, string attachmentKey) =>
        new(
            result.FlightId,
            result.Timestamp,
            attachmentKey,
            result.Status,
            result.RemainingFlightTimeMinutes,
            result.EstimatedFuelAtArrivalKg,
            result.LowFuelWarning);
}

public interface ICalculationResultRepository
{
    Task SaveAsync(CalculationResultRecord record, CancellationToken cancellationToken = default);

    Task<CalculationResultRecord?> GetLatestAsync(string flightId, CancellationToken cancellationToken = default);
}

public sealed class DynamoDbCalculationResultRepository(IAmazonDynamoDB dynamoDbClient, string tableName) : ICalculationResultRepository
{
    public async Task SaveAsync(CalculationResultRecord record, CancellationToken cancellationToken = default)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            [AttributeNames.FlightId] = new() { S = record.FlightId },
            [AttributeNames.Timestamp] = new() { S = UtcTimestamp.ToMillisecondPrecision(record.Timestamp) },
            [AttributeNames.Attachment] = new() { S = record.AttachmentKey },
            [AttributeNames.Status] = new() { S = record.Status },
            [AttributeNames.RemainingFlightTimeMinutes] = ToNumber(record.RemainingFlightTimeMinutes),
            [AttributeNames.EstimatedFuelAtArrivalKg] = ToNumber(record.EstimatedFuelAtArrivalKg),
            [AttributeNames.LowFuelWarning] = new() { BOOL = record.LowFuelWarning },
        };

        await dynamoDbClient.PutItemAsync(new PutItemRequest(tableName, item), cancellationToken);
    }

    public async Task<CalculationResultRecord?> GetLatestAsync(string flightId, CancellationToken cancellationToken = default)
    {
        var item = await DynamoDbQueries.GetLatestItemAsync(dynamoDbClient, tableName, flightId, cancellationToken);

        return item is null ? null : ToRecord(item);
    }

    private static CalculationResultRecord ToRecord(Dictionary<string, AttributeValue> item) =>
        new(
            item[AttributeNames.FlightId].S,
            UtcTimestamp.Parse(item[AttributeNames.Timestamp].S),
            item[AttributeNames.Attachment].S,
            item[AttributeNames.Status].S,
            FromNumber(item[AttributeNames.RemainingFlightTimeMinutes]),
            FromNumber(item[AttributeNames.EstimatedFuelAtArrivalKg]),
            item[AttributeNames.LowFuelWarning].BOOL ?? false);

    private static AttributeValue ToNumber(int value) => new() { N = value.ToString(CultureInfo.InvariantCulture) };

    private static int FromNumber(AttributeValue value) => int.Parse(value.N, CultureInfo.InvariantCulture);
}
