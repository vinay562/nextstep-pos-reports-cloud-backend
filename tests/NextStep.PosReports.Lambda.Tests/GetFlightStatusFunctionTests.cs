using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.TestUtilities;
using NextStep.PosReports.Domain.Models;
using NextStep.PosReports.Infrastructure.Persistence;
using NextStep.PosReports.Lambda.GetFlightStatus;
using NSubstitute;

namespace NextStep.PosReports.Lambda.Tests;

public sealed class GetFlightStatusFunctionTests
{
    private readonly ICalculationResultRepository _calculationResultRepository = Substitute.For<ICalculationResultRepository>();
    private readonly IParsedReportRepository _parsedReportRepository = Substitute.For<IParsedReportRepository>();
    private readonly Function _function;

    public GetFlightStatusFunctionTests()
    {
        _function = new Function(_calculationResultRepository, _parsedReportRepository);
    }

    [Fact]
    public async Task CalculatedFlight_ReturnsLatestCalculation()
    {
        _calculationResultRepository
            .GetLatestAsync(SampleData.FlightId, Arg.Any<CancellationToken>())
            .Returns(new CalculationResultRecord(
                SampleData.FlightId, SampleData.CalculatedAt, SampleData.ResultKey, FlightStatus.Calculated, 43, 10513, false));

        var response = await _function.HandleAsync(RequestFor(SampleData.FlightId), new TestLambdaContext());

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(
            """{"flightId":"UL20420260904RGNBKK","status":"CALCULATED","timestamp":"2026-09-04T12:06:15.842Z","remainingFlightTimeMinutes":43,"estimatedFuelAtArrivalKg":10513,"lowFuelWarning":false}""",
            response.Body);
        await _parsedReportRepository.DidNotReceive().GetLatestAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ParsedButNotCalculated_ReturnsParsedStatus()
    {
        _parsedReportRepository
            .GetLatestAsync(SampleData.FlightId, Arg.Any<CancellationToken>())
            .Returns(new ParsedReportRecord(SampleData.FlightId, SampleData.ReportTimestamp, SampleData.AttachmentKey));

        var response = await _function.HandleAsync(RequestFor(SampleData.FlightId), new TestLambdaContext());

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("""{"flightId":"UL20420260904RGNBKK","status":"PARSED","timestamp":"2026-09-04T12:05:00Z"}""", response.Body);
    }

    [Fact]
    public async Task UnknownFlight_Returns404()
    {
        var response = await _function.HandleAsync(RequestFor("XX1234"), new TestLambdaContext());

        Assert.Equal(404, response.StatusCode);
        Assert.Equal("""{"message":"No status found for flight 'XX1234'."}""", response.Body);
    }

    [Fact]
    public async Task MissingFlightId_Returns400()
    {
        var response = await _function.HandleAsync(new APIGatewayHttpApiV2ProxyRequest(), new TestLambdaContext());

        Assert.Equal(400, response.StatusCode);
    }

    private static APIGatewayHttpApiV2ProxyRequest RequestFor(string flightId) =>
        new() { PathParameters = new Dictionary<string, string> { ["flightId"] = flightId } };
}
