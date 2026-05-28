// Thin fetch wrapper used by every typed client. Keeps error handling,
// JSON parsing, and 204-no-content semantics in one place.
//
// Base URL defaults to "" so requests go through the Next.js rewrite in
// next.config.ts. Set NEXT_PUBLIC_API_BASE to bypass the proxy (e.g. when
// the frontend is served from a different origin than the backend).

const BASE = process.env.NEXT_PUBLIC_API_BASE ?? "";

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
    public readonly body?: unknown
  ) {
    super(message);
    this.name = "ApiError";
  }
}

export async function fetchJson<T>(
  path: string,
  init: RequestInit = {}
): Promise<T> {
  const res = await fetch(`${BASE}${path}`, {
    ...init,
    headers: {
      "Content-Type": "application/json",
      Accept: "application/json",
      ...init.headers,
    },
  });

  if (res.status === 204) {
    // 204 No Content — callers that type T as void are responsible for
    // not touching the return value. We can't construct a literal void,
    // so we coerce. Mutations that don't return JSON should declare T as
    // `void` so TS rejects accidental property access.
    return undefined as T;
  }

  const text = await res.text();
  const body = text ? safeParse(text) : undefined;

  if (!res.ok) {
    const message = extractMessage(body, res.statusText);
    throw new ApiError(res.status, message, body);
  }

  return body as T;
}

function safeParse(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return text;
  }
}

function extractMessage(body: unknown, fallback: string): string {
  if (typeof body === "string" && body.trim().length > 0) return body;
  if (body && typeof body === "object") {
    const maybe = body as Record<string, unknown>;
    if (typeof maybe.error === "string") return maybe.error;
    if (typeof maybe.message === "string") return maybe.message;
    if (typeof maybe.title === "string") return maybe.title;
  }
  return fallback;
}
