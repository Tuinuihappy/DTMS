"use client";

import { AlertCircle, ArrowRight, Check, Eye, EyeOff, Loader2, Truck } from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { useRouter, useSearchParams } from "next/navigation";
import { useId, useState } from "react";
import { useAuth } from "@/components/auth/auth-provider";

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
  const { login } = useAuth();
  // Landing CTAs may land users on the sign-up tab via ?mode=signup.
  const initialMode: Mode = params?.get("mode") === "signup" ? "up" : "in";
  const [mode, setMode] = useState<Mode>(initialMode);
  const [status, setStatus] = useState<Status>("idle");
  const [error, setError] = useState<string | null>(null);
  // After a successful sign-up we route back to the sign-in tab so the
  // user authenticates with the credentials they just registered. The
  // banner + prefilled username give them a clear signal that the account
  // exists; password is intentionally left blank.
  const [justCreated, setJustCreated] = useState(false);
  const [prefilledUsername, setPrefilledUsername] = useState("");

  // Sign-in: call /api/auth/login, then push to from-param or /dashboard.
  const handleSignInSubmit = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    if (status !== "idle") return;
    const fd = new FormData(e.currentTarget);
    const username = ((fd.get("username") as string) || "").trim();
    const password = (fd.get("password") as string) || "";
    setError(null);
    setStatus("submitting");
    const result = await login(username, password);
    if (!result.ok) {
      setError(result.message);
      setStatus("idle");
      return;
    }
    setStatus("success");
    const dest = params?.get("from") || "/dashboard";
    window.setTimeout(() => router.push(dest), 650);
  };

  // Sign-up: theatrical only — no backend endpoint exists yet. On "success"
  // flip back to sign-in with the just-registered username pre-filled and
  // a welcome banner shown.
  const handleSignUpSubmit = (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    if (status !== "idle") return;
    const username = (new FormData(e.currentTarget).get("email") as string) || "";
    setStatus("submitting");
    window.setTimeout(() => setStatus("success"), 950);
    window.setTimeout(() => {
      setPrefilledUsername(username);
      setJustCreated(true);
      setMode("in");
      setStatus("idle");
    }, 1700);
  };

  // Dismiss the welcome banner the moment the user manually navigates
  // away from sign-in (e.g. flips back to sign-up).
  const handleModeChange = (next: Mode) => {
    if (next !== "in") setJustCreated(false);
    setError(null);
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
              defaultUsername={prefilledUsername}
              error={error}
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
  defaultUsername,
  error,
}: {
  status: Status;
  onSubmit: (e: React.FormEvent<HTMLFormElement>) => void;
  welcome: boolean;
  defaultUsername: string;
  error: string | null;
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
                Account created. Sign in with the credentials you just
                registered to finish setting up your fleet.
              </span>
            </div>
          </motion.div>
        )}
      </AnimatePresence>

      <FieldStagger delay={0.86}>
        <FloatingInput
          label="Username"
          type="text"
          name="username"
          autoComplete="username"
          defaultValue={defaultUsername}
          required
        />
      </FieldStagger>
      <FieldStagger delay={0.92}>
        <PasswordInput label="Password" name="password" autoComplete="current-password" required />
      </FieldStagger>
      <AnimatePresence initial={false}>
        {error && (
          <motion.div
            key="error"
            initial={{ opacity: 0, y: -4, height: 0 }}
            animate={{ opacity: 1, y: 0, height: "auto" }}
            exit={{ opacity: 0, y: -4, height: 0 }}
            transition={{ duration: 0.32, ease }}
            className="overflow-hidden"
            role="alert"
            aria-live="polite"
          >
            <div
              className="flex items-start gap-2.5 rounded-2xl px-4 py-3 text-[12.5px] leading-relaxed ring-1"
              style={{
                background: "color-mix(in srgb, var(--color-coral) 14%, transparent)",
                color: "var(--color-coral)",
                borderColor: "transparent",
              }}
            >
              <AlertCircle className="mt-[2px] h-4 w-4 shrink-0" strokeWidth={2.4} />
              <span>{error}</span>
            </div>
          </motion.div>
        )}
      </AnimatePresence>
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

