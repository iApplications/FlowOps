export type PlatformHealth = {
  status: "Healthy" | "Degraded" | "Unhealthy";
  service: string;
};
