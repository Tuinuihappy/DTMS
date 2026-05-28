"use client";

import { useState, type ReactNode } from "react";
import {
  QueryClient,
  QueryClientProvider,
  type QueryClientConfig,
} from "@tanstack/react-query";
import { ReactQueryDevtools } from "@tanstack/react-query-devtools";
import { TooltipProvider } from "@/components/ui/tooltip";

const queryClientConfig: QueryClientConfig = {
  defaultOptions: {
    queries: {
      // 30s feels right for template catalogs — they don't change often.
      // Switching tabs / opening a dialog re-renders without refetching.
      staleTime: 30_000,
      // Retry once on transient backend hiccups; surface real errors fast.
      retry: 1,
      refetchOnWindowFocus: false,
    },
    mutations: {
      retry: 0,
    },
  },
};

export function Providers({ children }: { children: ReactNode }) {
  // Per TanStack docs: create the client lazily in state so that hot-reload
  // in development doesn't tear down the cache on every save.
  const [client] = useState(() => new QueryClient(queryClientConfig));

  return (
    <QueryClientProvider client={client}>
      <TooltipProvider delay={150}>{children}</TooltipProvider>
      {process.env.NODE_ENV === "development" ? (
        <ReactQueryDevtools initialIsOpen={false} buttonPosition="bottom-right" />
      ) : null}
    </QueryClientProvider>
  );
}
