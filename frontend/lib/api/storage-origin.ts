import "server-only";

/**
 * Where MinIO actually lives, from inside the docker network.
 *
 * This is config, but not the kind that broke: it names an internal topology
 * that no client ever has to reach. The value it replaces — `PublicEndpoint` —
 * had to be an address every browser and tablet could resolve, which is a value
 * that does not exist. It was a LAN IP, DHCP moved it, and uploads failed for
 * days with nothing in any log because the request never left the browser.
 */
export const STORAGE_INTERNAL_ORIGIN = (
  process.env.STORAGE_INTERNAL_ORIGIN ?? "http://minio:9000"
).replace(/\/$/, "");

/** Path prefix the browser talks to. Everything under it is relayed to MinIO. */
export const STORAGE_PROXY_PREFIX = "/api/storage";
