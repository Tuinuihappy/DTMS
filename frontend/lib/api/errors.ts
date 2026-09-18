// No "use client": this is a plain error type and a Response reader, and the
// BFF's own route handlers may want it too.

/**
 * Thrown when the API refuses a request because the caller has reached their
 * request limit. Carries the server's own wait, so a poller can back off by
 * exactly as long as it is told rather than guessing.
 *
 * Quotas belong to a caller, not to a page, so every screen a person has open
 * spends the same budget — which is why a 429 has to slow a client down
 * instead of being retried at the same rate.
 */
export class RateLimitError extends Error {
  readonly retryAfterSeconds: number;

  constructor(message: string, retryAfterSeconds: number) {
    super(message);
    this.name = "RateLimitError";
    this.retryAfterSeconds = retryAfterSeconds;
  }
}

/** Default wait when a 429 arrives without a usable Retry-After. */
const FALLBACK_RETRY_AFTER_SECONDS = 30;

/**
 * Turns a 429 into a {@link RateLimitError}, reading the message the API wrote
 * and the wait it asked for. Returns for every other status, so callers keep
 * their own error handling.
 */
export async function throwIfRateLimited(res: Response): Promise<void> {
  if (res.status !== 429) return;

  const header = Number(res.headers.get("retry-after"));
  let message = "";
  let fromBody = 0;

  try {
    const body = (await res.clone().json()) as {
      message?: string;
      detail?: string;
      retryAfterSeconds?: number;
    };
    message = body?.message ?? body?.detail ?? "";
    fromBody = Number(body?.retryAfterSeconds) || 0;
  } catch {
    // A 429 from anything but our own API may not carry JSON.
  }

  const seconds =
    [header, fromBody].find((n) => Number.isFinite(n) && n > 0) ??
    FALLBACK_RETRY_AFTER_SECONDS;

  throw new RateLimitError(
    message || `Too many requests. Try again in ${seconds} seconds.`,
    seconds,
  );
}
