"use client";

import { ArrowRight, Check, Eye, EyeOff, Loader2, Truck } from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { useRouter, useSearchParams } from "next/navigation";
import { useId, useState } from "react";

/* -------------------------------------------------------------------------- */
/* AuthPanel — the right-hand form panel.                                      */
/*                                                                            */
/* Fully theme-aware: every color flows through --color-ink-* / brand /        */
/* pastel tokens and the .glass utility, so the panel inherits the light       */
/* pastel-canvas look and the dark navy-velvet look from globals.css           */
/* automatically.                                                              */
/*                                                                            */
/* Tabs slide on a shared layoutId. Form bodies live inside AnimatePresence    */
/* with mode="wait" so the height auto-flows between five and six fields.     */
/*                                                                            */
/* Submit → spinner → checkmark → router.push("/dashboard").                   */
/* -------------------------------------------------------------------------- */

const ease = [0.22, 1, 0.36, 1] as const;

type Mode = "in" | "up";
type Status = "idle" | "submitting" | "success";

export function AuthPanel() {
  const router = useRouter();
  const params = useSearchParams();
  // Landing CTAs may land users on the sign-up tab via ?mode=signup.
  const initialMode: Mode = params?.get("mode") === "signup" ? "up" : "in";
  const [mode, setMode] = useState<Mode>(initialMode);
  const [status, setStatus] = useState<Status>("idle");
  // After a successful sign-up we route back to the sign-in tab so the
  // user authenticates with the credentials they just registered. The
  // banner + prefilled email give them a clear signal that the account
  // exists; password is intentionally left blank.
  const [justCreated, setJustCreated] = useState(false);
  const [prefilledEmail, setPrefilledEmail] = useState("");

  // Sign-in: theatrical pacing, then push to /dashboard.
  const handleSignInSubmit = (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    if (status !== "idle") return;
    setStatus("submitting");
    window.setTimeout(() => setStatus("success"), 950);
    window.setTimeout(() => router.push("/dashboard"), 1700);
  };

  // Sign-up: same pacing, but on success we flip the tab back to sign-in
  // with the just-registered email pre-filled and a welcome banner shown.
  const handleSignUpSubmit = (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    if (status !== "idle") return;
    const email = (new FormData(e.currentTarget).get("email") as string) || "";
    setStatus("submitting");
    window.setTimeout(() => setStatus("success"), 950);
    window.setTimeout(() => {
      setPrefilledEmail(email);
      setJustCreated(true);
      setMode("in");
      setStatus("idle");
    }, 1700);
  };

  // Dismiss the welcome banner the moment the user manually navigates
  // away from sign-in (e.g. flips back to sign-up).
  const handleModeChange = (next: Mode) => {
    if (next !== "in") setJustCreated(false);
    setMode(next);
  };

  return (
    <motion.div
      className="relative w-full max-w-[440px]"
      initial={{ opacity: 0, x: 32, scale: 0.985 }}
      animate={{ opacity: 1, x: 0, scale: 1 }}
      transition={{ duration: 0.85, delay: 0.55, ease }}
    >
      <div className="glass glass-edge relative overflow-hidden rounded-[var(--radius-xl)] p-7 sm:p-8">
        <motion.header
          initial={{ opacity: 0, y: 6 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.55, delay: 0.7, ease }}
          className="relative mb-7 flex items-center gap-3"
        >
          {/* Brand mark — the truck-in-pastel squircle matching the rest of
              the app. Uses fixed pastel gradient so it reads the same in
              both themes. */}
          <span
            className="relative grid h-10 w-10 place-items-center rounded-[12px] text-[var(--color-brand-900)] shadow-[inset_0_1px_0_rgba(255,255,255,0.55),0_8px_22px_-8px_rgba(15,23,42,0.18)]"
            style={{
              background:
                "linear-gradient(135deg, #FFD8CC 0%, #F3D5EC 55%, #D7DBFF 100%)",
            }}
            aria-hidden
          >
            <Truck className="h-6 w-6" strokeWidth={1.75} />
          </span>
          <div className="leading-tight">
            <p className="font-display text-[13px] font-semibold tracking-[0.16em] uppercase text-[var(--color-ink-900)]">
              TMS
            </p>
            <p className="text-[12px] text-[var(--color-ink-500)] -mt-0.5">
              Operations control for freight.
            </p>
          </div>
        </motion.header>

        {/* Tabs — sliding pill on a shared layoutId. */}
        <motion.div
          initial={{ opacity: 0, y: 6 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.55, delay: 0.78, ease }}
          className="relative mb-6 grid grid-cols-2 rounded-full bg-[var(--color-ink-50)] p-1 ring-1 ring-[var(--color-ink-100)] dark:ring-white/[0.06]"
        >
          {(["in", "up"] as const).map((m) => {
            const active = mode === m;
            return (
              <button
                key={m}
                type="button"
                onClick={() => handleModeChange(m)}
                className="relative z-10 cursor-pointer rounded-full py-2 text-[13px] font-medium tracking-tight transition-colors"
                style={{
                  color: active
                    ? "white"
                    : "var(--color-ink-500)",
                }}
              >
                {active && (
                  <motion.span
                    layoutId="auth-pill"
                    transition={{ type: "spring", stiffness: 380, damping: 32 }}
                    className="absolute inset-0 rounded-full bg-[var(--color-brand-900)] shadow-[inset_0_1px_0_rgba(255,255,255,0.18),0_8px_20px_-8px_rgba(14,21,48,0.45)]"
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
            <SignInForm
              key="in"
              status={status}
              onSubmit={handleSignInSubmit}
              welcome={justCreated}
              defaultEmail={prefilledEmail}
            />
          ) : (
            <SignUpForm key="up" status={status} onSubmit={handleSignUpSubmit} />
          )}
        </AnimatePresence>

        {/* Footer switcher */}
        <motion.p
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          transition={{ duration: 0.55, delay: 1.2 }}
          className="relative mt-7 text-center text-[12.5px] text-[var(--color-ink-500)]"
        >
          {mode === "in" ? (
            <>
              New to the fleet?{" "}
              <button
                type="button"
                onClick={() => handleModeChange("up")}
                className="cursor-pointer text-[var(--color-brand-900)] font-medium underline decoration-[var(--color-brand-200)] underline-offset-[3px] transition-colors hover:decoration-[var(--color-brand-500)] dark:text-[var(--color-brand-400)]"
              >
                Create an account
              </button>
            </>
          ) : (
            <>
              Already onboarded?{" "}
              <button
                type="button"
                onClick={() => handleModeChange("in")}
                className="cursor-pointer text-[var(--color-brand-900)] font-medium underline decoration-[var(--color-brand-200)] underline-offset-[3px] transition-colors hover:decoration-[var(--color-brand-500)] dark:text-[var(--color-brand-400)]"
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
  welcome,
  defaultEmail,
}: {
  status: Status;
  onSubmit: (e: React.FormEvent<HTMLFormElement>) => void;
  welcome: boolean;
  defaultEmail: string;
}) {
  return (
    <motion.form
      onSubmit={onSubmit}
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      exit={{ opacity: 0, y: -6 }}
      transition={{ duration: 0.3, ease }}
      className="relative space-y-5"
    >
      <AnimatePresence initial={false}>
        {welcome && (
          <motion.div
            key="welcome"
            initial={{ opacity: 0, y: -6, height: 0, marginBottom: 0 }}
            animate={{ opacity: 1, y: 0, height: "auto", marginBottom: 4 }}
            exit={{ opacity: 0, y: -4, height: 0, marginBottom: 0 }}
            transition={{ duration: 0.42, ease }}
            className="overflow-hidden"
          >
            <div
              className="flex items-start gap-2.5 rounded-2xl px-4 py-3 text-[12.5px] leading-relaxed ring-1"
              style={{
                background: "var(--color-success-soft)",
                color: "var(--color-success)",
                borderColor: "transparent",
              }}
            >
              <Check className="mt-[2px] h-4 w-4 shrink-0" strokeWidth={2.6} />
              <span>
                Account created. Sign in with the email you just registered
                to finish setting up your fleet.
              </span>
            </div>
          </motion.div>
        )}
      </AnimatePresence>

      <FieldStagger delay={0.86}>
        <FloatingInput
          label="Work email"
          type="email"
          name="email"
          autoComplete="email"
          defaultValue={defaultEmail}
          required
        />
      </FieldStagger>
      <FieldStagger delay={0.92}>
        <PasswordInput label="Password" name="password" autoComplete="current-password" required />
      </FieldStagger>
      <FieldStagger delay={0.98}>
        <div className="flex items-center justify-between text-[12.5px]">
          <label className="flex cursor-pointer items-center gap-2 text-[var(--color-ink-600)]">
            <CustomCheckbox /> Remember me
          </label>
          <a
            href="#"
            className="text-[var(--color-brand-900)] font-medium transition-colors hover:text-[var(--color-brand-500)] dark:text-[var(--color-brand-400)]"
          >
            Forgot password?
          </a>
        </div>
      </FieldStagger>
      <FieldStagger delay={1.04}>
        <SubmitButton
          status={status}
          label="Take the wheel"
          submittingLabel="Routing…"
          successLabel="On the road"
        />
      </FieldStagger>
      <FieldStagger delay={1.1}>
        <Divider />
      </FieldStagger>
      <FieldStagger delay={1.14}>
        <GoogleButton label="Continue with Google" />
      </FieldStagger>
    </motion.form>
  );
}

function SignUpForm({
  status,
  onSubmit,
}: {
  status: Status;
  onSubmit: (e: React.FormEvent<HTMLFormElement>) => void;
}) {
  return (
    <motion.form
      onSubmit={onSubmit}
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      exit={{ opacity: 0, y: -6 }}
      transition={{ duration: 0.3, ease }}
      className="relative space-y-5"
    >
      <FieldStagger delay={0.05}>
        <div className="grid grid-cols-2 gap-4">
          <FloatingInput label="First name" name="first" autoComplete="given-name" required />
          <FloatingInput label="Last name" name="last" autoComplete="family-name" required />
        </div>
      </FieldStagger>
      <FieldStagger delay={0.11}>
        <FloatingInput label="Work email" type="email" name="email" autoComplete="email" required />
      </FieldStagger>
      <FieldStagger delay={0.17}>
        <FloatingInput label="Carrier or company" name="company" autoComplete="organization" required />
      </FieldStagger>
      <FieldStagger delay={0.23}>
        <PasswordInput label="Choose a password" name="password" autoComplete="new-password" required />
      </FieldStagger>
      <FieldStagger delay={0.29}>
        <label className="flex cursor-pointer items-start gap-2 text-[12px] text-[var(--color-ink-600)]">
          <CustomCheckbox className="mt-[3px]" />
          <span className="leading-relaxed">
            I agree to the{" "}
            <a
              href="#"
              className="text-[var(--color-brand-900)] font-medium underline decoration-[var(--color-brand-200)] hover:decoration-[var(--color-brand-500)] dark:text-[var(--color-brand-400)]"
            >
              Carrier Terms
            </a>{" "}
            and{" "}
            <a
              href="#"
              className="text-[var(--color-brand-900)] font-medium underline decoration-[var(--color-brand-200)] hover:decoration-[var(--color-brand-500)] dark:text-[var(--color-brand-400)]"
            >
              Privacy Policy
            </a>
            .
          </span>
        </label>
      </FieldStagger>
      <FieldStagger delay={0.35}>
        <SubmitButton
          status={status}
          label="Start your route"
          submittingLabel="Creating account…"
          successLabel="Account created"
        />
      </FieldStagger>
      <FieldStagger delay={0.41}>
        <Divider />
      </FieldStagger>
      <FieldStagger delay={0.46}>
        <GoogleButton label="Sign up with Google" />
      </FieldStagger>
    </motion.form>
  );
}

/* -------------------------------------------------------------------------- */
/* Primitives                                                                  */
/* -------------------------------------------------------------------------- */

function FieldStagger({
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
      transition={{ duration: 0.42, delay, ease }}
    >
      {children}
    </motion.div>
  );
}

function FloatingInput({
  label,
  type = "text",
  defaultValue,
  ...rest
}: React.InputHTMLAttributes<HTMLInputElement> & { label: string }) {
  const id = useId();
  const [focused, setFocused] = useState(false);
  // Seed `hasValue` from the defaultValue so the label floats up immediately
  // when an email is pre-filled (e.g. after a successful sign-up routes the
  // user back to the sign-in tab).
  const seeded = typeof defaultValue === "string" && defaultValue.length > 0;
  const [hasValue, setHasValue] = useState(seeded);
  const floated = focused || hasValue;

  return (
    <div className="relative pt-3">
      <label
        htmlFor={id}
        className="pointer-events-none absolute left-0 select-none transition-all duration-200"
        style={{
          top: floated ? 0 : 22,
          fontSize: floated ? 11 : 14,
          letterSpacing: floated ? "0.08em" : "0",
          textTransform: floated ? "uppercase" : "none",
          color: focused
            ? "var(--color-brand-900)"
            : "var(--color-ink-500)",
          fontWeight: floated ? 600 : 400,
        }}
      >
        {label}
      </label>
      <input
        id={id}
        type={type}
        defaultValue={defaultValue}
        onFocus={() => setFocused(true)}
        onBlur={(e) => {
          setFocused(false);
          setHasValue(e.currentTarget.value.length > 0);
        }}
        onChange={(e) => setHasValue(e.currentTarget.value.length > 0)}
        className="block w-full bg-transparent pb-2 pt-1 text-[14px] text-[var(--color-ink-900)] placeholder:text-transparent outline-none"
        {...rest}
      />
      {/* Underline — ink at rest, brand-gradient on focus */}
      <span
        className="absolute inset-x-0 bottom-0 h-px"
        style={{ background: "var(--color-ink-200)" }}
      />
      <motion.span
        className="absolute inset-x-0 bottom-0 h-[1.5px] origin-left"
        style={{
          background:
            "linear-gradient(90deg, var(--color-brand-500), var(--color-brand-400))",
        }}
        initial={{ scaleX: 0, opacity: 0 }}
        animate={{ scaleX: focused ? 1 : 0, opacity: focused ? 1 : 0 }}
        transition={{ duration: 0.32, ease }}
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
        className="absolute right-0 bottom-2 grid h-7 w-7 cursor-pointer place-items-center rounded-full text-[var(--color-ink-400)] transition-colors hover:bg-[var(--color-ink-50)] hover:text-[var(--color-ink-700)] dark:hover:bg-white/[0.08]"
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
      className={`relative inline-grid h-[15px] w-[15px] cursor-pointer place-items-center rounded-[4px] transition-all ${className}`}
      style={{
        background: checked
          ? "var(--color-brand-900)"
          : "var(--color-surface)",
        boxShadow: checked
          ? "inset 0 1px 0 rgba(255,255,255,0.25), 0 0 0 1px var(--color-brand-900)"
          : "inset 0 0 0 1px var(--color-ink-200)",
      }}
    >
      <motion.svg
        viewBox="0 0 12 12"
        className="h-2.5 w-2.5"
        initial={false}
        animate={{ scale: checked ? 1 : 0, opacity: checked ? 1 : 0 }}
        transition={{ duration: 0.18, ease }}
      >
        <path
          d="M 2 6 L 5 9 L 10 3"
          fill="none"
          stroke="white"
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
        />
      </motion.svg>
    </span>
  );
}

function SubmitButton({
  status,
  label,
  submittingLabel = "Routing…",
  successLabel = "On the road",
}: {
  status: Status;
  label: string;
  submittingLabel?: string;
  successLabel?: string;
}) {
  const submitting = status === "submitting";
  const success = status === "success";
  return (
    <button
      type="submit"
      disabled={status !== "idle"}
      className="group relative flex h-12 w-full cursor-pointer items-center justify-center overflow-hidden rounded-full bg-[var(--color-brand-900)] pl-1.5 pr-1.5 text-[14px] font-semibold text-white shadow-[inset_0_1px_0_rgba(255,255,255,0.15),0_14px_30px_-12px_rgba(14,21,48,0.6)] transition-transform disabled:cursor-default"
    >
      {/* Sliding sheen on hover */}
      <span
        aria-hidden
        className="pointer-events-none absolute inset-0 -translate-x-full bg-gradient-to-r from-transparent via-white/30 to-transparent transition-transform duration-700 group-hover:translate-x-full"
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
            {submittingLabel}
          </motion.span>
        ) : success ? (
          <motion.span
            key="success"
            initial={{ opacity: 0, scale: 0.85 }}
            animate={{ opacity: 1, scale: 1 }}
            exit={{ opacity: 0, scale: 0.95 }}
            transition={{ type: "spring", stiffness: 360, damping: 22 }}
            className="relative flex items-center gap-2"
          >
            <Check className="h-4 w-4" strokeWidth={2.6} />
            {successLabel}
          </motion.span>
        ) : (
          <motion.span
            key="label"
            initial={{ opacity: 0, y: 6 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: -6 }}
            transition={{ duration: 0.2 }}
            className="relative flex items-center gap-2"
          >
            <span className="pl-3">{label}</span>
            <span className="grid h-9 w-9 place-items-center rounded-full bg-[var(--color-amber)] text-[var(--color-brand-900)] transition-transform duration-300 group-hover:rotate-45">
              <ArrowRight className="h-4 w-4" strokeWidth={2.5} />
            </span>
          </motion.span>
        )}
      </AnimatePresence>
    </button>
  );
}

function Divider() {
  return (
    <div className="relative flex items-center justify-center">
      <span className="h-px w-full bg-gradient-to-r from-transparent via-[var(--color-ink-200)] to-transparent" />
      <span className="absolute px-3 text-[11px] uppercase tracking-[0.18em] text-[var(--color-ink-400)]" style={{ background: "var(--color-surface)" }}>
        or
      </span>
    </div>
  );
}

function GoogleButton({ label }: { label: string }) {
  return (
    <button
      type="button"
      className="group flex h-11 w-full cursor-pointer items-center justify-center gap-2.5 rounded-full border border-[var(--color-ink-100)] bg-white/70 text-[13.5px] font-medium text-[var(--color-ink-800)] backdrop-blur transition-all hover:-translate-y-px hover:bg-white dark:border-white/[0.08] dark:bg-white/[0.05] dark:text-[var(--color-ink-800)] dark:hover:bg-white/[0.1]"
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
