import { randomInt } from 'node:crypto';

export interface PosReportFields {
  flightNumber: string;
  departure: string;
  destination: string;
  day: number;
  hour: number;
  minute: number;
  position: string;
  groundSpeedKnots: number;
  fuelOnBoardKg: number;
  fuelFlowKgPerHour: number;
}

const pad = (value: number) => value.toString().padStart(2, '0');

/** A random flight number so every test run works on its own flight ids. */
export function uniqueFlightNumber(): string {
  return `NS${randomInt(1000, 10000)}`;
}

/**
 * The specification example, moved to today (UTC) so the day is valid for the month the API receives it in.
 */
export function exampleReport(overrides: Partial<PosReportFields> = {}): PosReportFields {
  return {
    flightNumber: uniqueFlightNumber(),
    departure: 'RGN',
    destination: 'BKK',
    day: new Date().getUTCDate(),
    hour: 12,
    minute: 5,
    position: 'N1642.3E09612.5',
    groundSpeedKnots: 450,
    fuelOnBoardKg: 12500,
    fuelFlowKgPerHour: 2800,
    ...overrides,
  };
}

export function formatPosReport(report: PosReportFields): string {
  return [
    'POS',
    `${report.flightNumber}.FR ${report.departure}`,
    `TO ${report.destination}`,
    `${pad(report.day)}${pad(report.hour)}${pad(report.minute)}`,
    report.position,
    report.groundSpeedKnots,
    report.fuelOnBoardKg,
    report.fuelFlowKgPerHour,
  ].join('/');
}

/** flightId = flight number + yyyyMMdd (current UTC year/month, report day) + departure + destination. */
export function expectedFlightId(report: PosReportFields): string {
  const now = new Date();
  return `${report.flightNumber}${now.getUTCFullYear()}${pad(now.getUTCMonth() + 1)}${pad(report.day)}${report.departure}${report.destination}`;
}

/** Report time as stored by the parser, e.g. 2026-09-04T12:05:00Z. */
export function expectedReportTimestamp(report: PosReportFields): string {
  const now = new Date();
  return `${now.getUTCFullYear()}-${pad(now.getUTCMonth() + 1)}-${pad(report.day)}T${pad(report.hour)}:${pad(report.minute)}:00Z`;
}
