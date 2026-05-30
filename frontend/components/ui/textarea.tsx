import * as React from "react"

import { cn } from "@/lib/utils"

function Textarea({ className, ...props }: React.ComponentProps<"textarea">) {
  return (
    <textarea
      data-slot="textarea"
      className={cn(
        "flex field-sizing-content min-h-20 w-full rounded-xl border border-input bg-card/60 px-3 py-2.5 text-[14px] transition-colors outline-none placeholder:text-muted-foreground focus-visible:border-primary/40 focus-visible:bg-card focus-visible:ring-2 focus-visible:ring-primary/15 disabled:cursor-not-allowed disabled:bg-input/50 disabled:opacity-50 aria-invalid:border-destructive/60 aria-invalid:ring-2 aria-invalid:ring-destructive/20 md:text-[14px] dark:bg-white/[0.04] dark:focus-visible:bg-white/[0.08] dark:disabled:bg-input/80",
        className
      )}
      {...props}
    />
  )
}

export { Textarea }
