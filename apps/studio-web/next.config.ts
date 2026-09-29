import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  transpilePackages: ["@flowops/ui", "@flowops/api-client", "@flowops/types"],
};

export default nextConfig;
