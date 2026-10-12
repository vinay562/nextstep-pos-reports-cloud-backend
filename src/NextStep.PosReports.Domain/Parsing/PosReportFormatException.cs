namespace NextStep.PosReports.Domain.Parsing;

/// <summary>The POS report text does not match the expected format. The message is safe to return to API callers.</summary>
public sealed class PosReportFormatException(string message) : Exception(message);
