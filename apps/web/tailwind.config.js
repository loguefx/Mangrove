/** @type {import('tailwindcss').Config} */
export default {
  content: ["./index.html", "./src/**/*.{ts,tsx}"],
  darkMode: "class",
  theme: {
    extend: {
      colors: {
        // Mangrove palette (spec §10, §16)
        teal: {
          DEFAULT: "#0D9488",
          deep: "#0F3D38",
          mint: "#2DD4BF",
          ink: "#0B2D2A",
        },
      },
      fontFamily: {
        sans: ["Inter", "Segoe UI", "system-ui", "sans-serif"],
      },
      borderRadius: {
        "2xl": "1rem",
      },
    },
  },
  plugins: [],
};
