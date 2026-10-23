"use client";

import { AlertCircle, ArrowRight, Check, Eye, EyeOff, Loader2, Truck } from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { useRouter, useSearchParams } from "next/navigation";
import { useEffect, useId, useRef, useState } from "react";
import { useAuth } from "@/components/auth/auth-provider";
import { sanitizeReturnPath } from "@/lib/auth/session";
import { ease as easing, T } from "@/lib/motion";

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
/*                                                                            */
/* This is the first screen every operator sees, so the form is usable at     */
/* once: the panel fades in over T.base with no delay and no staggered        */
/* fields — the brand scene beside it carries the choreography.               */
/* -------------------------------------------------------------------------- */

const ease = easing.out;

// Account creation lives in the central employee onboarding portal.
// Clicking "Create account" anywhere on this panel opens it in a new tab.
const SIGNUP_URL = "https://10.204.212.22:8443/";

type Mode = "in" | "up";
type Status = "idle" | "submitting" | "success";

export function AuthPanel() {
  const router = useRouter();
  const params = useSearchParams();
  const { login } = useAuth();
  // Landing CTAs may land users on the sign-up tab via ?mode=signup.
  const initialMode: Mode = params?.get("mode") === "signup" ? "up" : "in";
  const [mode, setMode] = useState<Mode>(initialMode);
  const tabsId = useId();
  const [status, setStatus] = useState<Status>("idle");
  const [error, setError] = useState<string | null>(null);
  // Bumped on every failed attempt — re-triggers the shake even when the
  // message text is identical to the previous one.
  const [errorCount, setErrorCount] = useState(0);
  // `from` is attacker-controllable via the URL — sanitize before
  // navigating so /login?from=//evil.com can't bounce the user off-origin.
  // Deliberately NOT prefetched on mount: before the session cookie exists,
  // proxy.ts answers a prefetch of `dest` with a redirect back to /login,
  // and the router could replay that cached redirect on the push below.
  const dest = sanitizeReturnPath(params?.get("from"), "/dashboard");
  // Safety net for the post-login redirect. If the auth proxy bounces the
  // navigation straight back to /login (e.g. the browser refused the
  // session cookie), the pathname never changes, this component never
  // unmounts, and the button would freeze at its success state forever.
  // A successful navigation unmounts us and the cleanup cancels the timer.
  const bounceTimer = useRef<number | null>(null);
  useEffect(
    () => () => {
      if (bounceTimer.current !== null) window.clearTimeout(bounceTimer.current);
    },
    [],
  );

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
      setErrorCount((n) => n + 1);
      setStatus("idle");
      return;
    }
    // Push straight away (there used to be a 650ms pause here purely for the
    // check-mark); the check-mark stays on the button while the route loads,
    // until this component unmounts on arrival.
    setStatus("success");
    router.push(dest);
    bounceTimer.current = window.setTimeout(() => {
      setStatus("idle");
      setError(
        "Signed in, but the session didn't stick in this browser. Please try again — if it keeps happening, contact the DTMS admin.",
      );
      setErrorCount((n) => n + 1);
    }, 4000);
  };

  // Typing again means the user has read the error — get it out of the way.
  const clearError = () => {
    if (error !== null) setError(null);
  };

  // Dismiss the welcome banner the moment the user manually navigates
  // away from sign-in (e.g. flips back to sign-up). Also clears stale
  // sign-up validation errors when switching tabs.
  const handleModeChange = (next: Mode) => {
    setError(null);
    setMode(next);
  };

  return (
    <motion.div
      className="relative w-full max-w-[440px]"
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      transition={T.base}
    >
      <div className="glass glass-edge relative overflow-hidden rounded-[var(--radius-xl)] p-7 sm:p-8">
        <header className="relative mb-7 flex items-center gap-3">
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
        </header>

        {/* Tabs — single persistent pill that slides between positions.
            Persistent (not conditionally rendered per button) so that the
            Create-account click — which also calls window.open and steals
            focus to the new tab — can't interrupt a mount/unmount layout
            animation and leave the pill in a broken state. */}
        <div
          role="tablist"
          aria-label="Account"
          className="relative mb-6 grid grid-cols-2 rounded-full bg-[var(--color-ink-50)] p-1 ring-1 ring-[var(--color-ink-100)] dark:ring-white/[0.06]"
        >
          {/* Slide via transform: translateX (GPU-composited) — animating
              `left`/`right` between rem and % can't interpolate cleanly and
              snaps without a visible slide. Pill width = half the inner row,
              x: 0% → 100% moves it exactly one cell over. */}
          <motion.span
            aria-hidden
            className="absolute top-1 bottom-1 left-1 rounded-full bg-[var(--color-brand-900)] shadow-[inset_0_1px_0_rgba(255,255,255,0.18),0_8px_20px_-8px_rgba(14,21,48,0.45)]"
            style={{ width: "calc(50% - 0.25rem)" }}
            initial={false}
            animate={{ x: mode === "in" ? "0%" : "100%" }}
            transition={{ type: "spring", stiffness: 380, damping: 32 }}
          />
          {(["in", "up"] as const).map((m) => {
            const active = mode === m;
            const handleClick = () => {
              if (m === "up") {
                // Open the external onboarding portal alongside switching
                // to the in-page sign-up form so the user can choose either.
                window.open(SIGNUP_URL, "_blank", "noopener,noreferrer");
              }
              handleModeChange(m);
            };
            return (
              <button
                key={m}
                type="button"
                role="tab"
                id={`${tabsId}-tab-${m}`}
                aria-selected={active}
                aria-controls={`${tabsId}-panel`}
                onClick={handleClick}
                className="relative z-10 cursor-pointer rounded-full py-2 text-[13px] font-medium tracking-tight transition-colors"
                style={{
                  color: active
                    ? "white"
                    : "var(--color-ink-500)",
                }}
              >
                <span className="relative">{m === "in" ? "Sign in" : "Create account"}</span>
              </button>
            );
          })}
        </div>

        {/* Forms */}
        <div role="tabpanel" id={`${tabsId}-panel`} aria-labelledby={`${tabsId}-tab-${mode}`}>
          <AnimatePresence mode="wait" initial={false}>
            {mode === "in" ? (
              <SignInForm
                key="in"
                status={status}
                onSubmit={handleSignInSubmit}
                onEdit={clearError}
                welcome={false}
                defaultUsername=""
                error={error}
                errorCount={errorCount}
              />
            ) : (
              // Accounts come from the company directory (sign-in is LDAP-only).
              // The in-page form here used to fake a sign-up — a timer, then
              // "Account created. Sign in with the credentials you just
              // registered" — with nothing created (review M51).
              <motion.div
                key="up"
                initial={{ opacity: 0, y: 8 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: -8 }}
                transition={{ duration: 0.35, ease }}
                className="space-y-3 rounded-2xl border border-[var(--color-ink-100)] bg-white/60 p-5 text-[13px] leading-relaxed text-[var(--color-ink-700)] dark:bg-white/[0.04]"
              >
                <p className="font-semibold text-[var(--color-ink-900)]">Accounts aren&apos;t created here</p>
                <p>
                  Sign-in uses your company (AD) account. New accounts are set up in the
                  employee onboarding portal; once yours exists, sign in with it here.
                </p>
                <button
                  type="button"
                  onClick={() => window.open(SIGNUP_URL, "_blank", "noopener,noreferrer")}
                  className="cursor-pointer font-medium text-[var(--color-brand-900)] underline decoration-[var(--color-brand-200)] underline-offset-[3px] hover:decoration-[var(--color-brand-500)] dark:text-[var(--color-brand-400)]"
                >
                  Open the onboarding portal
                </button>
              </motion.div>
            )}
          </AnimatePresence>
        </div>

        {/* Footer switcher */}
        <p className="relative mt-7 text-center text-[12.5px] text-[var(--color-ink-500)]">
          {mode === "in" ? (
            <>
              New to the fleet?{" "}
              <button
                type="button"
                onClick={() => {
                  window.open(SIGNUP_URL, "_blank", "noopener,noreferrer");
                  handleModeChange("up");
                }}
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
        </p>
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
  onEdit,
  welcome,
  defaultUsername,
  error,
  errorCount,
}: {
  status: Status;
  onSubmit: (e: React.FormEvent<HTMLFormElement>) => void;
  onEdit: () => void;
  welcome: boolean;
  defaultUsername: string;
  error: string | null;
  errorCount: number;
}) {
  // Shake the credentials once per failed attempt. Restarting the CSS
  // animation in place (drop class → reflow → re-add) rather than keying the
  // wrapper: a key change would remount the inputs and wipe what was typed.
  const credsRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const el = credsRef.current;
    if (!el || errorCount === 0) return;
    el.classList.remove("shake-x");
    void el.offsetWidth;
    el.classList.add("shake-x");
  }, [errorCount]);

  return (
    <motion.form
      onSubmit={onSubmit}
      onChange={(e) => {
        const t = e.target;
        if (t instanceof HTMLInputElement && (t.name === "username" || t.name === "password")) {
          onEdit();
        }
      }}
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

      <div
        ref={credsRef}
        className="space-y-5"
        onAnimationEnd={(e) => e.currentTarget.classList.remove("shake-x")}
      >
        <FloatingInput
          label="Username"
          type="text"
          name="username"
          autoComplete="username"
          defaultValue={defaultUsername}
          autoFocus
          required
        />
        <PasswordInput label="Password" name="password" autoComplete="current-password" required />
      </div>
      <AnimatePresence initial={false}>
        {error && (
          <motion.div
            key="error"
            initial={{ opacity: 0, y: -4, height: 0 }}
            animate={{ opacity: 1, y: 0, height: "auto" }}
            exit={{ opacity: 0, y: -4, height: 0 }}
            transition={T.base}
            className="overflow-hidden"
            role="alert"
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
      <div className="flex items-center justify-between text-[12.5px]">
        <label className="flex cursor-pointer items-center gap-2 text-[var(--color-ink-600)]">
          <CustomCheckbox name="remember" /> Remember me
        </label>
        {/* Passwords are the company (AD) account's — there is no reset
            flow in DTMS to link to. Kept as an inert hint, not a dead
            href="#" that jumps the page to the top. */}
        <button
          type="button"
          aria-disabled="true"
          title="Passwords are managed by your company (AD) account — contact IT to reset it."
          className="cursor-help font-medium text-[var(--color-ink-500)]"
        >
          Forgot password?
        </button>
      </div>
      <SubmitButton
        status={status}
        label="Take the wheel"
        submittingLabel="Routing…"
        successLabel="On the road"
      />
    </motion.form>
  );
}

/* -------------------------------------------------------------------------- */
/* Primitives                                                                  */
/* -------------------------------------------------------------------------- */

function FloatingInput({
  label,
  type = "text",
  defaultValue,
  value,
  onFocus: externalOnFocus,
  onBlur: externalOnBlur,
  onChange: externalOnChange,
  ...rest
}: React.InputHTMLAttributes<HTMLInputElement> & { label: string }) {
  const id = useId();
  const [focused, setFocused] = useState(false);
  // Seed `hasValue` from defaultValue or (controlled) value so the label
  // floats immediately when prefilled. For controlled inputs the floated
  // state then tracks `value` directly so it stays in sync without an
  // initial render flicker.
  const controlled = value !== undefined;
  const seeded =
    (typeof defaultValue === "string" && defaultValue.length > 0) ||
    (typeof value === "string" && value.length > 0);
  const [hasValue, setHasValue] = useState(seeded);
  // Browser autofill fills the field without firing onChange (Chrome won't
  // even expose the value until the user interacts), so the label would sit
  // on top of the filled text. Check `:autofill` / the value a few times
  // over the first second — when browsers apply saved credentials.
  const inputRef = useRef<HTMLInputElement>(null);
  useEffect(() => {
    if (controlled) return;
    const el = inputRef.current;
    if (!el) return;
    const check = () => {
      let filled = el.value.length > 0;
      try {
        filled ||= el.matches(":autofill");
      } catch {
        try {
          filled ||= el.matches(":-webkit-autofill");
        } catch {
          // Neither selector supported — the value check is all we have.
        }
      }
      if (filled) setHasValue(true);
    };
    check();
    const timers = [100, 400, 1000].map((ms) => window.setTimeout(check, ms));
    return () => timers.forEach((t) => window.clearTimeout(t));
  }, [controlled]);
  const floated =
    focused ||
    (controlled ? typeof value === "string" && value.length > 0 : hasValue);

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
        ref={inputRef}
        id={id}
        type={type}
        // React forbids passing both `value` and `defaultValue` — when the
        // caller controls the field, drop defaultValue.
        {...(controlled ? { value } : { defaultValue })}
        onFocus={(e) => {
          setFocused(true);
          externalOnFocus?.(e);
        }}
        onBlur={(e) => {
          setFocused(false);
          setHasValue(e.currentTarget.value.length > 0);
          externalOnBlur?.(e);
        }}
        onChange={(e) => {
          setHasValue(e.currentTarget.value.length > 0);
          externalOnChange?.(e);
        }}
        className="block w-full bg-transparent pb-2 pt-1 text-[14px] text-[var(--color-ink-900)] placeholder:text-transparent outline-none! focus:outline-none! focus-visible:outline-none! focus:[box-shadow:none]! focus-visible:[box-shadow:none]! [&::-ms-reveal]:hidden [&::-ms-clear]:hidden"
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

// A real checkbox input (visually hidden) behind the styled box: it takes
// its accessible name from the wrapping <label>, clicking the label text
// toggles it, and its value rides along in the form's FormData. Nothing
// reads "remember" yet — the session lifetime is set by the auth service.
function CustomCheckbox({ name, className = "" }: { name?: string; className?: string }) {
  const [checked, setChecked] = useState(false);
  return (
    <>
      <input
        type="checkbox"
        name={name}
        checked={checked}
        onChange={(e) => setChecked(e.currentTarget.checked)}
        className="peer sr-only"
      />
      <span
        aria-hidden
        className={`relative inline-grid h-[15px] w-[15px] cursor-pointer place-items-center rounded-[4px] transition-all peer-focus-visible:outline-2 peer-focus-visible:outline-offset-2 peer-focus-visible:outline-[var(--color-brand-500)] ${className}`}
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
    </>
  );
}

function SubmitButton({
  status,
  label,
  submittingLabel = "Routing…",
  successLabel = "On the road",
  disabled = false,
}: {
  status: Status;
  label: string;
  submittingLabel?: string;
  successLabel?: string;
  disabled?: boolean;
}) {
  const submitting = status === "submitting";
  const success = status === "success";
  return (
    <button
      type="submit"
      disabled={status !== "idle" || disabled}
      className="group relative flex h-12 w-full cursor-pointer items-center justify-center overflow-hidden rounded-full bg-[var(--color-brand-900)] pl-1.5 pr-1.5 text-[14px] font-semibold text-white shadow-[inset_0_1px_0_rgba(255,255,255,0.15),0_14px_30px_-12px_rgba(14,21,48,0.6)] transition-transform disabled:cursor-not-allowed disabled:opacity-60"
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

