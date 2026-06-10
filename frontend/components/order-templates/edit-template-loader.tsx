"use client";

import { AlertTriangle, ArrowLeft } from "lucide-react";
import { motion } from "motion/react";
import Link from "next/link";
import { useEffect, useState } from "react";
import { getOrderTemplate, type OrderTemplateDto } from "@/lib/api/order-templates";
import { TemplateEditor } from "./template-editor";

export function EditTemplateLoader({ id }: { id: string }) {
  const [template, setTemplate] = useState<OrderTemplateDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const t = await getOrderTemplate(id);
        if (!cancelled) setTemplate(t);
      } catch (e) {
        if (!cancelled)
          setError((e as Error).message || "Failed to load template.");
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [id]);

  if (loading) {
    return (
      <div className="space-y-5">
        <motion.div
          initial={{ opacity: 0 }}
          animate={{ opacity: [0.4, 0.7, 0.4] }}
          transition={{ duration: 1.4, repeat: Infinity }}
          className="h-16 rounded-[var(--radius-xl)] bg-[var(--color-ink-100)] dark:bg-white/[0.04]"
        />
        <motion.div
          initial={{ opacity: 0 }}
          animate={{ opacity: [0.4, 0.7, 0.4] }}
          transition={{ duration: 1.4, repeat: Infinity, delay: 0.1 }}
          className="h-72 rounded-[var(--radius-xl)] bg-[var(--color-ink-100)] dark:bg-white/[0.04]"
        />
      </div>
    );
  }

  if (error || !template) {
    return (
      <div className="rounded-[var(--radius-xl)] glass px-6 py-12 text-center">
        <div className="mx-auto mb-4 grid h-12 w-12 place-items-center rounded-[14px] bg-[var(--color-coral)]/15 text-[var(--color-coral)]">
          <AlertTriangle className="h-5 w-5" strokeWidth={2.2} />
        </div>
        <h2 className="font-display text-[1.1rem] font-semibold text-[var(--color-ink-900)]">
          Couldn't load this template
        </h2>
        <p className="mt-1 text-[12.5px] text-[var(--color-ink-500)]">
          {error ?? "Template not found."}
        </p>
        <Link
          href="/delivery-orders/order-templates"
          className="mt-5 inline-flex items-center gap-1.5 rounded-full bg-[var(--color-brand-900)] px-4 py-2 text-[12px] font-semibold text-white dark:bg-[var(--color-brand-500)]"
        >
          <ArrowLeft className="h-3.5 w-3.5" strokeWidth={2.3} />
          Back to templates
        </Link>
      </div>
    );
  }

  return <TemplateEditor existing={template} />;
}
