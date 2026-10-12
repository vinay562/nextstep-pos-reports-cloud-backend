import { PosReportsApiClient } from './support/api-client';
import { exampleReport, formatPosReport } from './support/pos-reports';
import { loadStackOutputs } from './support/stack-outputs';

describe('API validation', () => {
  const api = new PosReportsApiClient(loadStackOutputs().apiUrl);
  const validReport = formatPosReport(exampleReport());

  test.each([
    ['an empty body', ''],
    ['a body without the POS/ prefix', validReport.replace('POS/', '')],
    ['too few parts', validReport.split('/').slice(0, 5).join('/')],
    ['a day that does not exist', formatPosReport(exampleReport({ day: 0 }))],
    ['an invalid time', formatPosReport(exampleReport({ hour: 25 }))],
    ['position minutes of 60 or more', formatPosReport(exampleReport({ position: 'N1660.0E09612.5' }))],
    ['a ground speed of zero', formatPosReport(exampleReport({ groundSpeedKnots: 0 }))],
  ])('POST /pos-reports with %s returns 400 and a reason', async (_description, body) => {
    const response = await api.submitPosReport(body);

    expect(response.status).toBe(400);
    expect(response.body.message).toEqual(expect.any(String));
  });

  test('GET /status with a malformed flight id returns 400', async () => {
    const response = await api.getFlightStatus('not-a-flight-id');

    expect(response.status).toBe(400);
    expect(response.body).toEqual({ message: expect.any(String) });
  });

  test('GET /status for an unknown flight returns 404', async () => {
    const response = await api.getFlightStatus('XX000020260101AAABBB');

    expect(response.status).toBe(404);
    expect(response.body).toEqual({ message: "No status found for flight 'XX000020260101AAABBB'." });
  });
});
