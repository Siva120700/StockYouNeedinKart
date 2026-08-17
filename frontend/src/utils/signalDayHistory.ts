/** IST calendar day (YYYY-MM-DD) for market-session history buckets. */
export function todayIstDate(): string {
  return new Intl.DateTimeFormat("en-CA", {
    timeZone: "Asia/Kolkata",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(new Date());
}

export function formatIstTime(iso: string | null | undefined): string {
  if (!iso) return "";
  try {
    return new Date(iso).toLocaleTimeString("en-IN", {
      timeZone: "Asia/Kolkata",
      hour: "2-digit",
      minute: "2-digit",
    });
  } catch {
    return "";
  }
}

export type SignalDayKeyFields = {
  id: string;
  instrumentId: string;
  side: string;
};

export type SignalDayKeyFn<T extends SignalDayKeyFields> = (row: T) => string;

/** Frozen first-seen snapshot for a signal that appeared today. */
export type SignalDayEntry<T extends SignalDayKeyFields> = T & {
  firstSeenAt: string;
  disappearedAt?: string | null;
  tradedAt?: string | null;
};

type DayBucket<T extends SignalDayKeyFields> = {
  date: string;
  byKey: Record<string, SignalDayEntry<T>>;
};

function storageKey(scope: string): string {
  return `syn.signalDayHistory.${scope}`;
}

export function defaultSignalDayKey(row: SignalDayKeyFields): string {
  return `${row.instrumentId}:${String(row.side).toLowerCase()}`;
}

/** @deprecated use defaultSignalDayKey */
export const signalDayKey = defaultSignalDayKey;

function readBucket<T extends SignalDayKeyFields>(scope: string): DayBucket<T> {
  const today = todayIstDate();
  try {
    const raw = localStorage.getItem(storageKey(scope));
    if (!raw) return { date: today, byKey: {} };
    const parsed = JSON.parse(raw) as DayBucket<T>;
    if (!parsed || parsed.date !== today || typeof parsed.byKey !== "object") {
      return { date: today, byKey: {} };
    }
    return parsed;
  } catch {
    return { date: today, byKey: {} };
  }
}

function writeBucket<T extends SignalDayKeyFields>(scope: string, bucket: DayBucket<T>) {
  localStorage.setItem(storageKey(scope), JSON.stringify(bucket));
}

export type SyncSignalDayResult<T extends SignalDayKeyFields> = {
  activeKeys: Set<string>;
  history: SignalDayEntry<T>[];
  traded: SignalDayEntry<T>[];
};

/**
 * Merge live signals into today's store.
 * First sighting is frozen (entry/SL/targets never updated).
 * Rows missing from live become History; Trade marks stay until next IST day.
 */
export function syncSignalDayHistory<T extends SignalDayKeyFields>(
  scope: string,
  live: T[],
  getKey: SignalDayKeyFn<T> = defaultSignalDayKey,
): SyncSignalDayResult<T> {
  const today = todayIstDate();
  const bucket = readBucket<T>(scope);
  if (bucket.date !== today) {
    bucket.date = today;
    bucket.byKey = {};
  }

  const now = new Date().toISOString();
  const activeKeys = new Set(live.map((r) => getKey(r)));

  for (const row of live) {
    const key = getKey(row);
    const existing = bucket.byKey[key];
    if (!existing) {
      bucket.byKey[key] = {
        ...row,
        firstSeenAt: now,
        disappearedAt: null,
        tradedAt: null,
      };
    } else {
      bucket.byKey[key] = {
        ...existing,
        disappearedAt: null,
        id: existing.tradedAt ? existing.id : row.id,
      };
    }
  }

  for (const [key, entry] of Object.entries(bucket.byKey)) {
    if (!activeKeys.has(key) && !entry.disappearedAt) {
      bucket.byKey[key] = { ...entry, disappearedAt: now };
    }
  }

  writeBucket(scope, bucket);

  const all = Object.values(bucket.byKey);
  const history = all
    .filter((e) => e.disappearedAt && !activeKeys.has(getKey(e)))
    .sort((a, b) => String(b.disappearedAt).localeCompare(String(a.disappearedAt)));
  const traded = all
    .filter((e) => e.tradedAt)
    .sort((a, b) => String(b.tradedAt).localeCompare(String(a.tradedAt)));

  return { activeKeys, history, traded };
}

export function markSignalDayTraded<T extends SignalDayKeyFields>(
  scope: string,
  row: T,
  getKey: SignalDayKeyFn<T> = defaultSignalDayKey,
): SignalDayEntry<T> | null {
  const bucket = readBucket<T>(scope);
  const key = getKey(row);
  const now = new Date().toISOString();
  const existing = bucket.byKey[key];
  const entry: SignalDayEntry<T> = existing
    ? { ...existing, tradedAt: existing.tradedAt ?? now }
    : { ...row, firstSeenAt: now, disappearedAt: null, tradedAt: now };
  bucket.byKey[key] = entry;
  writeBucket(scope, bucket);
  return entry;
}

export function isSignalDayTraded<T extends SignalDayKeyFields>(
  scope: string,
  row: T,
  getKey: SignalDayKeyFn<T> = defaultSignalDayKey,
): boolean {
  const entry = readBucket<T>(scope).byKey[getKey(row)];
  return Boolean(entry?.tradedAt);
}

/** Clear Trade marks for the given rows (removes them from the Traded tab). */
export function unmarkSignalDayTraded<T extends SignalDayKeyFields>(
  scope: string,
  rows: T[],
  getKey: SignalDayKeyFn<T> = defaultSignalDayKey,
): number {
  if (rows.length === 0) return 0;
  const bucket = readBucket<T>(scope);
  let cleared = 0;
  for (const row of rows) {
    const key = getKey(row);
    const existing = bucket.byKey[key];
    if (!existing?.tradedAt) continue;
    bucket.byKey[key] = { ...existing, tradedAt: null };
    cleared += 1;
  }
  if (cleared > 0) writeBucket(scope, bucket);
  return cleared;
}

/** Re-read today's traded/history lists without changing live sync state. */
export function listSignalDayHistory<T extends SignalDayKeyFields>(
  scope: string,
  live: T[],
  getKey: SignalDayKeyFn<T> = defaultSignalDayKey,
): SyncSignalDayResult<T> {
  return syncSignalDayHistory(scope, live, getKey);
}

export type SignalsTab = "active" | "history" | "traded";
