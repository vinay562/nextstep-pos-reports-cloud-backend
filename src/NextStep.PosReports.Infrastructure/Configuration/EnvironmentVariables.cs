namespace NextStep.PosReports.Infrastructure.Configuration;

/// <summary>Environment variable names set on the Lambda functions by the CDK stack.</summary>
public static class EnvironmentVariables
{
    public const string BucketName = "BUCKET_NAME";
    public const string ParsedReportsTableName = "PARSED_REPORTS_TABLE_NAME";
    public const string CalculationResultsTableName = "CALCULATION_RESULTS_TABLE_NAME";
    public const string CalculationQueueUrl = "CALCULATION_QUEUE_URL";

    public static string GetRequired(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Required environment variable '{name}' is not set.");
}
