import path from "node:path";
import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  reactStrictMode: true,

  // Standalone output bundles only the files the server actually needs
  // (traced by @vercel/nft) so the Docker runner stage can ship without
  // node_modules and stay small.
  output: "standalone",

  turbopack: {
    // Pin the workspace root explicitly so Turbopack never walks up into
    // the parent .NET monorepo when a stray lockfile shows up there.
    root: path.resolve(__dirname),
  },
};

export default nextConfig;
