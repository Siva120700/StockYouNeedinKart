import { useEffect, useMemo, useState } from "react";
import {
  Alert,
  Box,
  Button,
  Chip,
  Collapse,
  MenuItem,
  Stack,
  Tab,
  Tabs,
  TextField,
  Typography,
} from "@mui/material";
import { CaretUp, Handshake, Play } from "@phosphor-icons/react";
import { columnFactories } from "../../zen_components/table/columnFactories";
import type { ColumnConfig } from "../../zen_components/table/columnTypes";
import ZenTable from "../../zen_components/table/ZenTable";
import { useZenPrimaryLayoutContext } from "../../zen_components/layout/ZenPrimaryLayoutProvider";
import PageFrame, { ScrollPane } from "../../zen_components/layout/PageFrame";
import { DEFAULT_SMALL_ICON_SIZE } from "../../constants";
import { IndexOptionsApi } from "./api";
import type { NiftyOptionChainSnapshot, NiftyOrbRecommendation } from "./types";
import {
  loadHistoricalHitRates,
  type HitRateByInstrument,
} from "../../utils/historicalHitRate";
import { addLocalDayPosition, closeLocalDayPositionsByIds } from "../../utils/localDayPositions";
import {
  formatIstTime,
  isSignalDayTraded,
  markSignalDayTraded,
  syncSignalDayHistory,
  unmarkSignalDayTraded,
  type SignalDayEntry,
  type SignalsTab,
} from "../../utils/signalDayHistory";
import TradedDeleteBar from "../../zen_components/shared/TradedDeleteBar";

const HISTORY_SCOPE = "index_options";
const SOURCE_ORB = "nifty_orb";
const SOURCE_COMBO = "nifty_orb_liq_v2";
const SOURCE_LIQ_BO = "nifty_liq_breakout";
const SOURCE_BRK_VOL = "nifty_breakout_volume";
const SOURCE_BRK_CHAIN = "nifty_breakout_chain";
const SOURCE_HERO_ZERO = "nifty_hero_zero";
const POLL_MS = 45_000;

function indexOptionsDayKey(r: NiftyOrbRecommendation): string {
  return [
    r.signalSource || SOURCE_ORB,
    String(r.side).toLowerCase(),
    r.contractStrike ?? "",
    r.contractOptionType ?? "",
    r.contractTradingSymbol ?? r.id,
  ].join(":");
}

function isMarketHoursIst(): boolean {
  const ist = new Date(
    new Date().toLocaleString("en-US", { timeZone: "Asia/Kolkata" }),
  );
  const day = ist.getDay();
  if (day === 0 || day === 6) return false;
  const mins = ist.getHours() * 60 + ist.getMinutes();
  return mins >= 9 * 60 + 10 && mins <= 15 * 60 + 35;
}

function fmt(n: number | null | undefined, d = 2): string {
  if (n == null || !Number.isFinite(Number(n))) return "—";
  return Number(n).toFixed(d);
}

function fmtDelta(n: number | null | undefined, d = 3): string {
  if (n == null || !Number.isFinite(Number(n))) return "—";
  return Math.abs(Number(n)).toFixed(d);
}

function sourceLabel(source: string): string {
  if (source === SOURCE_COMBO) return "ORB + Liquidity V2";
  if (source === SOURCE_LIQ_BO) return "Liquidity + Breakout";
  if (source === SOURCE_BRK_VOL) return "Breakout + Volume";
  if (source === SOURCE_BRK_CHAIN) return "Breakout + Chain";
  if (source === SOURCE_HERO_ZERO) return "Hero Zero";
  return "Nifty ORB";
}

