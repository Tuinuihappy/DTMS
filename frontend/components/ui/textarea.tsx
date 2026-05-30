import * as React from "react"

import { cn } from "@/lib/utils"

function Textarea({ className, ...props }: React.ComponentProps<"textarea">) {
  return (
    <textarea
      data-slot="textarea"
      className={cn(
        "flex field-sizing-content min-h-20 w-full rounded-xl border-0 bg-white/45 px-3 py-2.5 text-[14px] shadow-[inset_0_0_0_1px_rgba(0,0,0,0.07),inset_0_1px_0_0_rgba(255,255,255,0.5)] backdrop-blur-md transition-all outline-none placeholder:text-muted-foreground focus-visible:bg-white/70 focus-visible:shadow-[inset_0_0_0_1.5px_var(--primary),inset_0_1px_0_0_rgba(255,255,255,0.5)] focus-visible:ring-2 focus-visible:ring-primary/15 disabled:cursor-not-allowed disabled:opacity-50 aria-invalid:shadow-[inset_0_0_0_1.5px_var(--destructive)] aria-invalid:ring-2 aria-invalid:ring-destructive/20 md:text-[14px] dark:bg-white/[0.05] dark:shadow-[inset_0_0_0_1px_rgba(255,255,255,0.08),inset_0_1px_0_0_rgba(255,255,255,0.06)] dark:focus-visible:bg-white/[0.08]",
        className
      )}
      {...props}
    />
  )
}

export { Textarea }
