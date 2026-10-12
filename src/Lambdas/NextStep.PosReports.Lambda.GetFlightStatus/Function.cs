using System.Net;
using Amazon.DynamoDBv2;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.SystemTextJson;
using NextStep.PosReports.Infrastructure.Configuration;
using NextStep.PosReports.Infrastructure.Http;
using NextStep.PosReports.Infrastructure.Persistence;

[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

namespace NextStep.PosReports.Lambda.GetFlightStatus;

/// <summary>
/// <c>GET /status/{flightId}</c>: returns the latest calculation for the flight; if none exists yet, the latest
/// parsed report (status <c>PARSED</c>); otherwise 404. A 404 or <c>PARSED</c> straight after submitting a
/// report is expected while the pipeline catches up.
/// </summary>
public sealed class Function
{
    private const string FlightIdPathParameter = "flightId";

    private readonly ICalculationResultRepository _calculationResultRepository;
    private readonly IParsedReportRepository _parsedReportRepository;

    /// <summary>Used by the Lambda runtime.</summary>
    public Function()
        : this(
            new DynamoDbCalculationResultRepository(
                new AmazonDynamoDBClient(), EnvironmentVariables.GetRequired(EnvironmentVariables.CalculationResultsTableName)),
            new DynamoDbParsedReportRepository(
                new AmazonDynamoDBClient(), EnvironmentVariables.GetRequired(EnvironmentVariables.ParsedReportsTableName)))
    {
    }

    public Function(ICalculationResultRepository calculationResultRepository, IParsedReportRepository parsedReportRepository)
    {
        _calculationResultRepository = calculationResultRepository;
        _parsedReportRepository = parsedReportRepository;
    }

    public async Task<APIGatewayHttpApiV2ProxyResponse> HandleAsync(APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context)
    {
        if (request.PathParameters is null
            || !request.PathParameters.TryGetValue(FlightIdPathParameter, out var flightId)
            || string.IsNullOrWhiteSpace(flightId))
        {
            return ApiResponses.Error(HttpStatusCode.BadRequest, "The flightId path parameter is required.");
        }

        try
        {
            return await GetLatestStatusAsync(flightId);
        }
        catch (Exception exception)
        {
            context.Logger.LogError(exception, "Failed to look up status for flight {FlightId}", flightId);
            throw;
        }
    }

    private async Task<APIGatewayHttpApiV2ProxyResponse> GetLatestStatusAsync(string flightId)
    {
        var latestResult = await _calculationResultRepository.GetLatestAsync(flightId);
        if (latestResult is not null)
        {
            return ApiResponses.Json(HttpStatusCode.OK, FlightStatusResponse.FromCalculationResult(latestResult));
        }

        var latestParsedReport = await _parsedReportRepository.GetLatestAsync(flightId);
        if (latestParsedReport is not null)
        {
            return ApiResponses.Json(HttpStatusCode.OK, FlightStatusResponse.FromParsedReport(latestParsedReport));
        }

        return ApiResponses.Error(HttpStatusCode.NotFound, $"No status found for flight '{flightId}'.");
    }
}