function buildColumns(
  tab: SignalsTab,
  onTrade: (row: NiftyOrbRecommendation) => void,
): ColumnConfig<NiftyOrbRecommendation>[] {
  const cols: ColumnConfig<NiftyOrbRecommendation>[] = [
    columnFactories.createTextColumn<NiftyOrbRecommendation>({
      field: "signalSource",
      headerName: "Source",
      width: 140,
      getValue: (r) => sourceLabel(r.signalSource || SOURCE_ORB),
    }),
    columnFactories.createTextColumn<NiftyOrbRecommendation>({
      field: "side",
      headerName: "Side",
      width: 70,
      getValue: (r) => r.side,
    }),
    columnFactories.createTextColumn<NiftyOrbRecommendation>({
      field: "contractStrike",
      headerName: "Strike",
      width: 110,
      getValue: (r) =>
        r.contractStrike != null
          ? `${fmt(r.contractStrike, 0)} ${r.contractOptionType ?? ""}`.trim()
          : "—",
    }),
    columnFactories.createTextColumn<NiftyOrbRecommendation>({
      field: "premiumLtp",
      headerName: "Prem Entry",
      width: 100,
      getValue: (r) => fmt(r.premiumLtp),
    }),
    columnFactories.createTextColumn<NiftyOrbRecommendation>({
      field: "premiumStopLoss",
      headerName: "Prem SL",
      width: 90,
      getValue: (r) => fmt(r.premiumStopLoss),
    }),
    columnFactories.createTextColumn<NiftyOrbRecommendation>({
      field: "premiumTargetT1",
      headerName: "Prem T1",
      width: 90,
      getValue: (r) => fmt(r.premiumTargetT1),
    }),
    columnFactories.createTextColumn<NiftyOrbRecommendation>({
      field: "contractTradingSymbol",
      headerName: "Contract",
      width: 170,
      getValue: (r) => r.contractTradingSymbol ?? "—",
    }),
    columnFactories.createTextColumn<NiftyOrbRecommendation>({
      field: "spotLtp",
      headerName: "Spot",
      width: 90,
      getValue: (r) => fmt(r.spotLtp),
    }),
    columnFactories.createTextColumn<NiftyOrbRecommendation>({
      field: "underlyingEntry",
      headerName: "Nifty Entry",
      width: 100,
      getValue: (r) => fmt(r.underlyingEntry),
    }),
    columnFactories.createTextColumn<NiftyOrbRecommendation>({
      field: "underlyingStopLoss",
      headerName: "Nifty SL",
      width: 90,
      getValue: (r) => fmt(r.underlyingStopLoss),
    }),
    columnFactories.createTextColumn<NiftyOrbRecommendation>({
      field: "underlyingTargetT1",
      headerName: "Nifty T1",
      width: 90,
      getValue: (r) => fmt(r.underlyingTargetT1),
    }),
    columnFactories.createTextColumn<NiftyOrbRecommendation>({
      field: "delta",
      headerName: "Δ",
      width: 70,
      getValue: (r) => fmtDelta(r.delta, 3),
    }),
    columnFactories.createTextColumn<NiftyOrbRecommendation>({
      field: "status",
      headerName: "Status",
      width: 110,
      getValue: (r) => r.status,
    }),
  ];

  if (tab === "history") {
    cols.push(
      columnFactories.createTextColumn<NiftyOrbRecommendation>({
        field: "disappearedAt",
        headerName: "Left",
        width: 90,
        getValue: (r) =>
          formatIstTime((r as SignalDayEntry<NiftyOrbRecommendation>).disappearedAt),
      }),
    );
  }
  if (tab === "traded") {
    cols.push(
      columnFactories.createTextColumn<NiftyOrbRecommendation>({
        field: "tradedAt",
        headerName: "Traded",
        width: 90,
        getValue: (r) =>
          formatIstTime((r as SignalDayEntry<NiftyOrbRecommendation>).tradedAt),
      }),
    );
  }
  if (tab !== "traded") {
    cols.push(
      columnFactories.createActionColumn<NiftyOrbRecommendation>(
        (row) => [
          {
            icon: <Handshake size={DEFAULT_SMALL_ICON_SIZE} />,
            tooltip: isSignalDayTraded(HISTORY_SCOPE, row, indexOptionsDayKey)
              ? "Already traded"
              : "Trade — open in Positions",
            disabled: () => isSignalDayTraded(HISTORY_SCOPE, row, indexOptionsDayKey),
            onClick: (r) => onTrade(r),
          },
        ],
        { field: "actions", headerName: "Trade", width: 80 },
      ),
    );
  }
  return cols;
}

