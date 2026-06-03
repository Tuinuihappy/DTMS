"use client";

import { ArrowRight, Check, Eye, EyeOff, Loader2, Truck } from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { useRouter, useSearchParams } from "next/navigation";
import { useId, useState } from "react";

/* -------------------------------------------------------------------------- */
/* AuthPanel — the glass form panel that lives over the dusk scene.            */
/*                                                                            */
/* Two modes: "in" (sign in) and "up" (sign up). The tabs share a layoutId    */
/* so the pill slides between them; the form bodies live inside an            */
/* AnimatePresence with mode="wait" + height-auto so the panel re-flows       */
/* smoothly between five and six fields.                                       */
/*                                                                            */
/* No real auth — submit shows a spinner, then a checkmark, then routes to    */
/* /dashboard. That delay sells the moment without being slow.                */
/* -------------------------------------------------------------------------- */

const EASE_OUT_QUART = [0.22, 1, 0.36, 1] as const;

type Mode = "in" | "up";
type Status = "idle" | "submitting" | "success";

export function AuthPanel() {
  const router = useRouter();
  const params = useSearchParams();
  // Default tab honours ?mode=signup — landing's "Get started" CTAs use this
  // to drop users directly into the registration flow.
  const initialMode: Mode = params?.get("mode") === "signup" ? "up" : "in";
  const [mode, setMode] = useState<Mode>(initialMode);
  const [status, setStatus] = useState<Status>("idle");

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (status !== "idle") return;
    setStatus("submitting");
    // Theatrical pacing — long enough to feel like real work, short enough
    // not to annoy. Spinner → check → route, with the check held for a
    // beat so the user sees the success state.
    window.setTimeout(() => setStatus("success"), 950);
    window.setTimeout(() => router.push("/dashboard"), 1700);
  };

  return (
    <motion.div
      className="relative w-full max-w-[460px]"
      initial={{ opacity: 0, x: 40, scale: 0.985 }}
      animate={{ opacity: 1, x: 0, scale: 1 }}
      transition={{ duration: 0.9, delay: 0.85, ease: EASE_OUT_QUART }}
    >
      <div className="dusk-glass relative rounded-[28px] p-7 sm:p-9 overflow-hidden">
        {/* Inner light leak — the dusk scene shows through subtly at the top */}
        <div
          aria-hidden
          className="pointer-events-none absolute inset-x-0 -top-1/2 h-full"
          style={{
            background:
              "radial-gradient(80% 60% at 70% 100%, rgba(255, 170, 110, 0.22), transparent 70%)",
          }}
        />

        <motion.header
          initial={{ opacity: 0, y: 8 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.6, delay: 1.0, ease: EASE_OUT_QUART }}
          className="relative mb-7 flex items-center gap-3"
        >
          {/* Brand mark — the truck-in-pastel that matches the rest of the app */}
          <span
            className="relative grid h-10 w-10 place-items-center rounded-[12px] text-[#1A0F2E] shadow-[inset_0_1px_0_rgba(255,255,255,0.55),0_8px_22px_-8px_rgba(0,0,0,0.45)]"
            style={{
              background:
                "linear-gradient(135deg, #FFD8CC 0%, #F3D5EC 55%, #D7DBFF 100%)",
            }}
            aria-hidden
          >
            <Truck className="h-6 w-6" strokeWidth={1.75} />
          </span>
          <div className="leading-tight">
            <p className="font-dusk-display text-[15px] font-medium tracking-[0.16em] text-amber-100/70 uppercase">
              TMS
            </p>
            <p className="font-dusk-body text-[12.5px] text-amber-50/55 -mt-0.5">
              Operations control for freight.
            </p>
          </div>
        </motion.header>

        {/* Tabs — sliding pill on layoutId */}
        <motion.div
          initial={{ opacity: 0, y: 8 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.6, delay: 1.08, ease: EASE_OUT_QUART }}
          className="relative mb-6 grid grid-cols-2 rounded-full bg-white/[0.06] p-1 backdrop-blur-md ring-1 ring-white/[0.08]"
        >
          {(["in", "up"] as const).map((m) => {
            const active = mode === m;
            return (
              <button
                key={m}
                type="button"
                onClick={() => setMode(m)}
                className="relative z-10 rounded-full py-2 text-[13px] font-medium tracking-tight transition-colors font-dusk-body cursor-pointer"
                style={{
                  color: active ? "#1A0F2E" : "rgba(255, 240, 230, 0.65)",
                }}
              >
                {active && (
                  <motion.span
                    layoutId="auth-pill"
                    transition={{ type: "spring", stiffness: 380, damping: 32 }}
                    className="absolute inset-0 rounded-full"
                    style={{
                      background:
                        "linear-gradient(135deg, #FFE7B7 0%, #FFC48A 50%, #FF9B7A 100%)",
                      boxShadow:
                        "inset 0 1px 0 rgba(255,255,255,0.65), 0 6px 14px -6px rgba(255, 140, 90, 0.55)",
                    }}
                  />
                )}
                <span className="relative">{m === "in" ? "Sign in" : "Create account"}</span>
              </button>
            );
          })}
        </motion.div>

        {/* Forms */}
        <AnimatePresence mode="wait" initial={false}>
          {mode === "in" ? (
            <SignInForm key="in" status={status} onSubmit={handleSubmit} />
          ) : (
            <SignUpForm key="up" status={status} onSubmit={handleSubmit} />
          )}
        </AnimatePresence>

        {/* Footer switcher */}
        <motion.p
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          transition={{ duration: 0.6, delay: 1.5 }}
          className="relative mt-7 text-center text-[12.5px] font-dusk-body text-amber-50/60"
        >
          {mode === "in" ? (
            <>
              New to the fleet?{" "}
              <button
                type="button"
                onClick={() => setMode("up")}
                className="text-amber-100/90 underline decoration-amber-200/40 underline-offset-[3px] transition-colors hover:text-amber-50 hover:decoration-amber-100/80 cursor-pointer"
              >
                Create an account
              </button>
            </>
          ) : (
            <>
              Already onboarded?{" "}
              <button
                type="button"
                onClick={() => setMode("in")}
                className="text-amber-100/90 underline decoration-amber-200/40 underline-offset-[3px] transition-colors hover:text-amber-50 hover:decoration-amber-100/80 cursor-pointer"
              >
                Sign in
              </button>
            </>
          )}
        </motion.p>
      </div>
    </motion.div>
  );
}

