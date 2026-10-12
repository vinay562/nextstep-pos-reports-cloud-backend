namespace NextStep.PosReports.Domain.Models;

/// <summary>A position in decimal degrees. South and West are negative.</summary>
public readonly record struct GeoCoordinate(double Latitude, double Longitude);
