import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      "/graphql": {
        target: "http://localhost:5080",
        timeout: 900_000,
        proxyTimeout: 900_000,
      },
      "/health": "http://localhost:5080",
    },
  },
});