/* -------------------------------------------------------------------------- */
/* Forms                                                                       */
/* -------------------------------------------------------------------------- */

function SignInForm({
  status,
  onSubmit,
}: {
  status: Status;
  onSubmit: (e: React.FormEvent) => void;
}) {
  return (
    <motion.form
      onSubmit={onSubmit}
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      exit={{ opacity: 0, y: -6 }}
      transition={{ duration: 0.32, ease: EASE_OUT_QUART }}
      className="relative space-y-5"
    >
      <FormFieldStagger delay={1.16}>
        <FloatingInput label="Work email" type="email" name="email" autoComplete="email" required />
      </FormFieldStagger>
      <FormFieldStagger delay={1.24}>
        <PasswordInput label="Password" name="password" autoComplete="current-password" required />
      </FormFieldStagger>
      <FormFieldStagger delay={1.32}>
        <div className="flex items-center justify-between text-[12.5px] font-dusk-body">
          <label className="flex items-center gap-2 text-amber-50/70 cursor-pointer">
            <CustomCheckbox /> Remember me
          </label>
          <a href="#" className="text-amber-100/85 hover:text-amber-50 transition-colors">
            Forgot password?
          </a>
        </div>
      </FormFieldStagger>
      <FormFieldStagger delay={1.4}>
        <SubmitButton status={status} label="Take the wheel" />
      </FormFieldStagger>
      <FormFieldStagger delay={1.48}>
        <Divider />
      </FormFieldStagger>
      <FormFieldStagger delay={1.54}>
        <GoogleButton label="Continue with Google" />
      </FormFieldStagger>
    </motion.form>
  );
}

