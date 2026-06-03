import { BentoFeatures } from "@/components/landing/bento-features";
import { CtaBanner } from "@/components/landing/cta-banner";
import { LandingFooter } from "@/components/landing/footer";
import { LandingHero } from "@/components/landing/hero";
import { LandingIntro } from "@/components/landing/landing-intro";
import { Platforms } from "@/components/landing/platforms";
import { Testimonials } from "@/components/landing/testimonials";
import { LandingTopNav } from "@/components/landing/top-nav";

export default function LandingPage() {
  return (
    <div className="layer-content">
      {/* Boot-sequence overlay — runs every entry to /, dismisses itself. */}
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
