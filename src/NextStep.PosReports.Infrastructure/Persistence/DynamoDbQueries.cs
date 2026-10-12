using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace NextStep.PosReports.Infrastructure.Persistence;

internal static class DynamoDbQueries
{
    /// <summary>Returns the item with the highest sort key (latest timestamp) for a flight, or null.</summary>
    public static async Task<Dictionary<string, AttributeValue>?> GetLatestItemAsync(
        IAmazonDynamoDB dynamoDbClient,
        string tableName,
        string flightId,
        CancellationToken cancellationToken)
    {
        var request = new QueryRequest
        {
            TableName = tableName,
            KeyConditionExpression = $"{AttributeNames.FlightId} = :flightId",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":flightId"] = new() { S = flightId },
            },
            ScanIndexForward = false,
            Limit = 1,
        };

        var response = await dynamoDbClient.QueryAsync(request, cancellationToken);

        return response.Items is { Count: > 0 } items ? items[0] : null;
    }
}
