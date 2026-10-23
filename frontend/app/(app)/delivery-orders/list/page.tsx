import { OrdersExperience } from "@/components/delivery-orders/orders-experience";

export const metadata = {
  title: "Delivery orders · TMS",
  description: "All active & past delivery orders.",
};

export default function DeliveryOrderListPage() {
  return (
    <>
      <main className="layer-content mx-auto max-w-[1340px] px-4 pb-32 pt-28 sm:px-6 md:px-6 md:pt-32 lg:pl-[var(--rail-width,80px)] lg:pr-6">
        <OrdersExperience />
      </main>
    </>
  );
}
