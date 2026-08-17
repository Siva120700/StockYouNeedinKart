import { useEffect, useMemo, useState } from "react";
import {
  Alert,
  Button,
  Checkbox,
  FormControl,
  FormControlLabel,
  InputLabel,
  ListItemText,
  MenuItem,
  OutlinedInput,
  Select,
  type SelectChangeEvent,
  Switch,
  Stack as MuiStack,
  Tab,
  Tabs,
  Tooltip,
} from "@mui/material";
import { Play, Handshake, FilePdf, FileXls } from "@phosphor-icons/react";
import { ActionFactory, DataFactory } from "../api/factories";
import type { LiquiditySignal } from "../api/types";
import { columnFactories } from "../zen_components/table/columnFactories";
import type { ColumnConfig } from "../zen_components/table/columnTypes";
import ZenTable from "../zen_components/table/ZenTable";
import { useZenPrimaryLayoutContext } from "../zen_components/layout/ZenPrimaryLayoutProvider";
import { DEFAULT_SMALL_ICON_SIZE } from "../constants";
import {
  downloadExcelTable,
  downloadPdfTable,
  exportStamp,
  type ExportColumn,
} from "../utils/exportTable";
import {
  createHistoricalHitRateColumn,
  formatHitRatePct,
  loadHistoricalHitRates,
  type HitRateByInstrument,
} from "../utils/historicalHitRate";
import { createSectorRsColumn } from "../utils/sectorRelativeStrength.tsx";
import {
  isSignalDayTraded,
  markSignalDayTraded,
  syncSignalDayHistory,
  unmarkSignalDayTraded,
  type SignalDayEntry,
} from "../utils/signalDayHistory";
import TradedDeleteBar from "../zen_components/shared/TradedDeleteBar";

type ScoredLiquiditySignal = LiquiditySignal & { score: number };
type LiquidityTab = "active" | "history" | "traded";

