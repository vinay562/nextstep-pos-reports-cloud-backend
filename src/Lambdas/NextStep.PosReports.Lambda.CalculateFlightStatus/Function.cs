using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.SystemTextJson;
using Amazon.Lambda.SQSEvents;
using Amazon.S3;
using NextStep.PosReports.Domain.Airports;
using NextStep.PosReports.Domain.Calculation;
using NextStep.PosReports.Domain.Identifiers;
using NextStep.PosReports.Domain.Models;
using NextStep.PosReports.Domain.Time;
using NextStep.PosReports.Infrastructure.Configuration;
using NextStep.PosReports.Infrastructure.Messaging;
using NextStep.PosReports.Infrastructure.Persistence;
using NextStep.PosReports.Infrastructure.Serialization;
using NextStep.PosReports.Infrastructure.Storage;

[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

namespace NextStep.PosReports.Lambda.CalculateFlightStatus;

/// <summary>
/// Consumes calculation requests from SQS: loads the parsed report, estimates remaining flight time and
/// fuel at arrival, stores the result under <c>results/</c> and records it in the calculation-results table.
/// </summary>
/// <remarks>
/// Uses partial batch responses: only failed messages return to the queue, and after the configured
/// number of attempts SQS moves them to the dead-letter queue.
/// </remarks>
public sealed class Function
{
    private readonly IObjectStorage _objectStorage;
    private readonly IParsedReportRepository _parsedReportRepository;
    private readonly ICalculationResultRepository _calculationResultRepository;
    private readonly TimeProvider _timeProvider;

    /// <summary>Used by the Lambda runtime.</summary>
    public Function()
        : this(
            new S3ObjectStorage(new AmazonS3Client(), EnvironmentVariables.GetRequired(EnvironmentVariables.BucketName)),
            new DynamoDbParsedReportRepository(
                new AmazonDynamoDBClient(), EnvironmentVariables.GetRequired(EnvironmentVariables.ParsedReportsTableName)),
            new DynamoDbCalculationResultRepository(
                new AmazonDynamoDBClient(), EnvironmentVariables.GetRequired(EnvironmentVariables.CalculationResultsTableName)),
            TimeProvider.System)
    {
    }

    public Function(
        IObjectStorage objectStorage,
        IParsedReportRepository parsedReportRepository,
        ICalculationResultRepository calculationResultRepository,
        TimeProvider timeProvider)
    {
        _objectStorage = objectStorage;
        _parsedReportRepository = parsedReportRepository;
        _calculationResultRepository = calculationResultRepository;
        _timeProvider = timeProvider;
    }

    public async Task<SQSBatchResponse> HandleAsync(SQSEvent sqsEvent, ILambdaContext context)
    {
        var batchItemFailures = new List<SQSBatchResponse.BatchItemFailure>();

        foreach (var message in sqsEvent.Records ?? [])
        {
            try
            {
                await CalculateAsync(message, context);
            }
            catch (Exception exception)
            {
                context.Logger.LogError(
                    exception,
                    "Failed to calculate flight status for message {MessageId} (receive count {ReceiveCount}): {MessageBody}",
                    message.MessageId,
                    message.Attributes?.GetValueOrDefault("ApproximateReceiveCount") ?? "unknown",
                    message.Body);

                batchItemFailures.Add(new SQSBatchResponse.BatchItemFailure { ItemIdentifier = message.MessageId });
            }
        }

        return new SQSBatchResponse(batchItemFailures);
    }

    private async Task CalculateAsync(SQSEvent.SQSMessage message, ILambdaContext context)
    {
        var request = JsonSerializer.Deserialize<CalculationRequest>(message.Body, JsonDefaults.Options)
                      ?? throw new InvalidDataException("Calculation request message is empty.");

        var parsedReport = await _parsedReportRepository.GetAsync(request.FlightId, request.Timestamp)
                           ?? throw new InvalidOperationException(
                               $"No parsed report found for flight {request.FlightId} at {UtcTimestamp.ToSecondPrecision(request.Timestamp)}.");

        var report = await _objectStorage.GetJsonAsync<PosReport>(parsedReport.AttachmentKey);
        var destination = AirportDirectory.GetLocation(report.Destination);
        var estimate = FlightEstimator.Estimate(report, destination);

        var calculatedAt = UtcTimestamp.TruncateToMilliseconds(_timeProvider.GetUtcNow());
        var result = FlightCalculationResult.Create(report, estimate, calculatedAt);
        var resultKey = ObjectKeys.ForCalculationResult(result.FlightId, calculatedAt);

        await _objectStorage.PutJsonAsync(resultKey, result);
        await _calculationResultRepository.SaveAsync(CalculationResultRecord.From(result, resultKey));

        if (result.LowFuelWarning)
        {
            context.Logger.LogWarning(
                "LOW FUEL: flight {FlightId} is estimated to arrive with {EstimatedFuelAtArrivalKg} kg in {RemainingFlightTimeMinutes} min",
                result.FlightId,
                result.EstimatedFuelAtArrivalKg,
                result.RemainingFlightTimeMinutes);
        }
        else
        {
            context.Logger.LogInformation(
                "Calculated flight {FlightId}: {RemainingFlightTimeMinutes} min remaining, {EstimatedFuelAtArrivalKg} kg fuel at arrival",
                result.FlightId,
                result.RemainingFlightTimeMinutes,
                result.EstimatedFuelAtArrivalKg);
        }
    }
}