function DetailPanel({
  row,
  onClose,
}: {
  row: NiftyOrbRecommendation;
  onClose: () => void;
}) {
  const isCombo = row.signalSource === SOURCE_COMBO;
  const isLiqBo = row.signalSource === SOURCE_LIQ_BO;
  const isBrkVol = row.signalSource === SOURCE_BRK_VOL;
  const isHeroZero = row.signalSource === SOURCE_HERO_ZERO;
  return (
    <Box
      sx={{
        p: 2,
        border: "1px solid",
        borderColor: "divider",
        borderRadius: 1,
        bgcolor: "background.paper",
      }}
    >
      <Stack direction="row" alignItems="center" spacing={1} mb={1}>
        <Typography variant="h6">NIFTY {row.side.toUpperCase()}</Typography>
        <Chip size="small" label={sourceLabel(row.signalSource)} />
        <Chip
          size="small"
          color={
            row.status === "recommended"
              ? "success"
              : row.status === "waiting"
                ? "warning"
                : "default"
          }
          label={row.status}
        />
        <Button
          size="small"
          endIcon={<CaretUp size={DEFAULT_SMALL_ICON_SIZE} />}
          onClick={onClose}
        >
          Close
        </Button>
      </Stack>
      <Typography variant="body2" mb={1}>
        {row.status === "recommended" && row.contractTradingSymbol
          ? `Buy ${row.contractStrike != null ? `${fmt(row.contractStrike, 0)} ${row.contractOptionType ?? ""}` : row.contractTradingSymbol} @ ₹${fmt(row.premiumLtp)} | Prem SL ₹${fmt(row.premiumStopLoss)} | Prem T1 ₹${fmt(row.premiumTargetT1)} | Flat by ${row.flatByIst}`
          : (row.skipReason ?? row.status)}
      </Typography>
      <Stack direction={{ xs: "column", md: "row" }} spacing={4}>
        <Box flex={1}>
          <Typography variant="subtitle2" gutterBottom>
            Option ticket (buy)
          </Typography>
          <Typography variant="body2">
            Strike:{" "}
            {row.contractStrike != null
              ? `${fmt(row.contractStrike, 0)} ${row.contractOptionType ?? ""}`
              : "—"}
          </Typography>
          <Typography variant="body2">
            Contract: {row.contractTradingSymbol ?? "—"}
          </Typography>
          <Typography variant="body2">
            Entry (premium): ₹{fmt(row.premiumLtp)}
          </Typography>
          <Typography variant="body2">
            SL (premium): ₹{fmt(row.premiumStopLoss)}
          </Typography>
          <Typography variant="body2">
            T1 / T2 / T3 (premium): ₹{fmt(row.premiumTargetT1)} / ₹
            {fmt(row.premiumTargetT2)} / ₹{fmt(row.premiumTargetT3)}
          </Typography>
          <Typography variant="body2">
            Expiry: {row.contractExpiryLabel ?? "—"} | Lot:{" "}
            {row.contractLotSize ?? "—"} | Δ {fmtDelta(row.delta, 3)}
          </Typography>
          <Typography variant="body2">
            ATM alt: {row.altTradingSymbol ?? "—"} @ ₹{fmt(row.altPremiumLtp)}
          </Typography>
        </Box>
        <Box flex={1}>
          <Typography variant="subtitle2" gutterBottom>
            {isLiqBo
              ? "Nifty levels vs strike chart (ticket = strike premium)"
              : isBrkVol
                ? "Nifty Breakout + volume (Δ × Nifty option ticket)"
                : isHeroZero
                  ? "Hero Zero — far OTM lottery (full premium at risk)"
                  : isCombo
                ? "Composed Nifty levels (ORB + Liq V2)"
                : "Nifty ORB (structure)"}
          </Typography>
          <Typography variant="body2">Spot: {fmt(row.spotLtp)}</Typography>
          {!isLiqBo && !isBrkVol && !isHeroZero && (
            <Typography variant="body2">
              OR: {fmt(row.orbLow)} – {fmt(row.orbHigh)} ({fmt(row.orbRange, 1)} pts)
            </Typography>
          )}
          <Typography variant="body2">Entry: {fmt(row.underlyingEntry)}</Typography>
          <Typography variant="body2">SL: {fmt(row.underlyingStopLoss)}</Typography>
          <Typography variant="body2">
            T1 / T2 / T3: {fmt(row.underlyingTargetT1)} /{" "}
            {fmt(row.underlyingTargetT2)} / {fmt(row.underlyingTargetT3)}
          </Typography>
        </Box>
      </Stack>
      <Stack direction="row" flexWrap="wrap" gap={0.5} mt={1}>
        {(row.reasons ?? []).map((x) => (
          <Chip key={x} size="small" label={x} variant="outlined" />
        ))}
      </Stack>
    </Box>
  );
}

