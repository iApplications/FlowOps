import type { PlatformHealth } from "@flowops/types";

export async function getPlatformHealth(baseUrl: string): Promise<PlatformHealth> {
  const response = await fetch(new URL("/api/health", baseUrl), {
    cache: "no-store",
  });

  if (!response.ok) {
    throw new Error(`FlowOps API health request failed: ${response.status}`);
  }

  return (await response.json()) as PlatformHealth;
}
