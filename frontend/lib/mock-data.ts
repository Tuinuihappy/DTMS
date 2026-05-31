export type ShipmentTier = "sky" | "lavender" | "peach";

export type PriorityShipment = {
  id: string;
  tier: ShipmentTier;
  route: string;
  origin: string;
  destination: string;
  etaHours: number;
  distanceKm: number;
  progressKm: number;
  drivers: { name: string; avatarHue: number }[];
  cargo: string;
  badge: string;
};

export type DayActivity = {
  day: string;
  shipments: number;
  value: number;
};

export type Driver = {
  name: string;
  role: string;
  region: string;
  score: number;
  trend: number;
  avatarHue: number;
};

export type FunnelStep = {
  label: string;
  count: number;
  color: "ink" | "amber" | "coral";
};

export const weekActivity: DayActivity[] = [
  { day: "M", shipments: 58, value: 18420 },
  { day: "T", shipments: 72, value: 22100 },
  { day: "W", shipments: 84, value: 28950 },
  { day: "T", shipments: 67, value: 19840 },
  { day: "F", shipments: 91, value: 31280 },
  { day: "S", shipments: 44, value: 12600 },
  { day: "S", shipments: 38, value: 9870 },
];

export const peakDayIndex = 4;

export const priorityShipments: PriorityShipment[] = [
  {
    id: "SHP-2841",
    tier: "sky",
    route: "Eastern Seaboard",
    origin: "Bangkok Port",
    destination: "Map Ta Phut",
    etaHours: 4.2,
    distanceKm: 220,
    progressKm: 132,
    drivers: [
      { name: "Niran Suk", avatarHue: 18 },
      { name: "Alisa Wong", avatarHue: 220 },
      { name: "Tan Wei", avatarHue: 145 },
    ],
    cargo: "Petrochemical · 24 ton",
    badge: "Critical",
  },
  {
    id: "SHP-2849",
    tier: "lavender",
    route: "Northern Corridor",
    origin: "Lat Krabang ICD",
    destination: "Lampang DC",
    etaHours: 9.5,
    distanceKm: 615,
    progressKm: 240,
    drivers: [
      { name: "Marisa P.", avatarHue: 280 },
      { name: "Kenji O.", avatarHue: 30 },
    ],
    cargo: "Automotive parts · 18 ton",
    badge: "High",
  },
  {
    id: "SHP-2856",
    tier: "peach",
    route: "Southern Bypass",
    origin: "Bang Sue Hub",
    destination: "Hat Yai DC",
    etaHours: 14.0,
    distanceKm: 950,
    progressKm: 380,
    drivers: [
      { name: "Suchart R.", avatarHue: 12 },
      { name: "Pavel I.", avatarHue: 200 },
      { name: "Mai Linh", avatarHue: 320 },
    ],
    cargo: "Cold chain · 12 ton",
    badge: "High",
  },
];

export const topDrivers: Driver[] = [
  {
    name: "Niran Sukhothai",
    role: "Senior dispatcher",
    region: "Eastern Seaboard",
    score: 982,
    trend: 6.4,
    avatarHue: 18,
  },
  {
    name: "Marisa Petchara",
    role: "Long-haul lead",
    region: "Northern corridor",
    score: 941,
    trend: 3.1,
    avatarHue: 280,
  },
  {
    name: "Kenji Otsuka",
    role: "Cold chain",
    region: "Southern bypass",
    score: 918,
    trend: -1.2,
    avatarHue: 30,
  },
  {
    name: "Alisa Wong",
    role: "Port relay",
    region: "Laem Chabang",
    score: 902,
    trend: 4.8,
    avatarHue: 220,
  },
];

export const dispatchFunnel: FunnelStep[] = [
  { label: "Requests", count: 312, color: "ink" },
  { label: "Quoted", count: 248, color: "amber" },
  { label: "Confirmed", count: 196, color: "ink" },
  { label: "Dispatched", count: 174, color: "amber" },
  { label: "Delivered", count: 158, color: "coral" },
];
