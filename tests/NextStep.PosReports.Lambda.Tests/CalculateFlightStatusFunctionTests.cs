using Amazon.Lambda.SQSEvents;
using Amazon.Lambda.TestUtilities;
using Microsoft.Extensions.Time.Testing;
using NextStep.PosReports.Domain.Models;
using NextStep.PosReports.Infrastructure.Persistence;
using NextStep.PosReports.Infrastructure.Storage;
using NextStep.PosReports.Lambda.CalculateFlightStatus;
using NSubstitute;

namespace NextStep.PosReports.Lambda.Tests;

public sealed class CalculateFlightStatusFunctionTests
{
    private readonly IObjectStorage _objectStorage = Substitute.For<IObjectStorage>();
    private readonly IParsedReportRepository _parsedReportRepository = Substitute.For<IParsedReportRepository>();
    private readonly ICalculationResultRepository _calculationResultRepository = Substitute.For<ICalculationResultRepository>();
    private readonly Function _function;

    public CalculateFlightStatusFunctionTests()
    {
        _function = new Function(
            _objectStorage, _parsedReportRepository, _calculationResultRepository, new FakeTimeProvider(SampleData.CalculatedAt));

        _parsedReportRepository
            .GetAsync(SampleData.FlightId, SampleData.ReportTimestamp, Arg.Any<CancellationToken>())
            .Returns(new ParsedReportRecord(SampleData.FlightId, SampleData.ReportTimestamp, SampleData.AttachmentKey));
        _objectStorage
            .GetTextAsync(SampleData.AttachmentKey, Arg.Any<CancellationToken>())
            .Returns(SampleData.AttachmentJson);
    }

    [Fact]
    public async Task ValidRequest_StoresResultAndRecordsIt()
    {
        var response = await _function.HandleAsync(SqsEventWith(SampleData.CalculationRequestJson), new TestLambdaContext());

        Assert.Empty(response.BatchItemFailures);
        await _objectStorage.Received(1).PutTextAsync(
            SampleData.ResultKey, SampleData.ResultJson, "application/json", Arg.Any<CancellationToken>());
        await _calculationResultRepository.Received(1).SaveAsync(
            new CalculationResultRecord(
                SampleData.FlightId, SampleData.CalculatedAt, SampleData.ResultKey, FlightStatus.Calculated, 43, 10513, false),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotEnoughFuel_RecordsLowFuelWarning()
    {
        const string lowFuelAttachment =
            """{"flightId":"UL20420260904RGNBKK","flightNumber":"UL204","departure":"RGN","destination":"BKK","timestamp":"2026-09-04T12:05:00Z","latitude":16.705,"longitude":96.2083,"groundSpeedKnots":450,"fuelOnBoardKg":1000,"fuelFlowKgPerHour":2800}""";
        _objectStorage.GetTextAsync(SampleData.AttachmentKey, Arg.Any<CancellationToken>()).Returns(lowFuelAttachment);

        var response = await _function.HandleAsync(SqsEventWith(SampleData.CalculationRequestJson), new TestLambdaContext());

        Assert.Empty(response.BatchItemFailures);
        await _calculationResultRepository.Received(1).SaveAsync(
            Arg.Is<CalculationResultRecord>(record =>
                record.LowFuelWarning && record.Status == FlightStatus.LowFuelWarning && record.EstimatedFuelAtArrivalKg < 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnknownDestinationAirport_ReportsMessageAsFailed()
    {
        _objectStorage
            .GetTextAsync(SampleData.AttachmentKey, Arg.Any<CancellationToken>())
            .Returns(SampleData.AttachmentJson.Replace("\"BKK\"", "\"XXX\""));

        var response = await _function.HandleAsync(SqsEventWith(SampleData.CalculationRequestJson, "message-1"), new TestLambdaContext());

        Assert.Equal("message-1", Assert.Single(response.BatchItemFailures).ItemIdentifier);
        await _calculationResultRepository.DidNotReceive().SaveAsync(Arg.Any<CalculationResultRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MissingParsedRecord_ReportsMessageAsFailed()
    {
        _parsedReportRepository
            .GetAsync(SampleData.FlightId, SampleData.ReportTimestamp, Arg.Any<CancellationToken>())
            .Returns((ParsedReportRecord?)null);

        var response = await _function.HandleAsync(SqsEventWith(SampleData.CalculationRequestJson, "message-1"), new TestLambdaContext());

        Assert.Equal("message-1", Assert.Single(response.BatchItemFailures).ItemIdentifier);
    }

    [Fact]
    public async Task MixedBatch_OnlyFailedMessagesAreReturnedToTheQueue()
    {
        var sqsEvent = new SQSEvent
        {
            Records =
            [
                Message("good-message", SampleData.CalculationRequestJson),
                Message("bad-message", "not json"),
            ],
        };

        var response = await _function.HandleAsync(sqsEvent, new TestLambdaContext());

        Assert.Equal("bad-message", Assert.Single(response.BatchItemFailures).ItemIdentifier);
        await _calculationResultRepository.Received(1).SaveAsync(Arg.Any<CalculationResultRecord>(), Arg.Any<CancellationToken>());
    }

    private static SQSEvent SqsEventWith(string body, string messageId = "message-id") =>
        new() { Records = [Message(messageId, body)] };

    private static SQSEvent.SQSMessage Message(string messageId, string body) =>
        new() { MessageId = messageId, Body = body };
}
