import { PosReportsApiClient } from './support/api-client';
import { PipelineResources } from './support/aws-resources';
import { waitFor } from './support/polling';
import { exampleReport, expectedFlightId, expectedReportTimestamp, formatPosReport } from './support/pos-reports';
import { loadStackOutputs } from './support/stack-outputs';

/**
 * Visibility timeout (90 s) x 3 receives before SQS moves the message to the DLQ, plus margin.
 */
const DEAD_LETTER_TIMEOUT_MS = 8 * 60_000;

describe('failure handling', () => {
  const outputs = loadStackOutputs();
  const api = new PosReportsApiClient(outputs.apiUrl);
  const resources = new PipelineResources(outputs);

  test(
    'a report the calculator cannot process ends up in the dead-letter queue, and the flight stays PARSED',
    async () => {
      // XXX passes format validation but is not in the airport directory, so every calculation attempt fails.
      const report = exampleReport({ destination: 'XXX' });
      const flightId = expectedFlightId(report);

      expect((await api.submitPosReport(formatPosReport(report))).status).toBe(202);

      const deadLetteredRequest = await waitFor(
        `the calculation request for ${flightId} in the dead-letter queue`,
        () => resources.takeDeadLetteredCalculationRequest(flightId),
        { timeoutMs: DEAD_LETTER_TIMEOUT_MS, intervalMs: 0 },
      );

      expect(deadLetteredRequest).toEqual({ flightId, timestamp: expectedReportTimestamp(report) });
      expect((await api.getFlightStatus(flightId)).body).toEqual({
        flightId,
        status: 'PARSED',
        timestamp: expectedReportTimestamp(report),
      });
      expect(await resources.queryCalculationResults(flightId)).toEqual([]);
    },
    DEAD_LETTER_TIMEOUT_MS + 30_000,
  );
});
