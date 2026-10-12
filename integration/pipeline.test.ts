import { FlightStatusBody, PosReportsApiClient } from './support/api-client';
import { PipelineResources } from './support/aws-resources';
import { waitFor } from './support/polling';
import { exampleReport, expectedFlightId, expectedReportTimestamp, formatPosReport } from './support/pos-reports';
import { loadStackOutputs } from './support/stack-outputs';

const PIPELINE_TIMEOUT_MS = 90_000;

describe('POS report pipeline (end to end)', () => {
  const outputs = loadStackOutputs();
  const api = new PosReportsApiClient(outputs.apiUrl);
  const resources = new PipelineResources(outputs);

  const waitForCalculatedStatus = (flightId: string) =>
    waitFor(
      `a calculated status for ${flightId}`,
      async () => {
        const response = await api.getFlightStatus(flightId);
        return response.status === 200 && response.body.remainingFlightTimeMinutes !== undefined ? response.body : undefined;
      },
      { timeoutMs: PIPELINE_TIMEOUT_MS },
    );

  describe('the specification example', () => {
    const report = exampleReport();
    const rawReport = formatPosReport(report);
    const flightId = expectedFlightId(report);
    const reportTimestamp = expectedReportTimestamp(report);
    const attachmentKey = `attachment/${flightId}-${reportTimestamp}.json`;

    let finalStatus: FlightStatusBody;

    beforeAll(async () => {
      const response = await api.submitPosReport(rawReport);
      expect(response).toEqual({ status: 202, body: { flightId, status: 'RECEIVED' } });

      finalStatus = await waitForCalculatedStatus(flightId);
    }, PIPELINE_TIMEOUT_MS + 10_000);

    test('status API returns the calculated result', () => {
      expect(finalStatus).toEqual({
        flightId,
        status: 'CALCULATED',
        timestamp: expect.stringMatching(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/),
        remainingFlightTimeMinutes: 43,
        estimatedFuelAtArrivalKg: 10513,
        lowFuelWarning: false,
      });
    });

    test('raw report is stored unchanged under pos/{flightId}-{receivedAt}', async () => {
      const keys = await resources.listObjectKeys(`pos/${flightId}-`);

      expect(keys).toHaveLength(1);
      expect(keys[0]).toMatch(new RegExp(`^pos/${flightId}-\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}\\.\\d{3}Z$`));
      expect(await resources.readObject(keys[0])).toBe(rawReport);
    });

    test('parsed report is stored under attachment/', async () => {
      const attachment = JSON.parse(await resources.readObject(attachmentKey));

      expect(attachment).toEqual({
        flightId,
        flightNumber: report.flightNumber,
        departure: 'RGN',
        destination: 'BKK',
        timestamp: reportTimestamp,
        latitude: 16.705,
        longitude: 96.2083,
        groundSpeedKnots: 450,
        fuelOnBoardKg: 12500,
        fuelFlowKgPerHour: 2800,
      });
    });

    test('parsed-reports table points to the attachment', async () => {
      expect(await resources.queryParsedReports(flightId)).toEqual([
        { flightId, timestamp: reportTimestamp, attachment: attachmentKey },
      ]);
    });

    test('calculation result is stored under results/ with its inputs and recorded in its own table', async () => {
      const [resultItem] = await resources.queryCalculationResults(flightId);
      const resultKey = `results/${flightId}-${finalStatus.timestamp}.json`;

      expect(resultItem).toEqual({
        flightId,
        timestamp: finalStatus.timestamp,
        attachment: resultKey,
        status: 'CALCULATED',
        remainingFlightTimeMinutes: 43,
        estimatedFuelAtArrivalKg: 10513,
        lowFuelWarning: false,
      });

      expect(JSON.parse(await resources.readObject(resultKey))).toEqual({
        flightId,
        timestamp: finalStatus.timestamp,
        input: {
          currentLatitude: 16.705,
          currentLongitude: 96.2083,
          destination: 'BKK',
          groundSpeedKnots: 450,
          fuelOnBoardKg: 12500,
          fuelFlowKgPerHour: 2800,
        },
        remainingFlightTimeMinutes: 43,
        estimatedFuelAtArrivalKg: 10513,
        lowFuelWarning: false,
      });
    });
  });

  test('a flight without enough fuel is marked LOW_FUEL_WARNING', async () => {
    const report = exampleReport({ fuelOnBoardKg: 1000 });
    const flightId = expectedFlightId(report);

    expect((await api.submitPosReport(formatPosReport(report))).status).toBe(202);
    const status = await waitForCalculatedStatus(flightId);

    expect(status.status).toBe('LOW_FUEL_WARNING');
    expect(status.lowFuelWarning).toBe(true);
    expect(status.estimatedFuelAtArrivalKg).toBeLessThan(0);
  }, PIPELINE_TIMEOUT_MS + 10_000);

  test('with several reports for one flight, the status is the most recent calculation', async () => {
    const firstReport = exampleReport({ hour: 12, minute: 5 });
    const secondReport = { ...firstReport, minute: 20, position: 'N1530.0E09830.0' };
    const flightId = expectedFlightId(firstReport);

    expect((await api.submitPosReport(formatPosReport(firstReport))).body.flightId).toBe(flightId);
    expect((await api.submitPosReport(formatPosReport(secondReport))).body.flightId).toBe(flightId);

    const results = await waitFor(
      `two calculation results for ${flightId}`,
      async () => {
        const items = await resources.queryCalculationResults(flightId);
        return items.length === 2 ? items : undefined;
      },
      { timeoutMs: PIPELINE_TIMEOUT_MS },
    );

    const latestResult = results.reduce((latest, item) =>
      (item.timestamp as string) > (latest.timestamp as string) ? item : latest,
    );
    const status = await api.getFlightStatus(flightId);

    expect(status.body.timestamp).toBe(latestResult.timestamp);
    expect(status.body.remainingFlightTimeMinutes).toBe(latestResult.remainingFlightTimeMinutes);
  }, PIPELINE_TIMEOUT_MS + 10_000);
});