function SignUpForm({
  status,
  onSubmit,
}: {
  status: Status;
  onSubmit: (e: React.FormEvent) => void;
}) {
  return (
    <motion.form
      onSubmit={onSubmit}
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      exit={{ opacity: 0, y: -6 }}
      transition={{ duration: 0.32, ease: EASE_OUT_QUART }}
      className="relative space-y-5"
    >
      <FormFieldStagger delay={0.04}>
        <div className="grid grid-cols-2 gap-4">
          <FloatingInput label="First name" name="first" autoComplete="given-name" required />
          <FloatingInput label="Last name" name="last" autoComplete="family-name" required />
        </div>
      </FormFieldStagger>
      <FormFieldStagger delay={0.1}>
        <FloatingInput label="Work email" type="email" name="email" autoComplete="email" required />
      </FormFieldStagger>
      <FormFieldStagger delay={0.16}>
        <FloatingInput label="Carrier or company" name="company" autoComplete="organization" required />
      </FormFieldStagger>
      <FormFieldStagger delay={0.22}>
        <PasswordInput label="Choose a password" name="password" autoComplete="new-password" required />
      </FormFieldStagger>
      <FormFieldStagger delay={0.28}>
        <label className="flex items-start gap-2 text-[12px] font-dusk-body text-amber-50/70 cursor-pointer">
          <CustomCheckbox className="mt-[3px]" />
          <span className="leading-relaxed">
            I agree to the{" "}
            <a href="#" className="text-amber-100/85 underline decoration-amber-200/40 hover:text-amber-50">
              Carrier Terms
            </a>{" "}
            and the{" "}
            <a href="#" className="text-amber-100/85 underline decoration-amber-200/40 hover:text-amber-50">
              Privacy Policy
            </a>
            .
          </span>
        </label>
      </FormFieldStagger>
      <FormFieldStagger delay={0.34}>
        <SubmitButton status={status} label="Start your route" />
      </FormFieldStagger>
      <FormFieldStagger delay={0.4}>
        <Divider />
      </FormFieldStagger>
      <FormFieldStagger delay={0.46}>
        <GoogleButton label="Sign up with Google" />
      </FormFieldStagger>
    </motion.form>
  );
}

/* -------------------------------------------------------------------------- */
/* Primitives                                                                  */
/* -------------------------------------------------------------------------- */

function FormFieldStagger({
  delay,
  children,
}: {
  delay: number;
  children: React.ReactNode;
}) {
  return (
    <motion.div
      initial={{ opacity: 0, y: 10 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.45, delay, ease: EASE_OUT_QUART }}
    >
      {children}
    </motion.div>
  );
}

function FloatingInput({
  label,
  type = "text",
  ...rest
}: React.InputHTMLAttributes<HTMLInputElement> & { label: string }) {
  const id = useId();
  const [focused, setFocused] = useState(false);
  const [hasValue, setHasValue] = useState(false);
  const floated = focused || hasValue;

  return (
    <div className="relative pt-3">
      <label
        htmlFor={id}
        className="pointer-events-none absolute left-0 font-dusk-body transition-all duration-200 select-none"
        style={{
          top: floated ? 0 : 22,
          fontSize: floated ? 11 : 14,
          letterSpacing: floated ? "0.08em" : "0",
          textTransform: floated ? "uppercase" : "none",
          color: focused
            ? "rgba(255, 220, 170, 0.95)"
            : "rgba(255, 240, 220, 0.55)",
        }}
      >
        {label}
      </label>
      <input
        id={id}
        type={type}
        onFocus={() => setFocused(true)}
        onBlur={(e) => {
          setFocused(false);
          setHasValue(e.currentTarget.value.length > 0);
        }}
        onChange={(e) => setHasValue(e.currentTarget.value.length > 0)}
        className="block w-full bg-transparent pb-2 pt-1 text-[14px] font-dusk-body text-amber-50 placeholder:text-transparent outline-none"
        {...rest}
      />
      {/* Underline — neutral at rest, sun-warm on focus */}
      <span
        className="absolute inset-x-0 bottom-0 h-px"
        style={{ background: "rgba(255, 240, 220, 0.2)" }}
      />
      <motion.span
        className="absolute inset-x-0 bottom-0 h-[1.5px] origin-left"
        style={{
          background:
            "linear-gradient(90deg, #FFD56B 0%, #FF8A4C 60%, #FF5C7A 100%)",
        }}
        initial={{ scaleX: 0, opacity: 0 }}
        animate={{ scaleX: focused ? 1 : 0, opacity: focused ? 1 : 0 }}
        transition={{ duration: 0.35, ease: EASE_OUT_QUART }}
      />
    </div>
  );
}

