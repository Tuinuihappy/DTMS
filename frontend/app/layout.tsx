import type { Metadata } from "next";
import { Google_Sans, Geist_Mono } from "next/font/google";
import { Toaster } from "@/components/ui/sonner";
import { Providers } from "./providers";
import "./globals.css";

// Google Sans (the real one — Google added it to Google Fonts in 2025
// under SIL OFL). next/font/google downloads it at build time and self-
// hosts inside the standalone output, so the production server never
// calls fonts.googleapis.com. Weights chosen to cover everything the UI
// uses: 400 body, 500 hover/labels, 600 headings, 700 emphasis.
const googleSans = Google_Sans({
  variable: "--font-sans",
  subsets: ["latin"],
  weight: ["400", "500", "600", "700"],
  display: "swap",
});

// Keep Geist Mono for inline parameter values (LIFT_PALLET id=4 p0=1).
// It pairs cleanly with Google Sans and we already use it everywhere
// `font-mono` appears in the UI.
const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
  display: "swap",
});

export const metadata: Metadata = {
  title: "DTMS Templates",
  description:
    "Compose ActionTemplate recipes and OrderTemplate plans for the RIOT3 dispatcher.",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html
      lang="en"
      className={`${googleSans.variable} ${geistMono.variable} h-full antialiased`}
    >
      {/* body's background gradient lives in globals.css so it composes
          with the foreground tokens; we just stretch and stack here. */}
      <body className="min-h-full flex flex-col text-foreground">
        <Providers>{children}</Providers>
        <Toaster richColors position="bottom-right" />
      </body>
    </html>
  );
}
