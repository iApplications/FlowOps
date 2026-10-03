import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  distDir: process.env.FLOWOPS_HTTPS_TEST === "1" ? ".next-https" : ".next",
  transpilePackages: ["@flowops/ui", "@flowops/api-client", "@flowops/types"],
  async rewrites() {
    return [{ source: "/api/:path*", destination: `${process.env.FLOWOPS_API_URL ?? "http://localhost:5050"}/api/:path*` }];
  },
};

export default nextConfig;