function PasswordInput({
  label,
  ...rest
}: React.InputHTMLAttributes<HTMLInputElement> & { label: string }) {
  const [reveal, setReveal] = useState(false);
  return (
    <div className="relative">
      <FloatingInput label={label} type={reveal ? "text" : "password"} {...rest} />
      <button
        type="button"
        aria-label={reveal ? "Hide password" : "Show password"}
        onClick={() => setReveal((r) => !r)}
        className="absolute right-0 bottom-2 grid h-7 w-7 place-items-center rounded-full text-amber-50/60 transition-colors hover:bg-white/[0.08] hover:text-amber-50 cursor-pointer"
      >
        {reveal ? <EyeOff className="h-4 w-4" strokeWidth={1.6} /> : <Eye className="h-4 w-4" strokeWidth={1.6} />}
      </button>
    </div>
  );
}

function CustomCheckbox({ className = "" }: { className?: string }) {
  const [checked, setChecked] = useState(false);
  return (
    <span
      role="checkbox"
      aria-checked={checked}
      tabIndex={0}
      onClick={() => setChecked((c) => !c)}
      onKeyDown={(e) => {
        if (e.key === " " || e.key === "Enter") {
          e.preventDefault();
          setChecked((c) => !c);
        }
      }}
      className={`relative inline-grid h-[15px] w-[15px] place-items-center rounded-[4px] transition-all cursor-pointer ${className}`}
      style={{
        background: checked
          ? "linear-gradient(135deg, #FFE7B7 0%, #FF9B7A 100%)"
          : "rgba(255, 240, 220, 0.08)",
        boxShadow: checked
          ? "inset 0 1px 0 rgba(255,255,255,0.65), 0 0 0 1px rgba(255, 170, 110, 0.55)"
          : "inset 0 0 0 1px rgba(255, 240, 220, 0.25)",
      }}
    >
      <motion.svg
        viewBox="0 0 12 12"
        className="h-2.5 w-2.5"
        initial={false}
        animate={{ scale: checked ? 1 : 0, opacity: checked ? 1 : 0 }}
        transition={{ duration: 0.18, ease: EASE_OUT_QUART }}
      >
        <path
          d="M 2 6 L 5 9 L 10 3"
          fill="none"
          stroke="#1A0F2E"
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
        />
      </motion.svg>
    </span>
  );
}

