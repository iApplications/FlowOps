import type { PlatformHealth } from "@flowops/types";

export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message);
  }
}

export async function apiRequest<T>(path: string, options: RequestInit = {}): Promise<T> {
  const method = (options.method ?? "GET").toUpperCase();
  const headers = new Headers(options.headers);
  if (typeof document !== "undefined")
    headers.set("X-FlowOps-Surface", document.body.classList.contains("surface-studio") ? "Studio" : "Portal");
  if (options.body && !headers.has("Content-Type")) headers.set("Content-Type", "application/json");
  if (!["GET", "HEAD", "OPTIONS"].includes(method)) {
    const csrfResponse = await fetch("/api/auth/csrf", { signal: options.signal, headers: { "X-FlowOps-Surface": headers.get("X-FlowOps-Surface") ?? "Portal" }, credentials: "same-origin", cache: "no-store" });
    if (!csrfResponse.ok) throw new ApiError(csrfResponse.status, "Unable to start a secure request.");
    const csrf = (await csrfResponse.json()) as { requestToken: string };
    headers.set("X-CSRF-TOKEN", csrf.requestToken);
  }
  const response = await fetch(path, { ...options, method, headers, credentials: "same-origin", cache: "no-store" });
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
