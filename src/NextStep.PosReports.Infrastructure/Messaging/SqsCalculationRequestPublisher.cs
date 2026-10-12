using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using NextStep.PosReports.Infrastructure.Serialization;

namespace NextStep.PosReports.Infrastructure.Messaging;

public interface ICalculationRequestPublisher
{
    Task PublishAsync(CalculationRequest request, CancellationToken cancellationToken = default);
}

public sealed class SqsCalculationRequestPublisher(IAmazonSQS sqsClient, string queueUrl) : ICalculationRequestPublisher
{
    public async Task PublishAsync(CalculationRequest request, CancellationToken cancellationToken = default)
    {
        var messageBody = JsonSerializer.Serialize(request, JsonDefaults.Options);

        await sqsClient.SendMessageAsync(new SendMessageRequest(queueUrl, messageBody), cancellationToken);
    }
}
