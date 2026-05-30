import * as React from "react"
import { Input as InputPrimitive } from "@base-ui/react/input"

import { cn } from "@/lib/utils"

function Input({ className, type, ...props }: React.ComponentProps<"input">) {
  return (
    <InputPrimitive
      type={type}
      data-slot="input"
      className={cn(
        // Apple-style: taller (h-9), rounded-xl, hairline border, primary
        // focus ring. shadcn's defaults are too tight/rectangular for
        // the new chrome — these adjustments echo iOS form fields.
        "h-9 w-full min-w-0 rounded-xl border border-input bg-card/60 px-3 py-1.5 text-[14px] transition-colors outline-none file:inline-flex file:h-7 file:border-0 file:bg-transparent file:text-sm file:font-medium file:text-foreground placeholder:text-muted-foreground focus-visible:border-primary/40 focus-visible:bg-card focus-visible:ring-2 focus-visible:ring-primary/15 disabled:pointer-events-none disabled:cursor-not-allowed disabled:bg-input/50 disabled:opacity-50 aria-invalid:border-destructive/60 aria-invalid:ring-2 aria-invalid:ring-destructive/20 md:text-[14px] dark:bg-white/[0.04] dark:focus-visible:bg-white/[0.08] dark:disabled:bg-input/80",
        className
      )}
      {...props}
    />
  )
}

export { Input }
