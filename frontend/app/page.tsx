import { BentoFeatures } from "@/components/landing/bento-features";
import { CtaBanner } from "@/components/landing/cta-banner";
import { LandingFooter } from "@/components/landing/footer";
import { LandingHero } from "@/components/landing/hero";
import { LandingIntro } from "@/components/landing/landing-intro";
import { Platforms } from "@/components/landing/platforms";
import { Testimonials } from "@/components/landing/testimonials";
import { LandingTopNav } from "@/components/landing/top-nav";

// Runs during HTML parse, before first paint: if the boot intro already
// played this session, flag <html> so the SSR'd overlay is display:none from
// the first frame instead of flashing until hydration hides it. Key must
// match INTRO_SEEN_KEY in components/landing/landing-intro.tsx (a constant
// exported from a "use client" module can't be read here).
const INTRO_SEEN_KEY = "dtms.landing-intro.seen";
const introSeenScript = `try{if(sessionStorage.getItem("${INTRO_SEEN_KEY}")==="1")document.documentElement.setAttribute("data-intro-seen","")}catch(e){}`;

export default function LandingPage() {
  return (
    <div className="layer-content">
      <script dangerouslySetInnerHTML={{ __html: introSeenScript }} />
      {/* Boot-sequence overlay — once per session, skippable, dismisses itself. */}
      <LandingIntro />
      <LandingTopNav />
      <LandingHero />
      <BentoFeatures />
      <Testimonials />
      <Platforms />
      <CtaBanner />
      <LandingFooter />
    </div>
  );
}