function ChainPanel({ chain }: { chain: NiftyOptionChainSnapshot | null }) {
  if (!chain) {
    return (
      <Alert severity="info">
        Option chain loads with Run / refresh. PCR and OI walls gate Breakout + Chain tickets.
      </Alert>
    );
  }
  if (!chain.usable) {
    return (
      <Alert severity="warning">
        Nearest-expiry Nifty OI ladder thin or unavailable
        {chain.expiryLabel ? ` (${chain.expiryLabel})` : ""}. Breakout + Chain will skip until OI
        quotes fill in.
      </Alert>
    );
  }
  const top = [...chain.ladder]
    .sort((a, b) => Math.max(b.callOi, b.putOi) - Math.max(a.callOi, a.putOi))
    .slice(0, 8);
  return (
    <Box
      sx={{
        p: 1.5,
        border: "1px solid",
        borderColor: "divider",
        borderRadius: 1,
      }}
    >
      <Typography variant="subtitle1" fontWeight={700} gutterBottom>
        Nifty option chain · {chain.expiryLabel || "nearest"} · spot {fmt(chain.spot, 0)}
      </Typography>
      <Stack direction="row" flexWrap="wrap" gap={1} mb={1}>
        <Chip size="small" label={`PCR ${chain.pcr != null ? chain.pcr.toFixed(2) : "—"}`} />
        <Chip
          size="small"
          color="success"
          variant="outlined"
          label={`Put wall ${fmt(chain.putWallStrike, 0)} (${chain.putWallOi.toLocaleString()})`}
        />
        <Chip
          size="small"
          color="error"
          variant="outlined"
          label={`Call wall ${fmt(chain.callWallStrike, 0)} (${chain.callWallOi.toLocaleString()})`}
        />
        <Chip
          size="small"
          variant="outlined"
          label={`Max pain ${fmt(chain.maxPainStrike, 0)}`}
        />
      </Stack>
      <Typography variant="caption" color="text.secondary" display="block" mb={0.5}>
        Top OI strikes (context for Breakout + Chain filter)
      </Typography>
      <Stack direction="row" flexWrap="wrap" gap={0.75}>
        {top.map((r) => (
          <Chip
            key={r.strike}
            size="small"
            variant="outlined"
            label={`${fmt(r.strike, 0)} C ${r.callOi.toLocaleString()} / P ${r.putOi.toLocaleString()}`}
          />
        ))}
      </Stack>
    </Box>
  );
}

function SectionTable({
  title,
  blurb,
  rows,
  columns,
  loading,
  expandedId,
  onExpand,
  enableSelection = false,
  selectedRowIds,
  onSelectedRowIdsChange,
  getRowId = (r) => r.id,
}: {
  title: string;
  blurb: string;
  rows: NiftyOrbRecommendation[];
  columns: ColumnConfig<NiftyOrbRecommendation>[];
  loading: boolean;
  expandedId: string | null;
  onExpand: (id: string | null) => void;
  enableSelection?: boolean;
  selectedRowIds?: string[];
  onSelectedRowIdsChange?: (ids: string[]) => void;
  getRowId?: (row: NiftyOrbRecommendation) => string;
}) {
  const expanded = rows.find((r) => r.id === expandedId) ?? null;
  return (
    <Stack spacing={1.5}>
      <Box>
        <Typography variant="h6">{title}</Typography>
        <Typography variant="body2" color="text.secondary">
          {blurb}
        </Typography>
      </Box>
      <ZenTable
        rows={rows}
        columns={columns}
        loading={loading}
        getRowId={getRowId}
        onRowClick={(r) => onExpand(expandedId === r.id ? null : r.id)}
        enableSelection={enableSelection}
        selectedRowIds={selectedRowIds}
        onSelectedRowIdsChange={onSelectedRowIdsChange}
      />
      <Collapse in={!!expanded}>
        {expanded && (
          <DetailPanel row={expanded} onClose={() => onExpand(null)} />
        )}
      </Collapse>
    </Stack>
  );
}

