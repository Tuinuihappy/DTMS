import * as React from "react"
import { Input as InputPrimitive } from "@base-ui/react/input"

import { cn } from "@/lib/utils"

function Input({ className, type, ...props }: React.ComponentProps<"input">) {
  return (
    <InputPrimitive
      type={type}
      data-slot="input"
      className={cn(
        // Liquid Glass surface — translucent fill, hairline border via
        // box-shadow inset, primary-tinted focus ring. The bg is white/40
        // (light) / white/4 (dark) so it picks up the panel behind it.
        "h-9 w-full min-w-0 rounded-xl border-0 bg-white/45 px-3 py-1.5 text-[14px] shadow-[inset_0_0_0_1px_rgba(0,0,0,0.07),inset_0_1px_0_0_rgba(255,255,255,0.5)] backdrop-blur-md transition-all outline-none file:inline-flex file:h-7 file:border-0 file:bg-transparent file:text-sm file:font-medium file:text-foreground placeholder:text-muted-foreground focus-visible:bg-white/70 focus-visible:shadow-[inset_0_0_0_1.5px_var(--primary),inset_0_1px_0_0_rgba(255,255,255,0.5)] focus-visible:ring-2 focus-visible:ring-primary/15 disabled:pointer-events-none disabled:cursor-not-allowed disabled:opacity-50 aria-invalid:shadow-[inset_0_0_0_1.5px_var(--destructive)] aria-invalid:ring-2 aria-invalid:ring-destructive/20 md:text-[14px] dark:bg-white/[0.05] dark:shadow-[inset_0_0_0_1px_rgba(255,255,255,0.08),inset_0_1px_0_0_rgba(255,255,255,0.06)] dark:focus-visible:bg-white/[0.08]",
        className
      )}
      {...props}
    />
  )
}

export { Input }
