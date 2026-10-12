using System.Text;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.TestUtilities;
using Microsoft.Extensions.Time.Testing;
using NextStep.PosReports.Infrastructure.Storage;
using NextStep.PosReports.Lambda.ReceivePosReport;
using NSubstitute;

namespace NextStep.PosReports.Lambda.Tests;

public sealed class ReceivePosReportFunctionTests
{
    private readonly IObjectStorage _objectStorage = Substitute.For<IObjectStorage>();
    private readonly Function _function;

    public ReceivePosReportFunctionTests()
    {
        _function = new Function(_objectStorage, new FakeTimeProvider(SampleData.ReceivedAt));
    }

    [Fact]
    public async Task ValidReport_StoresRawReportAndReturnsFlightId()
    {
        var response = await _function.HandleAsync(Request(SampleData.RawReport), new TestLambdaContext());

        Assert.Equal(202, response.StatusCode);
        Assert.Equal("""{"flightId":"UL20420260904RGNBKK","status":"RECEIVED"}""", response.Body);
        await _objectStorage.Received(1).PutTextAsync(
            SampleData.RawReportKey, SampleData.RawReport, "text/plain", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Base64EncodedBody_IsDecoded()
    {
        var request = new APIGatewayHttpApiV2ProxyRequest
        {
            Body = Convert.ToBase64String(Encoding.UTF8.GetBytes(SampleData.RawReport)),
            IsBase64Encoded = true,
        };

        var response = await _function.HandleAsync(request, new TestLambdaContext());

        Assert.Equal(202, response.StatusCode);
        await _objectStorage.Received(1).PutTextAsync(
            SampleData.RawReportKey, SampleData.RawReport, "text/plain", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("POS/UL204.FR RGN/TO BKK")]
    public async Task MalformedReport_Returns400WithoutStoringAnything(string? body)
    {
        var response = await _function.HandleAsync(Request(body), new TestLambdaContext());

        Assert.Equal(400, response.StatusCode);
        Assert.Contains("\"message\":", response.Body);
        await _objectStorage.DidNotReceive().PutTextAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static APIGatewayHttpApiV2ProxyRequest Request(string? body) => new() { Body = body };
}
