using Amazon.Lambda.S3Events;
using Amazon.Lambda.TestUtilities;
using NextStep.PosReports.Infrastructure.Messaging;
using NextStep.PosReports.Infrastructure.Persistence;
using NextStep.PosReports.Infrastructure.Storage;
using NextStep.PosReports.Lambda.ParsePosReport;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace NextStep.PosReports.Lambda.Tests;

public sealed class ParsePosReportFunctionTests
{
    /// <summary>S3 event notifications deliver the key URL-encoded.</summary>
    private const string EncodedRawReportKey = "pos/UL20420260904RGNBKK-2026-09-04T15%3A12%3A30.123Z";

    private readonly IObjectStorage _objectStorage = Substitute.For<IObjectStorage>();
    private readonly IParsedReportRepository _parsedReportRepository = Substitute.For<IParsedReportRepository>();
    private readonly ICalculationRequestPublisher _calculationRequestPublisher = Substitute.For<ICalculationRequestPublisher>();
    private readonly Function _function;

    public ParsePosReportFunctionTests()
    {
        _function = new Function(_objectStorage, _parsedReportRepository, _calculationRequestPublisher);
        _objectStorage.GetTextAsync(SampleData.RawReportKey, Arg.Any<CancellationToken>()).Returns(SampleData.RawReport);
    }

    [Fact]
    public async Task NewRawReport_StoresAttachmentRecordsItAndQueuesCalculation()
    {
        await _function.HandleAsync(S3EventFor(EncodedRawReportKey), new TestLambdaContext());

        await _objectStorage.Received(1).PutTextAsync(
            SampleData.AttachmentKey, SampleData.AttachmentJson, "application/json", Arg.Any<CancellationToken>());
        await _parsedReportRepository.Received(1).SaveAsync(
            new ParsedReportRecord(SampleData.FlightId, SampleData.ReportTimestamp, SampleData.AttachmentKey),
            Arg.Any<CancellationToken>());
        await _calculationRequestPublisher.Received(1).PublishAsync(
            new CalculationRequest(SampleData.FlightId, SampleData.ReportTimestamp),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task YearAndMonth_ComeFromTheReceiveTimeInTheKey()
    {
        // Received in January 2027: the flight id must use 2027-01 even if parsing happens later.
        const string januaryKey = "pos/UL20420270104RGNBKK-2027-01-04T15:12:30.123Z";
        _objectStorage.GetTextAsync(januaryKey, Arg.Any<CancellationToken>()).Returns(SampleData.RawReport);

        await _function.HandleAsync(S3EventFor(januaryKey), new TestLambdaContext());

        await _calculationRequestPublisher.Received(1).PublishAsync(
            new CalculationRequest("UL20420270104RGNBKK", new DateTimeOffset(2027, 1, 4, 12, 5, 0, TimeSpan.Zero)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StorageFailure_PropagatesSoLambdaRetriesAndNothingIsQueued()
    {
        _objectStorage.GetTextAsync(SampleData.RawReportKey, Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("S3 unavailable"));

        await Assert.ThrowsAsync<IOException>(
            () => _function.HandleAsync(S3EventFor(EncodedRawReportKey), new TestLambdaContext()));

        await _calculationRequestPublisher.DidNotReceive().PublishAsync(Arg.Any<CalculationRequest>(), Arg.Any<CancellationToken>());
    }

    private static S3Event S3EventFor(string objectKey) => new()
    {
        Records =
        [
            new S3Event.S3EventNotificationRecord
            {
                S3 = new S3Event.S3Entity { Object = new S3Event.S3ObjectEntity { Key = objectKey } },
            },
        ],
    };
}
