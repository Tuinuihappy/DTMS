"use client";

import { Bell, Cloud, Search, User } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";

interface TopBarProps {
  search: string;
  onSearchChange: (next: string) => void;
}

// Adobe Creative Cloud-style top bar: search field in the centre, action
// icons on the right. The macOS window controls in the screenshot only
// make sense in a desktop wrapper, so we skip them here. Notification +
// cloud + avatar are decorative for now (no functionality wired) — they
// telegraph "this is the place those features will live".
export function TopBar({ search, onSearchChange }: TopBarProps) {
  return (
    <div className="liquid-glass relative flex items-center gap-3 rounded-[20px] px-3 py-2">
      <div className="relative z-[2] flex-1 max-w-xl">
        <div className="relative">
          <Search
            className="pointer-events-none absolute left-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted-foreground"
            strokeWidth={2.25}
          />
          <Input
            value={search}
            onChange={(e) => onSearchChange(e.target.value)}
            placeholder="Search templates…"
            className="h-9 rounded-full border-0 bg-white/45 pl-9 text-[13px] shadow-[inset_0_0_0_1px_rgba(0,0,0,0.06)] backdrop-blur-md placeholder:text-muted-foreground/70 focus-visible:bg-white/70 focus-visible:shadow-[inset_0_0_0_1.5px_var(--primary)] focus-visible:ring-2 focus-visible:ring-primary/15 dark:bg-white/[0.04] dark:shadow-[inset_0_0_0_1px_rgba(255,255,255,0.08)] dark:focus-visible:bg-white/[0.08]"
          />
        </div>
      </div>

      <div className="relative z-[2] flex items-center gap-1.5">
        <Button
          variant="ghost"
          size="icon"
          aria-label="Notifications"
          className="liquid-puck size-9 rounded-full text-foreground hover:bg-transparent"
        >
          <Bell className="relative z-[2] h-3.5 w-3.5" strokeWidth={2.25} />
        </Button>
        <Button
          variant="ghost"
          size="icon"
          aria-label="Sync"
          className="liquid-puck size-9 rounded-full text-foreground hover:bg-transparent"
        >
          <Cloud className="relative z-[2] h-3.5 w-3.5" strokeWidth={2.25} />
        </Button>
        <div className="liquid-puck liquid-puck-primary flex size-9 items-center justify-center rounded-full">
          <User className="relative z-[2] h-3.5 w-3.5" strokeWidth={2.25} />
        </div>
      </div>
    </div>
  );
}