function formatTime(iso: string | null | undefined) {
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

const ALL_V2_EVENTS = [
  "external_sweep",
  "internal_liquidity",
  "liquidity_cluster",
  "delayed_reclaim",
  "multi_sweep",
] as const;

type V2EventType = (typeof ALL_V2_EVENTS)[number];

function eventTypeLabel(eventType: string | null | undefined): string {
  switch (eventType) {
    case "external_sweep":
      return "External Sweep";
    case "internal_liquidity":
      return "Internal Liquidity";
    case "liquidity_cluster":
      return "Liquidity Cluster";
    case "delayed_reclaim":
      return "Delayed Reclaim";
    case "multi_sweep":
      return "Multi Sweep";
    default:
      return eventType ?? "";
  }
}

function formatTarget(row: LiquiditySignal, target: number | null | undefined) {
  if (target == null || !Number.isFinite(Number(target)) || !row.entryPrice) return "";
  const t = Number(target);
  const entry = Number(row.entryPrice);
  if (entry === 0) return t.toFixed(2);
  const pct =
    row.side === "sell" ? ((entry - t) / entry) * 100 : ((t - entry) / entry) * 100;
  return `${t.toLocaleString("en-IN", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })} (${pct >= 0 ? "+" : ""}${pct.toFixed(2)}%)`;
}

function formatSl(row: LiquiditySignal) {
  const sl = row.initialStopLoss;
  if (sl == null || !Number.isFinite(Number(sl)) || !row.entryPrice) return "";
  const s = Number(sl);
  const entry = Number(row.entryPrice);
  if (entry === 0) return s.toFixed(2);
  const pct = ((s - entry) / entry) * 100;
  return `${s.toLocaleString("en-IN", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })} (${pct >= 0 ? "+" : ""}${pct.toFixed(2)}%)`;
}

const liquidityExportBase: ExportColumn<ScoredLiquiditySignal>[] = [
  { header: "Score", value: (r) => r.score },
  { header: "Symbol", value: (r) => r.appSymbol },
  { header: "Side", value: (r) => (r.side === "sell" ? "SELL" : "BUY") },
  { header: "Entry", value: (r) => r.entryPrice },
  { header: "SL", value: (r) => formatSl(r) },
  { header: "T1", value: (r) => formatTarget(r, r.targetT1) },
  { header: "T2", value: (r) => formatTarget(r, r.targetT2) },
  { header: "T3", value: (r) => formatTarget(r, r.targetT3) },
  {
    header: "RVOL",
    value: (r) =>
      `${Number(r.relativeVolume).toFixed(2)} (${Math.round(Number(r.rvolPercentile) * 100)}%)`,
  },
  {
    header: "Sweep",
    value: (r) =>
      r.sweptZoneType
        ? `${r.sweptZoneType}${r.sweptZonePrice != null ? ` @ ${Number(r.sweptZonePrice).toFixed(1)}` : ""}`
        : "",
  },
  {
    header: "Near zone",
    value: (r) => {
      if (!r.nearestZoneType) return "";
      const dist =
        r.distancePct != null ? ` ${(Number(r.distancePct) * 100).toFixed(2)}%` : "";
      return `${r.nearestZoneType}${dist}`;
    },
  },
  { header: "Strong", value: (r) => (r.strongClose ? "Yes" : "No") },
  { header: "Sector", value: (r) => (r.sectorConfirmed ? "Yes" : "No") },
];

export type LiquidityRuleset = "classic" | "fresh" | "v2";

export default function LiquiditySignalsPage({
  ruleset = "classic",
}: {
  ruleset?: LiquidityRuleset;
}) {
  const { setTitle, setBreadcrumbs, setPageActions, setIsSyncing } =
    useZenPrimaryLayoutContext();
  const [rows, setRows] = useState<LiquiditySignal[]>([]);
  const [historyRows, setHistoryRows] = useState<SignalDayEntry<LiquiditySignal>[]>([]);
  const [tradedRows, setTradedRows] = useState<SignalDayEntry<LiquiditySignal>[]>([]);
  const [tab, setTab] = useState<LiquidityTab>("active");
  const [selectedTradedIds, setSelectedTradedIds] = useState<string[]>([]);
  const [hitRates, setHitRates] = useState<HitRateByInstrument>(() => new Map());
  const [loading, setLoading] = useState(true);
  const [running, setRunning] = useState(false);
  const [sectorCheck, setSectorCheck] = useState(false);
  const [hideLaggingRs, setHideLaggingRs] = useState(false);
  const [riskRewardCheck, setRiskRewardCheck] = useState(false);
  const [requireRetest, setRequireRetest] = useState(false);
  const [requireRelativeStrength, setRequireRelativeStrength] = useState(false);
  const [eventFilter, setEventFilter] = useState<V2EventType[]>([...ALL_V2_EVENTS]);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);

  const isV2 = ruleset === "v2";
  const pageTitle =
    ruleset === "fresh" ? "Liquidity Fresh" : ruleset === "v2" ? "Liquidity V2" : "Liquidity";
  const exportBase =
    ruleset === "fresh" ? "liquidity-fresh" : ruleset === "v2" ? "liquidity-v2" : "liquidity";
  const strategyKey =
    ruleset === "fresh" ? "liquidity_fresh" : ruleset === "v2" ? "liquidity_v2" : "liquidity";
  const historyScope = `liquidity.${ruleset}`;

  const liquidityExportColumns: ExportColumn<ScoredLiquiditySignal>[] = useMemo(
    () => [
      liquidityExportBase[0]!,
      liquidityExportBase[1]!,
      {
        header: "Hit %",
        value: (r) => formatHitRatePct(hitRates.get(r.instrumentId)),
      },
      ...(isV2
        ? ([
            { header: "Event", value: (r: ScoredLiquiditySignal) => eventTypeLabel(r.eventType) },
            { header: "Confidence", value: (r: ScoredLiquiditySignal) => r.confidenceRating ?? "" },
            { header: "Sweep str", value: (r: ScoredLiquiditySignal) => r.sweepStrength ?? "" },
            {
              header: "ATR14",
              value: (r: ScoredLiquiditySignal) =>
                r.atr14 != null && Number.isFinite(Number(r.atr14))
                  ? Number(r.atr14).toFixed(2)
                  : "",
            },
            {
              header: "Score reasons",
              value: (r: ScoredLiquiditySignal) => (r.scoreReasons ?? []).join("; "),
            },
          ] as ExportColumn<ScoredLiquiditySignal>[])
        : []),
      ...liquidityExportBase.slice(2),
    ],
    [hitRates, isV2],
  );

  async function refresh() {
    setError(null);
    setIsSyncing(true);
    try {
      const [signals, rates] = await Promise.all([
        DataFactory.liquiditySignals(undefined, ruleset),
        loadHistoricalHitRates(strategyKey),
      ]);
      setRows(signals);
      setHitRates(rates);
      const synced = syncSignalDayHistory(historyScope, signals);
      setHistoryRows(synced.history);
      setTradedRows(synced.traded);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setLoading(false);
      setIsSyncing(false);
    }
  }

  async function onRun() {
    setRunning(true);
    setError(null);
    try {
      await ActionFactory.runLiquidityAnalysis(
        ruleset,
        isV2
          ? { requireRetest, requireRelativeStrength }
          : undefined,
      );
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setRunning(false);
    }
  }

  async function onTrade(row: LiquiditySignal) {
    setError(null);
    setInfo(null);
    try {
      await ActionFactory.openPositionFromLiquiditySignal(row.id);
      markSignalDayTraded(historyScope, row);
      setInfo(`${row.appSymbol} moved to Positions (Traded).`);
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  }

  function tradedRowId(r: LiquiditySignal) {
    return `${r.instrumentId}:${r.side}:${r.id}`;
  }

  function onDeleteSelectedTraded() {
    const selected = tradedRows.filter((r) => selectedTradedIds.includes(tradedRowId(r)));
    if (selected.length === 0) return;
    unmarkSignalDayTraded(historyScope, selected);
    setSelectedTradedIds([]);
    const synced = syncSignalDayHistory(historyScope, rows);
    setHistoryRows(synced.history);
    setTradedRows(synced.traded);
    setInfo(`Removed ${selected.length} from Traded.`);
  }

  /** Risk-reward vs T1: reward/risk. Buy (T1-entry)/(entry-SL); sell (entry-T1)/(SL-entry). */
  function riskRewardRatio(row: LiquiditySignal): number | null {
    const entry = Number(row.entryPrice);
    const sl = Number(row.initialStopLoss);
    const target = Number(row.targetT1 ?? row.targetT2 ?? row.targetT3);
    if (![entry, sl, target].every((n) => Number.isFinite(n)) || entry === 0) return null;
    const risk = row.side === "sell" ? sl - entry : entry - sl;
    const reward = row.side === "sell" ? entry - target : target - entry;
    if (risk <= 0 || reward <= 0) return null;
    return reward / risk;
  }

  /**
   * Quality score 0–100: higher = stronger liquidity setup.
   * V2 uses server-side qualityScore; classic/fresh keep client heuristic.
   */
  function liquidityScore(row: LiquiditySignal): number {
    if (isV2 && row.qualityScore != null && Number.isFinite(Number(row.qualityScore))) {
      return Math.round(Number(row.qualityScore));
    }

    let score = 0;

    const pctile = Number(row.rvolPercentile);
    if (Number.isFinite(pctile)) {
      score += Math.min(1, Math.max(0, pctile)) * 25; // top of own history
    }

    const rvol = Number(row.relativeVolume);
    if (Number.isFinite(rvol) && rvol > 0) {
      score += Math.min(rvol / 3, 1) * 15; // caps at ~3×
    }

    const zone = (row.sweptZoneType ?? "").toLowerCase();
    if (zone.startsWith("equal")) score += 20;
    else if (zone.startsWith("swing")) score += 15;
    else if (zone === "pdh" || zone === "pdl") score += 12;
    else if (zone === "pwh" || zone === "pwl") score += 10;
    else if (zone === "round") score += 6;

    if (row.strongClose) score += 15;

    const rr = riskRewardRatio(row);
    if (rr != null) {
      score += Math.min(rr / 2, 1) * 20; // full points at R:R ≥ 2
    }

    const dist = Number(row.distancePct);
    if (Number.isFinite(dist)) {
      if (dist <= 0.005) score += 5;
      else if (dist <= 0.01) score += 3;
      else if (dist <= 0.02) score += 1;
    }

    return Math.round(Math.min(100, Math.max(0, score)));
  }

  const visibleRows = useMemo(() => {
    let list: ScoredLiquiditySignal[] = rows.map((r) => ({
      ...r,
      score: liquidityScore(r),
    }));
    if (sectorCheck) list = list.filter((r) => r.sectorConfirmed);
    if (hideLaggingRs) list = list.filter((r) => !r.sectorRs?.downranked);
    if (riskRewardCheck) {
      list = list.filter((r) => {
        const rr = riskRewardRatio(r);
        return rr != null && rr >= 1;
      });
    }
    if (isV2 && eventFilter.length > 0 && eventFilter.length < ALL_V2_EVENTS.length) {
      const allowed = new Set<string>(eventFilter);
      list = list.filter((r) => r.eventType != null && allowed.has(r.eventType));
    } else if (isV2 && eventFilter.length === 0) {
      list = [];
    }
    return list.sort(
      (a, b) =>
        Number(a.sectorRs?.downranked) - Number(b.sectorRs?.downranked) ||
        b.score - a.score,
    );
  }, [rows, sectorCheck, hideLaggingRs, riskRewardCheck, isV2, eventFilter]);

  const scoredHistory = useMemo(
    () =>
      historyRows.map((r) => ({
        ...r,
        score: liquidityScore(r),
      })),
    [historyRows],
  );

  const scoredTraded = useMemo(
    () =>
      tradedRows.map((r) => ({
        ...r,
        score: liquidityScore(r),
      })),
    [tradedRows],
  );

  const tableRows: ScoredLiquiditySignal[] = useMemo(() => {
    if (tab === "history") return scoredHistory;
    if (tab === "traded") return scoredTraded;
    return visibleRows;
  }, [tab, visibleRows, scoredHistory, scoredTraded]);

  function onEventFilterChange(event: SelectChangeEvent<string[]>) {
    const raw = event.target.value;
    const value = typeof raw === "string" ? raw.split(",") : raw;
    if (value.includes("all")) {
      setEventFilter(
        eventFilter.length === ALL_V2_EVENTS.length ? [] : [...ALL_V2_EVENTS],
      );
      return;
    }
    setEventFilter(
      value.filter((v): v is V2EventType =>
        (ALL_V2_EVENTS as readonly string[]).includes(v),
      ),
    );
  }

  function onExportPdf() {
    downloadPdfTable({
      title: `${pageTitle} Signals`,
      fileName: exportStamp(exportBase, "pdf"),
      columns: liquidityExportColumns,
      rows: tableRows,
    });
  }

  function onExportExcel() {
    downloadExcelTable({
      sheetName: pageTitle,
      fileName: exportStamp(exportBase, "xlsx"),
      columns: liquidityExportColumns,
      rows: tableRows,
    });
  }

  useEffect(() => {
    setTitle(pageTitle);
    setBreadcrumbs([{ label: "Home" }, { label: pageTitle }]);
    setTab("active");
    setLoading(true);
    void refresh();
    return () => setPageActions(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ruleset]);

  useEffect(() => {
    const exportDisabled = loading || tableRows.length === 0;
    setPageActions(
      <MuiStack direction="row" spacing={1} alignItems="center" flexWrap="wrap">
        {tab === "active" ? (
          <>
            <FormControlLabel
              control={
                <Switch
                  size="small"
                  checked={sectorCheck}
                  onChange={(e) => setSectorCheck(e.target.checked)}
                />
              }
              label="Sector check"
              sx={{ mr: 1 }}
            />
            <FormControlLabel
              control={
                <Switch
                  size="small"
                  checked={hideLaggingRs}
                  onChange={(e) => setHideLaggingRs(e.target.checked)}
                />
              }
              label="Hide lagging RS"
              sx={{ mr: 1 }}
            />
            <FormControlLabel
              control={
                <Switch
                  size="small"
                  checked={riskRewardCheck}
                  onChange={(e) => setRiskRewardCheck(e.target.checked)}
                />
              }
              label="R:R ≥ 1"
              sx={{ mr: 1 }}
            />
            {isV2 ? (
              <>
                <FormControl size="small" sx={{ minWidth: 180, mr: 1 }}>
                  <InputLabel>Event</InputLabel>
                  <Select
                    multiple
                    label="Event"
                    value={eventFilter}
                    onChange={onEventFilterChange}
                    input={<OutlinedInput label="Event" />}
                    renderValue={(selected) =>
                      selected.length === ALL_V2_EVENTS.length
                        ? "All events"
                        : selected.length === 0
                          ? "No events"
                          : selected.length === 1
                            ? eventTypeLabel(selected[0])
                            : `${selected.length} events`
                    }
                  >
                    <MenuItem value="all">
                      <Checkbox
                        size="small"
                        checked={eventFilter.length === ALL_V2_EVENTS.length}
                        indeterminate={
                          eventFilter.length > 0 &&
                          eventFilter.length < ALL_V2_EVENTS.length
                        }
                      />
                      <ListItemText primary="All" />
                    </MenuItem>
                    {ALL_V2_EVENTS.map((e) => (
                      <MenuItem key={e} value={e}>
                        <Checkbox size="small" checked={eventFilter.includes(e)} />
                        <ListItemText primary={eventTypeLabel(e)} />
                      </MenuItem>
                    ))}
                  </Select>
                </FormControl>
                <FormControlLabel
                  control={
                    <Switch
                      size="small"
                      checked={requireRetest}
                      onChange={(e) => setRequireRetest(e.target.checked)}
                    />
                  }
                  label="Require retest"
                  sx={{ mr: 1 }}
                />
                <FormControlLabel
                  control={
                    <Switch
                      size="small"
                      checked={requireRelativeStrength}
                      onChange={(e) => setRequireRelativeStrength(e.target.checked)}
                    />
                  }
                  label="Nifty RS"
                  sx={{ mr: 1 }}
                />
              </>
            ) : null}
          </>
        ) : null}
        <Button
          variant="outlined"
          size="small"
          disabled={exportDisabled}
          startIcon={<FileXls size={DEFAULT_SMALL_ICON_SIZE} />}
          onClick={onExportExcel}
        >
          Excel
        </Button>
        <Button
          variant="outlined"
          size="small"
          disabled={exportDisabled}
          startIcon={<FilePdf size={DEFAULT_SMALL_ICON_SIZE} />}
          onClick={onExportPdf}
        >
          PDF
        </Button>
        <Button
          variant="contained"
          size="small"
          disabled={running}
          startIcon={<Play size={DEFAULT_SMALL_ICON_SIZE} />}
          onClick={() => void onRun()}
        >
          {running ? "Running…" : `Run ${pageTitle.toLowerCase()}`}
        </Button>
      </MuiStack>,
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [
    running,
    loading,
    sectorCheck,
    hideLaggingRs,
    riskRewardCheck,
    requireRetest,
    requireRelativeStrength,
    eventFilter,
    tableRows,
    pageTitle,
    isV2,
    tab,
  ]);

  const columns = useMemo(() => {
    type Scored = LiquiditySignal & { score: number };

    const cols: ColumnConfig<Scored>[] = [
      columnFactories.createNumberColumn<Scored>({
        field: "score",
        headerName: "Score",
        width: 80,
        minDecimalPlaces: 0,
        getValue: (r) => r.score,
        displayRenderer: (value, row) => {
          const reasons = (row.scoreReasons ?? []).join(" · ") || "No score reasons";
          return (
            <Tooltip title={reasons} arrow placement="top">
              <span>{value == null || value === "" ? "—" : String(value)}</span>
            </Tooltip>
          );
        },
      }),
      ...(isV2
        ? [
            columnFactories.createTextColumn<Scored>({
              field: "eventType",
              headerName: "Event",
              width: 130,
              getValue: (r) => eventTypeLabel(r.eventType),
            }),
            columnFactories.createTextColumn<Scored>({
              field: "confidenceRating",
              headerName: "Conf",
              width: 70,
              getValue: (r) => r.confidenceRating ?? "",
            }),
            columnFactories.createTextColumn<Scored>({
              field: "sweepStrength",
              headerName: "Sweep str",
              width: 90,
              getValue: (r) => r.sweepStrength ?? "",
            }),
          ]
        : []),
      columnFactories.createTextColumn<Scored>({
        field: "appSymbol",
        headerName: "Symbol",
        width: 110,
        getValue: (r) => r.appSymbol,
      }),
      createSectorRsColumn<Scored>((r) => r.sectorRs),
      createHistoricalHitRateColumn<Scored>(hitRates, (r) => r.instrumentId),
      columnFactories.createStatusColumn<Scored>(
        {
          buy: { label: "BUY", color: "#2e7d32" },
          sell: { label: "SELL", color: "#c62828" },
        },
        {
          field: "side",
          headerName: "Side",
          width: 90,
          getValue: (r) => r.side,
        },
      ),
      columnFactories.createNumberColumn<Scored>({
        field: "entryPrice",
        headerName: "Entry",
        width: 100,
        minDecimalPlaces: 2,
        getValue: (r) => r.entryPrice,
      }),
      columnFactories.createTextColumn<Scored>({
        field: "initialStopLoss",
        headerName: "SL",
        width: 130,
        getValue: (r) => formatSl(r),
      }),
      columnFactories.createTextColumn<Scored>({
        field: "targetT1",
        headerName: "T1",
        width: 130,
        getValue: (r) => formatTarget(r, r.targetT1),
      }),
      columnFactories.createTextColumn<Scored>({
        field: "targetT2",
        headerName: "T2",
        width: 130,
        getValue: (r) => formatTarget(r, r.targetT2),
      }),
      columnFactories.createTextColumn<Scored>({
        field: "targetT3",
        headerName: "T3",
        width: 130,
        getValue: (r) => formatTarget(r, r.targetT3),
      }),
      columnFactories.createTextColumn<Scored>({
        field: "relativeVolume",
        headerName: "RVOL",
        width: 90,
        getValue: (r) =>
          `${Number(r.relativeVolume).toFixed(2)} (${Math.round(Number(r.rvolPercentile) * 100)}%)`,
      }),
      columnFactories.createTextColumn<Scored>({
        field: "sweptZoneType",
        headerName: "Sweep",
        width: 120,
        getValue: (r) =>
          r.sweptZoneType
            ? `${r.sweptZoneType}${r.sweptZonePrice != null ? ` @ ${Number(r.sweptZonePrice).toFixed(1)}` : ""}`
            : "",
      }),
      columnFactories.createTextColumn<Scored>({
        field: "nearestZoneType",
        headerName: "Near zone",
        width: 120,
        getValue: (r) => {
          if (!r.nearestZoneType) return "";
          const dist =
            r.distancePct != null ? ` ${((Number(r.distancePct) * 100).toFixed(2))}%` : "";
          return `${r.nearestZoneType}${dist}`;
        },
      }),
      columnFactories.createBooleanColumn<Scored>({
        field: "strongClose",
        headerName: "Strong",
        width: 80,
        getValue: (r) => r.strongClose,
      }),
    ];

    if (tab === "history") {
      cols.push(
        columnFactories.createTextColumn<Scored>({
          field: "disappearedAt",
          headerName: "Left",
          width: 90,
          getValue: (r) =>
            formatTime(
              (r as unknown as SignalDayEntry<LiquiditySignal>).disappearedAt,
            ),
        }),
      );
    }

    if (tab === "traded") {
      cols.push(
        columnFactories.createTextColumn<Scored>({
          field: "tradedAt",
          headerName: "Traded",
          width: 90,
          getValue: (r) =>
            formatTime(
              (r as unknown as SignalDayEntry<LiquiditySignal>).tradedAt,
            ),
        }),
      );
    }

    if (tab !== "traded") {
      cols.push(
        columnFactories.createActionColumn<Scored>(
          (row) => [
            {
              icon: <Handshake size={DEFAULT_SMALL_ICON_SIZE} />,
              tooltip: isSignalDayTraded(historyScope, row)
                ? "Already traded"
                : "Trade — open in Positions",
              disabled: () => isSignalDayTraded(historyScope, row),
              onClick: (r) => void onTrade(r),
            },
          ],
          { field: "actions", headerName: "Trade", width: 80 },
        ),
      );
    }

    return cols;
  }, [hitRates, isV2, tab, historyScope]);

  const emptyMessage =
    tab === "history"
      ? "No signals have left the list today. History keeps the frozen entry/SL/targets from when each name first appeared."
      : tab === "traded"
        ? "No traded signals today. Use Trade on Active or History to open a position."
        : sectorCheck || riskRewardCheck
          ? `No ${pageTitle.toLowerCase()} signals match the active filters. Turn filters off, or Run again.`
          : ruleset === "fresh"
            ? "No liquidity fresh setups still near entry. Click Run."
            : ruleset === "v2"
              ? "No liquidity V2 setups still near entry (T1 open). Click Run."
              : "No liquidity setups still near entry. Click Run.";

  return (
    <>
      {error ? (
        <Alert severity="error" sx={{ mb: 2 }}>
          {error}
        </Alert>
      ) : null}
      {info ? (
        <Alert severity="success" sx={{ mb: 2 }} onClose={() => setInfo(null)}>
          {info}
        </Alert>
      ) : null}
      <Tabs
        value={tab}
        onChange={(_, v: LiquidityTab) => {
          setTab(v);
          setSelectedTradedIds([]);
        }}
        sx={{ mb: 1.5, minHeight: 40 }}
      >
        <Tab value="active" label={`Active (${visibleRows.length})`} />
        <Tab value="history" label={`History (${historyRows.length})`} />
        <Tab value="traded" label={`Traded (${tradedRows.length})`} />
      </Tabs>
      {tab === "traded" ? (
        <TradedDeleteBar
          selectedCount={selectedTradedIds.length}
          onDelete={onDeleteSelectedTraded}
        />
      ) : null}
      <ZenTable
        columns={columns}
        rows={tableRows}
        getRowId={(r) => (tab === "active" ? r.id : tradedRowId(r))}
        loading={loading}
        enableSearch
        searchPlaceholder="Search symbol or name…"
        emptyMessage={emptyMessage}
        enableSelection={tab === "traded"}
        selectedRowIds={tab === "traded" ? selectedTradedIds : undefined}
        onSelectedRowIdsChange={tab === "traded" ? setSelectedTradedIds : undefined}
      />
    </>
  );
}
