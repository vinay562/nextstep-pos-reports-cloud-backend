import { setTimeout as sleep } from 'node:timers/promises';

export interface PollOptions {
  timeoutMs: number;
  intervalMs?: number;
}

/**
 * Calls `probe` until it returns a value (not undefined) or the timeout passes.
 * The pipeline is asynchronous, so every check after submitting a report goes through here.
 */
export async function waitFor<T>(
  description: string,
  probe: () => Promise<T | undefined>,
  { timeoutMs, intervalMs = 2_000 }: PollOptions,
): Promise<T> {
  const deadline = Date.now() + timeoutMs;

  while (Date.now() < deadline) {
    const value = await probe();
    if (value !== undefined) {
      return value;
    }
    await sleep(intervalMs);
  }

  throw new Error(`Timed out after ${timeoutMs} ms waiting for ${description}.`);
}
