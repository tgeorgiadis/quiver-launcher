/** A request the catalog, or a repository host, refused; the status and the message for the player. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
  ) {
    super(message);
  }
}
