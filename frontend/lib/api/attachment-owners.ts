// The owner kinds an image can belong to (ADR-019), shared by the client and the
// Next routes. No "use client" here: the routes run on the server and must be
// able to import this without pulling in the browser-only upload code.
//
// Each value is also the backend's path segment. The API registers every
// attachment route once per owner kind, so the kind picks which permission
// guards the request — a value outside this list has no route to reach.

export const ATTACHMENT_OWNERS = ["carrier", "carrier-type", "maintenance"] as const;

export type AttachmentOwner = (typeof ATTACHMENT_OWNERS)[number];

export const isAttachmentOwner = (value: unknown): value is AttachmentOwner =>
  typeof value === "string" && (ATTACHMENT_OWNERS as readonly string[]).includes(value);

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** Checked before an id goes into a backend path, where it must be a bare GUID. */
export const isGuid = (value: unknown): value is string =>
  typeof value === "string" && GUID.test(value);

/** The backend path for everything under one owner. Inputs must already be validated. */
export const ownerPath = (owner: AttachmentOwner, ownerId: string) =>
  `/api/v1/fleet/attachments/${owner}/${ownerId}`;