export default function IndexOptionsPage() {
  const { setTitle, setBreadcrumbs, setPageActions, setIsSyncing } =
    useZenPrimaryLayoutContext();
  const [rows, setRows] = useState<NiftyOrbRecommendation[]>([]);
  const [historyRows, setHistoryRows] = useState<
    SignalDayEntry<NiftyOrbRecommendation>[]
  >([]);
  const [tradedRows, setTradedRows] = useState<
    SignalDayEntry<NiftyOrbRecommendation>[]
  >([]);
  const [tab, setTab] = useState<SignalsTab>("active");
  const [selectedTradedIds, setSelectedTradedIds] = useState<string[]>([]);
  const [, setHitRates] = useState<HitRateByInstrument>(() => new Map());
  const [loading, setLoading] = useState(true);
  const [running, setRunning] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<"all" | "recommended" | "waiting" | "skipped">(
    "all",
  );
  const [expandedOrbId, setExpandedOrbId] = useState<string | null>(null);
  const [expandedComboId, setExpandedComboId] = useState<string | null>(null);
  const [expandedLiqBoId, setExpandedLiqBoId] = useState<string | null>(null);
  const [expandedBrkVolId, setExpandedBrkVolId] = useState<string | null>(null);
  const [expandedBrkChainId, setExpandedBrkChainId] = useState<string | null>(null);
  const [expandedHeroZeroId, setExpandedHeroZeroId] = useState<string | null>(null);
  const [expandedHistoryId, setExpandedHistoryId] = useState<string | null>(null);
  const [chain, setChain] = useState<NiftyOptionChainSnapshot | null>(null);

  function applyHistorySync(recs: NiftyOrbRecommendation[]) {
    const synced = syncSignalDayHistory(HISTORY_SCOPE, recs, indexOptionsDayKey);
    setHistoryRows(synced.history);
    setTradedRows(synced.traded);
  }

  async function refresh(silent = false) {
    if (!silent) setError(null);
    if (!silent) setIsSyncing(true);
    try {
      const [
        recs,
        ratesOrb,
        ratesCombo,
        ratesLiqBo,
        ratesBrkVol,
        ratesBrkChain,
        ratesHeroZero,
        chainSnap,
      ] = await Promise.all([
        IndexOptionsApi.fetchRecommendations(),
        loadHistoricalHitRates("nifty_orb"),
        loadHistoricalHitRates("nifty_orb_liq_v2"),
        loadHistoricalHitRates("nifty_liq_breakout"),
        loadHistoricalHitRates("nifty_breakout_volume"),
        loadHistoricalHitRates("nifty_breakout_chain"),
        loadHistoricalHitRates("nifty_hero_zero"),
        IndexOptionsApi.fetchOptionChain().catch(() => null),
      ]);
      setRows(recs);
      applyHistorySync(recs);
      const merged = new Map(ratesOrb);
      for (const [k, v] of ratesCombo) merged.set(k, v);
      for (const [k, v] of ratesLiqBo) merged.set(k, v);
      for (const [k, v] of ratesBrkVol) merged.set(k, v);
      for (const [k, v] of ratesBrkChain) merged.set(k, v);
      for (const [k, v] of ratesHeroZero) merged.set(k, v);
      setHitRates(merged);
      if (chainSnap) setChain(chainSnap);
    } catch (e) {
      if (!silent) setError(e instanceof Error ? e.message : String(e));
    } finally {
      setLoading(false);
      if (!silent) setIsSyncing(false);
    }
  }

  async function onRun() {
    setRunning(true);
    setError(null);
    try {
      await IndexOptionsApi.runAnalysis();
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setRunning(false);
    }
  }

  function onTrade(row: NiftyOrbRecommendation) {
    setError(null);
    setInfo(null);
    const premium = Number(row.premiumLtp);
    const entry =
      Number.isFinite(premium) && premium > 0 ? premium : Number(row.underlyingEntry);
    const slRaw = Number(row.premiumStopLoss);
    const sl = Number.isFinite(slRaw) && slRaw > 0 ? slRaw : entry * 0.5;
    markSignalDayTraded(HISTORY_SCOPE, row, indexOptionsDayKey);
    addLocalDayPosition({
      id: `idx-opt-${row.id}`,
      scope: HISTORY_SCOPE,
      symbol: row.contractTradingSymbol ?? `NIFTY ${fmt(row.contractStrike, 0)}`,
      instrumentName: `${sourceLabel(row.signalSource || SOURCE_ORB)} · index options`,
      side: "buy",
      quantityLots: 1,
      entryPrice: entry,
      currentStopLoss: sl,
      lastPrice: entry,
      notes: `Prem T1 ${fmt(row.premiumTargetT1)} · Nifty SL ${fmt(row.underlyingStopLoss)} · flat ${row.flatByIst}`,
    });
    setInfo(`${row.contractTradingSymbol ?? "NIFTY"} moved to Positions (Traded).`);
    applyHistorySync(rows);
  }

  function tradedRowId(r: NiftyOrbRecommendation) {
    return `${indexOptionsDayKey(r)}:${r.id}`;
  }

  function onDeleteSelectedTraded() {
    const selected = tradedRows.filter((r) => selectedTradedIds.includes(tradedRowId(r)));
    if (selected.length === 0) return;
    unmarkSignalDayTraded(HISTORY_SCOPE, selected, indexOptionsDayKey);
    closeLocalDayPositionsByIds(selected.map((r) => `idx-opt-${r.id}`));
    setSelectedTradedIds([]);
    applyHistorySync(rows);
    setInfo(`Removed ${selected.length} from Traded.`);
  }

  const orbRows = useMemo(() => {
    const list = rows.filter((r) => (r.signalSource || SOURCE_ORB) === SOURCE_ORB);
    if (statusFilter === "all") return list;
    return list.filter((r) => r.status === statusFilter);
  }, [rows, statusFilter]);

  const comboRows = useMemo(() => {
    const list = rows.filter((r) => r.signalSource === SOURCE_COMBO);
    if (statusFilter === "all") return list;
    return list.filter((r) => r.status === statusFilter);
  }, [rows, statusFilter]);

  const liqBoRows = useMemo(() => {
    const list = rows.filter((r) => r.signalSource === SOURCE_LIQ_BO);
    if (statusFilter === "all") return list;
    return list.filter((r) => r.status === statusFilter);
  }, [rows, statusFilter]);

  const brkVolRows = useMemo(() => {
    const list = rows.filter((r) => r.signalSource === SOURCE_BRK_VOL);
    if (statusFilter === "all") return list;
    return list.filter((r) => r.status === statusFilter);
  }, [rows, statusFilter]);

  const brkChainRows = useMemo(() => {
    const list = rows.filter((r) => r.signalSource === SOURCE_BRK_CHAIN);
    if (statusFilter === "all") return list;
    return list.filter((r) => r.status === statusFilter);
  }, [rows, statusFilter]);

  const heroZeroRows = useMemo(() => {
    const list = rows.filter((r) => r.signalSource === SOURCE_HERO_ZERO);
    if (statusFilter === "all") return list;
    return list.filter((r) => r.status === statusFilter);
  }, [rows, statusFilter]);

  const activeCount =
    orbRows.length +
    comboRows.length +
    liqBoRows.length +
    brkVolRows.length +
    brkChainRows.length +
    heroZeroRows.length;

  const columns = buildColumns(tab, onTrade);

  useEffect(() => {
    setTitle("Index Options");
    setBreadcrumbs([{ label: "Home" }, { label: "Index Options" }]);
    void refresh();

    const poll = () => {
      if (isMarketHoursIst() || document.visibilityState === "visible") void refresh(true);
    };
    const intervalId = window.setInterval(poll, POLL_MS);
    const onVisible = () => {
      if (document.visibilityState === "visible") void refresh(true);
    };
    document.addEventListener("visibilitychange", onVisible);

    return () => {
      setPageActions(null);
      window.clearInterval(intervalId);
      document.removeEventListener("visibilitychange", onVisible);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    setPageActions(
      <Stack direction="row" spacing={1} alignItems="center">
        {tab === "active" ? (
          <TextField
            select
            size="small"
            label="Status"
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value as typeof statusFilter)}
            sx={{ minWidth: 140 }}
          >
            <MenuItem value="all">All</MenuItem>
            <MenuItem value="recommended">Recommended</MenuItem>
            <MenuItem value="waiting">Waiting</MenuItem>
            <MenuItem value="skipped">Skipped</MenuItem>
          </TextField>
        ) : null}
        <Button
          size="small"
          variant="contained"
          startIcon={<Play size={DEFAULT_SMALL_ICON_SIZE} />}
          onClick={() => void onRun()}
          disabled={running}
        >
          {running ? "Running…" : "Run"}
        </Button>
      </Stack>,
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [statusFilter, running, tab]);

  return (
    <PageFrame>
      {error && <Alert severity="error">{error}</Alert>}
      {info ? (
        <Alert severity="success" onClose={() => setInfo(null)}>
          {info}
        </Alert>
      ) : null}

      <Tabs
        value={tab}
        onChange={(_, v: SignalsTab) => {
          setTab(v);
          setSelectedTradedIds([]);
        }}
        sx={{ minHeight: 40 }}
      >
        <Tab value="active" label={`Active (${activeCount})`} />
        <Tab value="history" label={`History (${historyRows.length})`} />
        <Tab value="traded" label={`Traded (${tradedRows.length})`} />
      </Tabs>

      <ScrollPane>
      <Stack spacing={3}>
      {tab === "active" ? (
        <>
          <ChainPanel chain={chain} />

          <SectionTable
            title="Nifty ORB"
            blurb="30-min ORB (9:15–9:45). CE when OR high breaks · PE when OR low breaks — up to two independent tickets. High-probability strikes (score ≥80, combos ≥85) trigger bell + browser alerts. Flat by 14:30 IST."
            rows={orbRows}
            columns={columns}
            loading={loading}
            expandedId={expandedOrbId}
            onExpand={setExpandedOrbId}
          />

          <SectionTable
            title="ORB + Liquidity V2"
            blurb="Same session: ORB trigger must align with Liquidity V2 (same side, entry within 0.5%). Entry from ORB; SL is the nearer of ORB and Liq V2; T1–T3 at 2R/3R/4R. Same strike + premium ticket as ORB."
            rows={comboRows}
            columns={columns}
            loading={loading}
            expandedId={expandedComboId}
            onExpand={setExpandedComboId}
          />

          <SectionTable
            title="Liquidity + Breakout"
            blurb="When Liq V2 and Breakout agree on side → Δ × Nifty option ticket (1 ITM + ATM). Single-engine setups use strike premium chart with match ≥55. Flat by 14:30 IST."
            rows={liqBoRows}
            columns={columns}
            loading={loading}
            expandedId={expandedLiqBoId}
            onExpand={setExpandedLiqBoId}
          />

          <SectionTable
            title="Breakout + Volume"
            blurb="Nifty 2-day high/low breakout with volume confirmation only (no Liquidity V2). Option ticket via Δ × Nifty entry / SL / T1 — same 1 ITM + ATM alt as ORB. High-probability (≥80) triggers bell alerts."
            rows={brkVolRows}
            columns={columns}
            loading={loading}
            expandedId={expandedBrkVolId}
            onExpand={setExpandedBrkVolId}
          />

          <SectionTable
            title="Breakout + Chain"
            blurb="Same pattern breakout + volume as above, then option-chain OI must agree (put wall / call wall / PCR). Only then emits strike · premium entry · SL · T1–T3. Confidence 82; ≥80 alerts. Flat by 14:30 IST."
            rows={brkChainRows}
            columns={columns}
            loading={loading}
            expandedId={expandedBrkChainId}
            onExpand={setExpandedBrkChainId}
          />

          <SectionTable
            title="Hero Zero"
            blurb="Far OTM lottery when ORB break and/or Breakout+Volume gives a clear direction. Buy cheap CE/PE (₹8–₹45, Δ 0.04–0.22) — risk full premium, targets 2× / 3× / 5× premium. Speculative only; no bell alerts. Flat by 14:30 IST."
            rows={heroZeroRows}
            columns={columns}
            loading={loading}
            expandedId={expandedHeroZeroId}
            onExpand={setExpandedHeroZeroId}
          />
        </>
      ) : (
        <>
          {tab === "traded" ? (
            <TradedDeleteBar
              selectedCount={selectedTradedIds.length}
              onDelete={onDeleteSelectedTraded}
            />
          ) : null}
          <SectionTable
            title={tab === "history" ? "History (left today)" : "Traded today"}
            blurb={
              tab === "history"
                ? "Frozen premium / Nifty levels from when each ticket first appeared today."
                : "Tickets you marked Trade — also listed under Positions. Select rows to delete."
            }
            rows={tab === "history" ? historyRows : tradedRows}
            columns={columns}
            loading={loading}
            expandedId={expandedHistoryId}
            onExpand={setExpandedHistoryId}
            enableSelection={tab === "traded"}
            selectedRowIds={tab === "traded" ? selectedTradedIds : undefined}
            onSelectedRowIdsChange={tab === "traded" ? setSelectedTradedIds : undefined}
            getRowId={tradedRowId}
          />
        </>
      )}
      </Stack>
      </ScrollPane>
    </PageFrame>
  );
}
