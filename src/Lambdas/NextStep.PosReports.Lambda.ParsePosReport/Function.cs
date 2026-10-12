using System.Net;
using Amazon.DynamoDBv2;
using Amazon.Lambda.Core;
using Amazon.Lambda.S3Events;
using Amazon.Lambda.Serialization.SystemTextJson;
using Amazon.S3;
using Amazon.SQS;
using NextStep.PosReports.Domain.Identifiers;
using NextStep.PosReports.Domain.Parsing;
using NextStep.PosReports.Infrastructure.Configuration;
using NextStep.PosReports.Infrastructure.Messaging;
using NextStep.PosReports.Infrastructure.Persistence;
using NextStep.PosReports.Infrastructure.Storage;

[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

namespace NextStep.PosReports.Lambda.ParsePosReport;

/// <summary>
/// Triggered by new objects under <c>pos/</c>: parses the raw report, stores the structured report under
/// <c>attachment/</c>, records it in the parsed-reports table and queues it for calculation.
/// </summary>
/// <remarks>
/// Every step is idempotent (same keys on a retry), so a failed invocation can safely be retried by Lambda.
/// </remarks>
public sealed class Function
{
    private readonly IObjectStorage _objectStorage;
    private readonly IParsedReportRepository _parsedReportRepository;
    private readonly ICalculationRequestPublisher _calculationRequestPublisher;

    /// <summary>Used by the Lambda runtime.</summary>
    public Function()
        : this(
            new S3ObjectStorage(new AmazonS3Client(), EnvironmentVariables.GetRequired(EnvironmentVariables.BucketName)),
            new DynamoDbParsedReportRepository(
                new AmazonDynamoDBClient(), EnvironmentVariables.GetRequired(EnvironmentVariables.ParsedReportsTableName)),
            new SqsCalculationRequestPublisher(
                new AmazonSQSClient(), EnvironmentVariables.GetRequired(EnvironmentVariables.CalculationQueueUrl)))
    {
    }

    public Function(
        IObjectStorage objectStorage,
        IParsedReportRepository parsedReportRepository,
        ICalculationRequestPublisher calculationRequestPublisher)
    {
        _objectStorage = objectStorage;
        _parsedReportRepository = parsedReportRepository;
        _calculationRequestPublisher = calculationRequestPublisher;
    }

    public async Task HandleAsync(S3Event s3Event, ILambdaContext context)
    {
        foreach (var record in s3Event.Records ?? [])
        {
            // Keys in S3 event notifications are URL-encoded (':' arrives as '%3A').
            var objectKey = WebUtility.UrlDecode(record.S3.Object.Key);

            try
            {
                await ParseAsync(objectKey, context);
            }
            catch (Exception exception)
            {
                context.Logger.LogError(exception, "Failed to parse POS report {ObjectKey}", objectKey);
                throw;
            }
        }
    }

    private async Task ParseAsync(string objectKey, ILambdaContext context)
    {
        // Year and month come from the receive time in the key, not from the clock now,
        // so the flight id always matches the one the API returned.
        var rawReportKey = ObjectKeys.ParseRawReportKey(objectKey);
        var rawReport = await _objectStorage.GetTextAsync(objectKey);
        var report = PosReportParser.Parse(rawReport, rawReportKey.ReceivedAt);

        var attachmentKey = ObjectKeys.ForAttachment(report.FlightId, report.Timestamp);
        await _objectStorage.PutJsonAsync(attachmentKey, report);
        await _parsedReportRepository.SaveAsync(new ParsedReportRecord(report.FlightId, report.Timestamp, attachmentKey));
        await _calculationRequestPublisher.PublishAsync(new CalculationRequest(report.FlightId, report.Timestamp));

        context.Logger.LogInformation(
            "Parsed POS report for flight {FlightId}, stored as {AttachmentKey} and queued for calculation",
            report.FlightId,
            attachmentKey);
    }
}
