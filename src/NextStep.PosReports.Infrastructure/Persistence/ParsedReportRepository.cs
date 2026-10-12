using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using NextStep.PosReports.Domain.Time;

namespace NextStep.PosReports.Infrastructure.Persistence;

/// <summary>Index entry for a parsed POS report; the report itself lives in S3 at <see cref="AttachmentKey"/>.</summary>
public sealed record ParsedReportRecord(string FlightId, DateTimeOffset Timestamp, string AttachmentKey);

public interface IParsedReportRepository
{
    Task SaveAsync(ParsedReportRecord record, CancellationToken cancellationToken = default);

    Task<ParsedReportRecord?> GetAsync(string flightId, DateTimeOffset timestamp, CancellationToken cancellationToken = default);

    Task<ParsedReportRecord?> GetLatestAsync(string flightId, CancellationToken cancellationToken = default);
}

public sealed class DynamoDbParsedReportRepository(IAmazonDynamoDB dynamoDbClient, string tableName) : IParsedReportRepository
{
    public async Task SaveAsync(ParsedReportRecord record, CancellationToken cancellationToken = default)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            [AttributeNames.FlightId] = new() { S = record.FlightId },
            [AttributeNames.Timestamp] = new() { S = UtcTimestamp.ToSecondPrecision(record.Timestamp) },
            [AttributeNames.Attachment] = new() { S = record.AttachmentKey },
        };

        await dynamoDbClient.PutItemAsync(new PutItemRequest(tableName, item), cancellationToken);
    }

    public async Task<ParsedReportRecord?> GetAsync(string flightId, DateTimeOffset timestamp, CancellationToken cancellationToken = default)
    {
        var request = new GetItemRequest
        {
            TableName = tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [AttributeNames.FlightId] = new() { S = flightId },
                [AttributeNames.Timestamp] = new() { S = UtcTimestamp.ToSecondPrecision(timestamp) },
            },
            // The parser writes this item just before queueing the message that leads here.
            ConsistentRead = true,
        };

        var response = await dynamoDbClient.GetItemAsync(request, cancellationToken);

        return response.Item is { Count: > 0 } item ? ToRecord(item) : null;
    }

    public async Task<ParsedReportRecord?> GetLatestAsync(string flightId, CancellationToken cancellationToken = default)
    {
        var item = await DynamoDbQueries.GetLatestItemAsync(dynamoDbClient, tableName, flightId, cancellationToken);

        return item is null ? null : ToRecord(item);
    }

    private static ParsedReportRecord ToRecord(Dictionary<string, AttributeValue> item) =>
        new(
            item[AttributeNames.FlightId].S,
            UtcTimestamp.Parse(item[AttributeNames.Timestamp].S),
            item[AttributeNames.Attachment].S);
}
