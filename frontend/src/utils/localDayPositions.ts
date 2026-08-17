import { todayIstDate } from "./signalDayHistory";

/** Local paper-book rows for sources without a backend openPosition mutation (options). */
export type LocalDayPosition = {
  id: string;
  scope: string;
  symbol: string;
  instrumentName: string;
  side: string;
  quantityLots: number;
  entryPrice: number;
  currentStopLoss: number;
  lastPrice?: number | null;
  computedUnrealizedPnl?: number | null;
  notes?: string | null;
  tradedAt: string;
};

type Bucket = {
  date: string;
  open: LocalDayPosition[];
};

const STORAGE_KEY = "syn.localDayPositions";

function readBucket(): Bucket {
  const today = todayIstDate();
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return { date: today, open: [] };
    const parsed = JSON.parse(raw) as Bucket;
    if (!parsed || parsed.date !== today || !Array.isArray(parsed.open)) {
      return { date: today, open: [] };
    }
    return parsed;
  } catch {
    return { date: today, open: [] };
  }
}

function writeBucket(bucket: Bucket) {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(bucket));
}

export function listLocalDayPositions(): LocalDayPosition[] {
  return readBucket().open;
}

export function addLocalDayPosition(
  input: Omit<LocalDayPosition, "tradedAt"> & { tradedAt?: string },
): LocalDayPosition {
  const bucket = readBucket();
  const today = todayIstDate();
  if (bucket.date !== today) {
    bucket.date = today;
    bucket.open = [];
  }
  const existing = bucket.open.findIndex((p) => p.id === input.id);
  const row: LocalDayPosition = {
    ...input,
    tradedAt: input.tradedAt ?? new Date().toISOString(),
  };
  if (existing >= 0) bucket.open[existing] = row;
  else bucket.open.unshift(row);
  writeBucket(bucket);
  return row;
}

export function closeLocalDayPosition(id: string): boolean {
  const bucket = readBucket();
  const next = bucket.open.filter((p) => p.id !== id);
  if (next.length === bucket.open.length) return false;
  bucket.open = next;
  writeBucket(bucket);
  return true;
}

export function closeLocalDayPositionsByIds(ids: string[]): number {
  if (ids.length === 0) return 0;
  const remove = new Set(ids);
  const bucket = readBucket();
  const next = bucket.open.filter((p) => !remove.has(p.id));
  const removed = bucket.open.length - next.length;
  if (removed > 0) {
    bucket.open = next;
    writeBucket(bucket);
  }
  return removed;
}
