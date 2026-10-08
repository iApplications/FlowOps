import type { PlatformHealth } from "@flowops/types";

export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message);
  }
}

// Antiforgery tokens stay valid for the browser's CSRF cookie and signed-in identity, so one token is reused per
// surface. When sign-in or sign-out changes the identity, the API answers "Invalid CSRF token." and the request
// is retried once with a fresh token.
const csrfTokens = new Map<string, Promise<string>>();
const invalidCsrfError = "Invalid CSRF token.";

function csrfToken(surface: string): Promise<string> {
  let token = csrfTokens.get(surface);
  if (!token) {
    token = fetch("/api/auth/csrf", { headers: { "X-FlowOps-Surface": surface }, credentials: "same-origin", cache: "no-store" }).then(async response => {
      if (!response.ok) throw new ApiError(response.status, "Unable to start a secure request.");
      return ((await response.json()) as { requestToken: string }).requestToken;
    });
    token.catch(() => csrfTokens.delete(surface));
    csrfTokens.set(surface, token);
  }
  return token;
}

export async function apiRequest<T>(path: string, options: RequestInit = {}): Promise<T> {
  const method = (options.method ?? "GET").toUpperCase();
  const headers = new Headers(options.headers);
  if (typeof document !== "undefined")
    headers.set("X-FlowOps-Surface", document.body.classList.contains("surface-studio") ? "Studio" : "Portal");
  if (options.body && !(options.body instanceof FormData) && !headers.has("Content-Type")) headers.set("Content-Type", "application/json");
  const surface = headers.get("X-FlowOps-Surface") ?? "Portal";
  const mutation = !["GET", "HEAD", "OPTIONS"].includes(method);
  const send = async () => {
    if (mutation) headers.set("X-CSRF-TOKEN", await csrfToken(surface));
    return fetch(path, { ...options, method, headers, credentials: "same-origin", cache: "no-store" });
  };
  let response = await send();
  if (mutation && response.status === 400) {
    const body = await response.clone().json().catch(() => null) as { error?: string } | null;
    if (body?.error === invalidCsrfError) {
      csrfTokens.delete(surface);
      response = await send();
    }
  }
  if (!response.ok) {
    const body = await response.json().catch(() => null) as { error?: string; title?: string; errors?: Record<string, string[]> } | null;
    const validation = body?.errors && Object.values(body.errors).flat().join(" ");
    throw new ApiError(response.status, validation || body?.error || body?.title || `Request failed (${response.status}).`);
  }
  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

export async function getPlatformHealth(baseUrl: string): Promise<PlatformHealth> {
  const response = await fetch(new URL("/api/health", baseUrl), {
    cache: "no-store",
  });

  if (!response.ok) {
    throw new Error(`FlowOps API health request failed: ${response.status}`);
  }

  return (await response.json()) as PlatformHealth;
}

export async function apiDownload(path: string): Promise<Blob> {
  const surface = document.body.classList.contains("surface-studio") ? "Studio" : "Portal";
  const response = await fetch(path, { headers: { "X-FlowOps-Surface": surface }, credentials: "same-origin", cache: "no-store", signal: AbortSignal.timeout(60000) });
  if (!response.ok) {
    const body = await response.json().catch(() => null) as { error?: string } | null;
    throw new ApiError(response.status, body?.error ?? `Download failed (${response.status}).`);
  }
  return response.blob();
}
