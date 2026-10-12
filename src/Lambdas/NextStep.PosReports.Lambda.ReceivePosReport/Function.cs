using System.Net;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.SystemTextJson;
using Amazon.S3;
using NextStep.PosReports.Domain.Identifiers;
using NextStep.PosReports.Domain.Models;
using NextStep.PosReports.Domain.Parsing;
using NextStep.PosReports.Domain.Time;
using NextStep.PosReports.Infrastructure.Configuration;
using NextStep.PosReports.Infrastructure.Http;
using NextStep.PosReports.Infrastructure.Storage;

[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

namespace NextStep.PosReports.Lambda.ReceivePosReport;

/// <summary>
/// <c>POST /pos-reports</c>: validates the raw POS report, stores it under <c>pos/</c> and
/// returns the flight id right away. Parsing and calculation continue asynchronously.
/// </summary>
public sealed class Function
{
    private const string PlainTextContentType = "text/plain";

    private readonly IObjectStorage _objectStorage;
    private readonly TimeProvider _timeProvider;

    /// <summary>Used by the Lambda runtime.</summary>
    public Function()
        : this(
            new S3ObjectStorage(new AmazonS3Client(), EnvironmentVariables.GetRequired(EnvironmentVariables.BucketName)),
            TimeProvider.System)
    {
    }

    public Function(IObjectStorage objectStorage, TimeProvider timeProvider)
    {
        _objectStorage = objectStorage;
        _timeProvider = timeProvider;
    }

    public async Task<APIGatewayHttpApiV2ProxyResponse> HandleAsync(APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context)
    {
        var receivedAt = UtcTimestamp.TruncateToMilliseconds(_timeProvider.GetUtcNow());
        var rawReport = request.GetBodyText();

        PosReport report;
        try
        {
            report = PosReportParser.Parse(rawReport, receivedAt);
        }
        catch (PosReportFormatException exception)
        {
            context.Logger.LogWarning("Rejected POS report: {Reason}", exception.Message);
            return ApiResponses.Error(HttpStatusCode.BadRequest, exception.Message);
        }

        var objectKey = ObjectKeys.ForRawReport(report.FlightId, receivedAt);
        await _objectStorage.PutTextAsync(objectKey, rawReport!, PlainTextContentType);

        context.Logger.LogInformation("Received POS report for flight {FlightId}, stored as {ObjectKey}", report.FlightId, objectKey);

        return ApiResponses.Json(HttpStatusCode.Accepted, new ReceivePosReportResponse(report.FlightId, FlightStatus.Received));
    }
}

public sealed record ReceivePosReportResponse(string FlightId, string Status);
