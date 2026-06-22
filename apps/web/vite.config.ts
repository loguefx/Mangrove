import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// The backend defaults to port 5000 (spec §12). Override with VITE_API_TARGET if you run it
// elsewhere (e.g. http://localhost:5080 when 5000 is taken). The proxy keeps the SPA and API on
// the same origin so the httpOnly refresh cookie works in dev.
const apiTarget = process.env.VITE_API_TARGET ?? "http://localhost:5000";

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    host: true, // listen on 0.0.0.0 so the app is reachable on the LAN (private IP), not just localhost
    proxy: {
      "/api": {
        target: apiTarget,
        changeOrigin: true,
      },
    },
  },
});
