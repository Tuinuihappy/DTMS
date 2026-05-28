"use client";

import { useState, type ReactNode } from "react";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "@/components/ui/alert-dialog";

interface ConfirmDestructiveProps {
  // Element rendered as the trigger. base-ui will merge its trigger
  // behaviour into this node — pass a Button (or anything button-like)
  // rather than wrapping it.
  trigger: ReactNode;
  title: string;
  description?: string;
  confirmLabel?: string;
  onConfirm: () => void | Promise<void>;
}

// Single reusable destructive confirmation. Busy state stays local so a
// slow DELETE doesn't render twice during the round-trip.
export function ConfirmDestructive({
  trigger,
  title,
  description,
  confirmLabel = "Delete",
  onConfirm,
}: ConfirmDestructiveProps) {
  const [open, setOpen] = useState(false);
  const [pending, setPending] = useState(false);

  async function handleConfirm(e: React.MouseEvent) {
    e.preventDefault();
    setPending(true);
    try {
      await onConfirm();
      setOpen(false);
    } finally {
      setPending(false);
    }
  }

  return (
    <AlertDialog open={open} onOpenChange={setOpen}>
      {/* base-ui's Trigger accepts a `render` prop instead of asChild.
          When passed a JSX element, base-ui clones it and attaches the
          required listeners + aria. */}
      <AlertDialogTrigger render={trigger as React.ReactElement} />
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{title}</AlertDialogTitle>
          {description ? (
            <AlertDialogDescription>{description}</AlertDialogDescription>
          ) : null}
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={pending}>Cancel</AlertDialogCancel>
          <AlertDialogAction
            variant="destructive"
            onClick={handleConfirm}
            disabled={pending}
          >
            {pending ? "Working..." : confirmLabel}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
