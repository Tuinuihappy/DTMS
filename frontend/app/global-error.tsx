"use client";

import { useEffect } from "react";

export default function GlobalError({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  useEffect(() => {
    console.error("[global-error]", error);
  }, [error]);

  return (
    <html lang="en">
      <body
        style={{
          minHeight: "100vh",
          margin: 0,
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          fontFamily:
            "system-ui, -apple-system, Segoe UI, Roboto, sans-serif",
          background: "#0b1020",
          color: "#e6e9f2",
          padding: "24px",
        }}
      >
        <div style={{ maxWidth: 420, width: "100%", textAlign: "center" }}>
          <div
            style={{
              width: 56,
              height: 56,
              borderRadius: 16,
              margin: "0 auto 20px",
              display: "grid",
              placeItems: "center",
              background: "rgba(255, 99, 99, 0.12)",
              color: "#ff6b6b",
              fontSize: 28,
            }}
          >
            !
          </div>
          <h1
            style={{
              fontSize: 20,
              fontWeight: 600,
              margin: "0 0 8px",
            }}
          >
            The app crashed
          </h1>
          <p
            style={{
              fontSize: 14,
              color: "rgba(230, 233, 242, 0.6)",
              margin: "0 0 6px",
            }}
          >
            {error.message || "An unexpected error occurred."}
          </p>
          {error.digest && (
            <p
              style={{
                fontSize: 11,
                fontFamily: "ui-monospace, SF Mono, Consolas, monospace",
                color: "rgba(230, 233, 242, 0.35)",
                margin: "0 0 20px",
              }}
            >
              ref: {error.digest}
            </p>
          )}
          <div
            style={{
              display: "flex",
              gap: 8,
              justifyContent: "center",
              marginTop: 20,
            }}
          >
            <button
              onClick={reset}
              style={{
                background: "#e6e9f2",
                color: "#0b1020",
                border: "none",
                borderRadius: 999,
                padding: "10px 18px",
                fontSize: 14,
                fontWeight: 500,
                cursor: "pointer",
              }}
            >
              Reload
            </button>
            <button
              onClick={() => (window.location.href = "/")}
              style={{
                background: "transparent",
                color: "#e6e9f2",
                border: "1px solid rgba(230, 233, 242, 0.2)",
                borderRadius: 999,
                padding: "10px 18px",
                fontSize: 14,
                fontWeight: 500,
                cursor: "pointer",
              }}
            >
              Home
            </button>
          </div>
        </div>
      </body>
    </html>
  );
}
