export interface ApiResponse<T = Record<string, unknown>> {
  status: number;
  body: T;
}

export interface FlightStatusBody {
  flightId: string;
  status: string;
  timestamp: string;
  remainingFlightTimeMinutes?: number;
  estimatedFuelAtArrivalKg?: number;
  lowFuelWarning?: boolean;
}

export class PosReportsApiClient {
  constructor(private readonly apiUrl: string) {}

  public async submitPosReport(rawReport: string): Promise<ApiResponse> {
    const response = await fetch(`${this.apiUrl}/pos-reports`, {
      method: 'POST',
      headers: { 'Content-Type': 'text/plain' },
      body: rawReport,
    });
    return { status: response.status, body: (await response.json()) as Record<string, unknown> };
  }

  public async getFlightStatus(flightId: string): Promise<ApiResponse<FlightStatusBody>> {
    const response = await fetch(`${this.apiUrl}/status/${encodeURIComponent(flightId)}`);
    return { status: response.status, body: (await response.json()) as FlightStatusBody };
  }
}
