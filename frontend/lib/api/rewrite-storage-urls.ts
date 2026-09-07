import "server-only";

import { STORAGE_INTERNAL_ORIGIN, STORAGE_PROXY_PREFIX } from "@/lib/api/storage-origin";

/**
 * Replaces MinIO's internal origin with this app's own storage path, anywhere
 * it appears in a backend response.
 *
 * The API signs URLs for `minio:9000` — a name only the docker network can
 * resolve — so a browser handed one verbatim would fail with no server-side
 * trace at all. Rewriting the origin turns it into a same-origin path the
 * browser can fetch, which the storage route then relays back to exactly the
 * host the signature names.
 *
 * Deliberately walks the whole payload rather than knowing the shape of the
 * four DTOs that carry these URLs today. Shape-aware rewriting is the version
 * that quietly stops working when someone adds a fifth field or a fifth route,
 * and the failure would be an internal hostname reaching a browser — silent,
 * because nothing on the server ever sees the request that fails.
 *
 * A user-typed caption containing the literal internal origin would also be
 * rewritten. That is the trade, and it is the cheaper mistake.
 */
export function rewriteStorageUrls<T>(payload: T): T {
  return walk(payload) as T;
}

/** Cheap pre-check so payloads with no storage URLs skip the walk entirely. */
export function mayContainStorageUrl(raw: string): boolean {
  return raw.includes(STORAGE_INTERNAL_ORIGIN);
}

function walk(value: unknown): unknown {
  if (typeof value === "string") {
    return value.startsWith(STORAGE_INTERNAL_ORIGIN)
      ? STORAGE_PROXY_PREFIX + value.slice(STORAGE_INTERNAL_ORIGIN.length)
      : value;
  }

  if (Array.isArray(value)) return value.map(walk);

  if (value !== null && typeof value === "object") {
    const out: Record<string, unknown> = {};
    for (const [k, v] of Object.entries(value)) out[k] = walk(v);
    return out;
  }

  return value;
}
