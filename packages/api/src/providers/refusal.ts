import { ApiError } from "../errors";

/**
 * Why a release host refused, worded for players. Without a token each allows
 * a network fewer requests, and its answer says when that starts again, in
 * `ratelimit-*` headers (GitHub's are prefixed `x-`).
 */
export function refusal(response: Response, token: boolean, name: string, prefix: "x-ratelimit" | "ratelimit"): ApiError {
  const { status, headers } = response;
  if (status === 401 && token) return new ApiError(status, `${name} didn't accept your ${name} token. Change or remove it in Settings.`);
  if (status === 403 || status === 429) {
    const reset = Number(headers.get(`${prefix}-reset`));
    if (headers.get(`${prefix}-remaining`) === "0" && reset > 0) {
      const at = new Date(reset * 1000).toLocaleTimeString([], { hour: "numeric", minute: "2-digit" });
      return new ApiError(
        status,
        token
          ? `Your ${name} token's limit of requests is used up until ${at}. Try again then.`
          : `${name}'s limit of requests from this network is used up until ${at}. Try again then, or add a ${name} token in Settings to raise the limit.`,
      );
    }
    const wait = Number(headers.get("retry-after"));
    if (wait > 0) return new ApiError(status, `${name} is limiting requests from this network. Try again in ${Math.ceil(wait / 60)} min.`);
  }
  return new ApiError(status, `${name} answered ${status}. Try again later.`);
}