function SubmitButton({ status, label }: { status: Status; label: string }) {
  const submitting = status === "submitting";
  const success = status === "success";
  return (
    <button
      type="submit"
      disabled={status !== "idle"}
      className="group relative flex h-12 w-full items-center justify-center overflow-hidden rounded-full font-dusk-body text-[14px] font-semibold tracking-tight transition-transform disabled:cursor-default cursor-pointer"
      style={{
        color: "#1A0F2E",
        background:
          "linear-gradient(135deg, #FFE7B7 0%, #FFC48A 45%, #FF9B7A 75%, #FF6E72 100%)",
        boxShadow:
          "inset 0 1px 0 rgba(255,255,255,0.75), 0 12px 30px -10px rgba(255, 110, 114, 0.65), 0 0 0 1px rgba(255, 170, 110, 0.25)",
      }}
    >
      {/* Sliding sheen on hover — refracts like glass catching dusk light */}
      <span
        aria-hidden
        className="pointer-events-none absolute inset-0 translate-x-[-100%] bg-gradient-to-r from-transparent via-white/45 to-transparent transition-transform duration-700 group-hover:translate-x-[100%]"
      />
      <AnimatePresence mode="wait">
        {submitting ? (
          <motion.span
            key="loading"
            initial={{ opacity: 0, y: 6 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: -6 }}
            transition={{ duration: 0.2 }}
            className="relative flex items-center gap-2"
          >
            <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.2} />
            Routing…
          </motion.span>
        ) : success ? (
          <motion.span
            key="success"
            initial={{ opacity: 0, scale: 0.8 }}
            animate={{ opacity: 1, scale: 1 }}
            exit={{ opacity: 0, scale: 0.95 }}
            transition={{ type: "spring", stiffness: 360, damping: 22 }}
            className="relative flex items-center gap-2"
          >
            <Check className="h-4 w-4" strokeWidth={2.6} />
            On the road
          </motion.span>
        ) : (
          <motion.span
            key="label"
            initial={{ opacity: 0, y: 6 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: -6 }}
            transition={{ duration: 0.2 }}
            className="relative flex items-center gap-1.5"
          >
            {label}
            <ArrowRight className="h-4 w-4 transition-transform group-hover:translate-x-0.5" strokeWidth={2.2} />
          </motion.span>
        )}
      </AnimatePresence>
    </button>
  );
}

function Divider() {
  return (
    <div className="relative flex items-center justify-center">
      <span className="h-px w-full bg-gradient-to-r from-transparent via-white/[0.18] to-transparent" />
      <span className="absolute bg-[#1c1140]/0 px-3 text-[11px] font-dusk-body uppercase tracking-[0.18em] text-amber-50/45">
        or
      </span>
    </div>
  );
}

function GoogleButton({ label }: { label: string }) {
  return (
    <button
      type="button"
      className="group flex h-11 w-full items-center justify-center gap-2.5 rounded-full bg-white/[0.07] font-dusk-body text-[13.5px] font-medium text-amber-50/90 ring-1 ring-white/[0.1] transition-all hover:bg-white/[0.12] hover:text-amber-50 hover:-translate-y-px cursor-pointer"
    >
      <GoogleGlyph />
      {label}
    </button>
  );
}

function GoogleGlyph() {
  return (
    <svg viewBox="0 0 24 24" className="h-4 w-4" aria-hidden>
      <path
        fill="#EA4335"
        d="M12 5.04c1.86 0 3.5.64 4.8 1.9l3.57-3.57C18.16 1.18 15.31 0 12 0 7.27 0 3.19 2.7 1.24 6.65l4.17 3.23C6.4 7.04 8.96 5.04 12 5.04Z"
      />
      <path
        fill="#34A853"
        d="M23.49 12.27c0-.79-.07-1.54-.19-2.27H12v4.51h6.45c-.28 1.45-1.12 2.68-2.39 3.5l3.66 2.84c2.14-1.98 3.37-4.9 3.37-8.58Z"
      />
      <path
        fill="#FBBC05"
        d="M5.41 14.12a7.07 7.07 0 0 1 0-4.24L1.24 6.65a12 12 0 0 0 0 10.7l4.17-3.23Z"
      />
      <path
        fill="#4285F4"
        d="M12 24c3.24 0 5.95-1.07 7.93-2.9l-3.66-2.84c-1.02.69-2.33 1.1-4.27 1.1-3.04 0-5.6-2-6.59-4.69l-4.17 3.23C3.19 21.3 7.27 24 12 24Z"
      />
    </svg>
  );
}
