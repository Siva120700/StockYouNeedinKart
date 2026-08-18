import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  Alert,
  Autocomplete,
  Box,
  Button,
  Checkbox,
  FormControl,
  FormControlLabel,
  InputLabel,
  LinearProgress,
  ListItemText,
  MenuItem,
  OutlinedInput,
  Select,
  type SelectChangeEvent,
  Stack as MuiStack,
  Switch,
  TextField,
  Typography,
} from "@mui/material";
import { FloppyDisk, FilePdf, Play, Stop, Trash, ArrowsClockwise } from "@phosphor-icons/react";
import { ActionFactory, DataFactory } from "../api/factories";
import type {
  BacktestNoteInput,
  BacktestSymbolSummary,
  UniverseInstrument,
} from "../api/types";
import { columnFactories } from "../zen_components/table/columnFactories";
import type { ColumnConfig } from "../zen_components/table/columnTypes";
import ZenTable from "../zen_components/table/ZenTable";
import { useZenPrimaryLayoutContext } from "../zen_components/layout/ZenPrimaryLayoutProvider";
import { DEFAULT_SMALL_ICON_SIZE } from "../constants";
import {
  downloadPdfTable,
  exportStamp,
  type ExportColumn,
} from "../utils/exportTable";

const ALL_BACKTEST_STRATEGIES = [
  "signals",
  "liquidity",
  "liquidity_fresh",
  "liquidity_v2",
  "confluence",
  "trade_score",
  "breakout",
  "momentum_v2",
  "momentum_v3",
] as const;

type BacktestStrategy = (typeof ALL_BACKTEST_STRATEGIES)[number];

const STRATEGY_STORAGE_KEY = "backtest.strategyFilter";

function isBacktestStrategy(value: string): value is BacktestStrategy {
  return (ALL_BACKTEST_STRATEGIES as readonly string[]).includes(value);
}

function parseStoredStrategies(raw: string | null): BacktestStrategy[] {
  if (!raw) return [...ALL_BACKTEST_STRATEGIES];
  if (raw === "all") return [...ALL_BACKTEST_STRATEGIES];
  if (isBacktestStrategy(raw)) return [raw];
  try {
    const parsed = JSON.parse(raw) as unknown;
    if (Array.isArray(parsed)) {
      // An explicitly stored empty array means the user cleared the filter.
      return parsed.filter(
        (v): v is BacktestStrategy => typeof v === "string" && isBacktestStrategy(v),
      );
    }
  } catch {
    /* ignore */
  }
  return [...ALL_BACKTEST_STRATEGIES];
}

function strategyLabel(strategy: string | null | undefined): string {
  if (strategy === "confluence") return "Confluence";
  if (strategy === "trade_score") return "Trade Score";
  if (strategy === "breakout") return "Breakout";
  if (strategy === "liquidity_fresh") return "Liquidity Fresh";
  if (strategy === "liquidity_v2") return "Liquidity V2";
  if (strategy === "liquidity") return "Liquidity";
  if (strategy === "signals") return "Signals";
  if (strategy === "momentum_v2") return "Momentum V2";
  if (strategy === "momentum_v3") return "Momentum V3";
  return strategy ?? "";
}

function isAllStrategies(selected: readonly BacktestStrategy[]): boolean {
  return selected.length === ALL_BACKTEST_STRATEGIES.length;
}

function strategiesForFilter(selected: readonly BacktestStrategy[]): readonly BacktestStrategy[] {
  return isAllStrategies(selected) ? ALL_BACKTEST_STRATEGIES : selected;
}

function strategySelectionLabel(selected: readonly BacktestStrategy[]): string {
  if (selected.length === 0) return "No strategies";
  if (isAllStrategies(selected)) return "All strategies";
  if (selected.length === 1) return strategyLabel(selected[0]);
  return `${selected.length} strategies`;
}

function todayIso(): string {
  return new Date().toISOString().slice(0, 10);
}

function emptyForm(instrumentId: string): BacktestNoteInput {
  return {
    id: null,
    instrumentId,
    strategy: "signals",
    side: "buy",
    signalDate: todayIso(),
    entryPrice: 0,
    initialStopLoss: 0,
    targetT1: null,
    targetT2: null,
    targetT3: null,
    result: "open",
    targetLevel: null,
    targetHitPct: null,
    exitPrice: null,
    exitDate: null,
    pnlPct: null,
    rMultiple: null,
    notes: "",
    wouldTakeLive: null,
  };
}

function summaryRowId(row: BacktestSymbolSummary): string {
  return `${row.instrumentId}-${row.strategyFilter ?? "unknown"}`;
}

function isStockCompleted(
  instrumentId: string,
  strategies: readonly BacktestStrategy[],
  summaries: BacktestSymbolSummary[],
): boolean {
  return strategies.every((s) =>
    summaries.some(
      (r) => r.instrumentId === instrumentId && r.strategyFilter === s && r.timesInStrategy > 0,
    ),
  );
}

function mergeSummary(
  rows: BacktestSymbolSummary[],
  incoming: BacktestSymbolSummary,
): BacktestSymbolSummary[] {
  const id = summaryRowId(incoming);
  const rest = rows.filter((r) => summaryRowId(r) !== id);
  return [...rest, incoming].sort((a, b) => {
    const sym = a.appSymbol.localeCompare(b.appSymbol);
    if (sym !== 0) return sym;
    return (a.strategyFilter ?? "").localeCompare(b.strategyFilter ?? "");
  });
}

/** Portfolio aggregates from visible rows (setup-weighted where needed). */
function aggregateStats(rows: BacktestSymbolSummary[]) {
  const setups = rows.reduce((s, r) => s + (r.timesInStrategy || 0), 0);
  const targetHits = rows.reduce((s, r) => s + (r.targetHits || 0), 0);
  const slHits = rows.reduce((s, r) => s + (r.slHits || 0), 0);
  const decided = targetHits + slHits;
  const avgHitRatePct = decided > 0 ? (100 * targetHits) / decided : null;

  let weightedTargetPctSum = 0;
  let weightedTargetPctWeight = 0;
  let weightedRrSum = 0;
  let weightedRrWeight = 0;
  let weightedRSum = 0;
  let weightedRWeight = 0;

  for (const r of rows) {
    const w = Math.max(1, r.timesInStrategy || 0);
    if (r.avgTargetHitPct != null && Number.isFinite(Number(r.avgTargetHitPct))) {
      weightedTargetPctSum += Number(r.avgTargetHitPct) * w;
      weightedTargetPctWeight += w;
    }
    if (r.avgRiskReward != null && Number.isFinite(Number(r.avgRiskReward))) {
      weightedRrSum += Number(r.avgRiskReward) * w;
      weightedRrWeight += w;
    }
    if (r.avgRMultiple != null && Number.isFinite(Number(r.avgRMultiple))) {
      weightedRSum += Number(r.avgRMultiple) * w;
      weightedRWeight += w;
    }
  }

  return {
    stocks: rows.length,
    setups,
    targetHits,
    slHits,
    avgHitRatePct,
    avgTargetPct:
      weightedTargetPctWeight > 0 ? weightedTargetPctSum / weightedTargetPctWeight : null,
    avgRiskReward: weightedRrWeight > 0 ? weightedRrSum / weightedRrWeight : null,
    avgRMultiple: weightedRWeight > 0 ? weightedRSum / weightedRWeight : null,
  };
}

const summaryColumns: ColumnConfig<BacktestSymbolSummary>[] = [
  columnFactories.createTextColumn<BacktestSymbolSummary>({
    field: "appSymbol",
    headerName: "Symbol",
    width: 100,
    getValue: (r) => r.appSymbol,
  }),
  columnFactories.createTextColumn<BacktestSymbolSummary>({
    field: "instrumentName",
    headerName: "Stock name",
    width: 180,
    getValue: (r) => r.instrumentName,
  }),
  columnFactories.createTextColumn<BacktestSymbolSummary>({
    field: "strategyFilter",
    headerName: "Strategy",
    width: 110,
    getValue: (r) => strategyLabel(r.strategyFilter),
  }),
  columnFactories.createNumberColumn<BacktestSymbolSummary>({
    field: "timesInStrategy",
    headerName: "Setups",
    width: 90,
    getValue: (r) => r.timesInStrategy,
  }),
  columnFactories.createNumberColumn<BacktestSymbolSummary>({
    field: "targetHits",
    headerName: "Target hits",
    width: 110,
    getValue: (r) => r.targetHits,
  }),
  columnFactories.createNumberColumn<BacktestSymbolSummary>({
    field: "slHits",
    headerName: "SL hits",
    width: 90,
    getValue: (r) => r.slHits,
  }),
  columnFactories.createTextColumn<BacktestSymbolSummary>({
    field: "targetHitRatePct",
    headerName: "Hit rate",
    width: 100,
    getValue: (r) =>
      r.targetHitRatePct != null && Number.isFinite(Number(r.targetHitRatePct))
        ? Number(r.targetHitRatePct)
        : null,
    displayRenderer: (v) =>
      v != null && Number.isFinite(Number(v)) ? `${Number(v).toFixed(1)}%` : "—",
  }),
  columnFactories.createTextColumn<BacktestSymbolSummary>({
    field: "avgTargetHitPct",
    headerName: "Avg target %",
    width: 120,
    getValue: (r) =>
      r.avgTargetHitPct != null && Number.isFinite(Number(r.avgTargetHitPct))
        ? Number(r.avgTargetHitPct)
        : null,
    displayRenderer: (v) =>
      v != null && Number.isFinite(Number(v)) ? `${Number(v).toFixed(0)}%` : "—",
  }),
  columnFactories.createTextColumn<BacktestSymbolSummary>({
    field: "avgRiskReward",
    headerName: "Avg R:R",
    width: 90,
    getValue: (r) =>
      r.avgRiskReward != null && Number.isFinite(Number(r.avgRiskReward))
        ? Number(r.avgRiskReward)
        : null,
    displayRenderer: (v) =>
      v != null && Number.isFinite(Number(v)) ? Number(v).toFixed(2) : "—",
  }),
  columnFactories.createTextColumn<BacktestSymbolSummary>({
    field: "avgRMultiple",
    headerName: "Avg R",
    width: 80,
    getValue: (r) =>
      r.avgRMultiple != null && Number.isFinite(Number(r.avgRMultiple))
        ? Number(r.avgRMultiple)
        : null,
    displayRenderer: (v) =>
      v != null && Number.isFinite(Number(v)) ? Number(v).toFixed(2) : "—",
  }),
];

const backtestExportColumns: ExportColumn<BacktestSymbolSummary>[] = [
  { header: "Symbol", value: (r) => r.appSymbol },
  { header: "Stock name", value: (r) => r.instrumentName },
  { header: "Strategy", value: (r) => strategyLabel(r.strategyFilter) },
  { header: "Setups", value: (r) => r.timesInStrategy },
  { header: "Target hits", value: (r) => r.targetHits },
  { header: "SL hits", value: (r) => r.slHits },
  {
    header: "Hit rate %",
    value: (r) =>
      r.targetHitRatePct != null ? Number(r.targetHitRatePct).toFixed(1) : "",
  },
  {
    header: "Avg target %",
    value: (r) =>
      r.avgTargetHitPct != null ? Number(r.avgTargetHitPct).toFixed(0) : "",
  },
  {
    header: "Avg R:R",
    value: (r) =>
      r.avgRiskReward != null ? Number(r.avgRiskReward).toFixed(2) : "",
  },
  {
    header: "Avg R",
    value: (r) =>
      r.avgRMultiple != null ? Number(r.avgRMultiple).toFixed(2) : "",
  },
];

export default function BacktestPage() {
  const { setTitle, setBreadcrumbs, setPageActions, setIsSyncing } =
    useZenPrimaryLayoutContext();

  const [universe, setUniverse] = useState<UniverseInstrument[]>([]);
  const [runTarget, setRunTarget] = useState<UniverseInstrument | null>(null);
  const [strategyFilter, setStrategyFilter] = useState<BacktestStrategy[]>(() =>
    parseStoredStrategies(sessionStorage.getItem(STRATEGY_STORAGE_KEY)),
  );

  function persistStrategyFilter(next: BacktestStrategy[]) {
    setStrategyFilter(next);
    sessionStorage.setItem(STRATEGY_STORAGE_KEY, JSON.stringify(next));
  }

  function onStrategyFilterChange(event: SelectChangeEvent<string[]>) {
    const raw = event.target.value;
    const value = typeof raw === "string" ? raw.split(",") : raw;
    if (value.includes("all")) {
      // "All" toggles: clear when everything is already selected.
      persistStrategyFilter(
        isAllStrategies(strategyFilter) ? [] : [...ALL_BACKTEST_STRATEGIES],
      );
      return;
    }
    persistStrategyFilter(value.filter(isBacktestStrategy));
  }
  const [summaries, setSummaries] = useState<BacktestSymbolSummary[]>([]);
  const [form, setForm] = useState<BacktestNoteInput>(emptyForm(""));
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [running, setRunning] = useState(false);
  const [runningAll, setRunningAll] = useState(false);
  const [clearing, setClearing] = useState(false);
  const [skipCompleted, setSkipCompleted] = useState(true);
  const [riskRewardCheck, setRiskRewardCheck] = useState(() => {
    const saved = sessionStorage.getItem("backtest.riskRewardCheck");
    return saved !== "false";
  });
  const [sectorCheck, setSectorCheck] = useState(() => {
    const saved = sessionStorage.getItem("backtest.sectorCheck");
    return saved === "true";
  });
  const [batchProgress, setBatchProgress] = useState<{
    current: number;
    total: number;
    symbol: string;
    strategy: string;
    failed: number;
  } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);
  const [refreshingStocks, setRefreshingStocks] = useState(false);
  const [showManualForm, setShowManualForm] = useState(false);
  const [search, setSearch] = useState("");
  const [visibleRows, setVisibleRows] = useState<BacktestSymbolSummary[]>([]);
  const cancelBatchRef = useRef(false);

  const loadSummaries = useCallback(async () => {
    setIsSyncing(true);
    setError(null);
    try {
      const selected = strategiesForFilter(strategyFilter);
      const strat = selected.length === 1 ? selected[0]! : null;
      const minRr = riskRewardCheck ? 1 : null;
      const rows = await DataFactory.backtestSummaries(strat, minRr, sectorCheck || null);
      setSummaries(rows);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setIsSyncing(false);
      setLoading(false);
    }
  }, [strategyFilter, riskRewardCheck, sectorCheck, setIsSyncing]);

  const reloadUniverses = useCallback(async () => {
    setRefreshingStocks(true);
    setError(null);
    try {
      const list = await DataFactory.universes();
      const sorted = [...list].sort((a, b) => a.symbol.localeCompare(b.symbol));
      setUniverse(sorted);
      if (sorted[0] && !runTarget) {
        setRunTarget(sorted[0]);
        setForm(emptyForm(sorted[0].id));
      }
      setInfo(`Stock list: ${sorted.length} symbols (Nifty + F&O). Syncing Angel tokens…`);

      try {
        await ActionFactory.syncUniverseTokens();
        const refreshed = await DataFactory.universes();
        const resorted = [...refreshed].sort((a, b) => a.symbol.localeCompare(b.symbol));
        setUniverse(resorted);
        setInfo(`Stock list refreshed — ${resorted.length} symbols (Nifty + F&O).`);
      } catch (syncErr) {
        setInfo(
          `${sorted.length} symbols loaded. Token sync skipped: ${
            syncErr instanceof Error ? syncErr.message : String(syncErr)
          }`,
        );
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setRefreshingStocks(false);
    }
  }, [runTarget]);

  useEffect(() => {
    setTitle("Backtest");
    setBreadcrumbs([{ label: "Home" }, { label: "Backtest" }]);
    setPageActions(null);
    void (async () => {
      try {
        const list = await DataFactory.universes();
        const sorted = [...list].sort((a, b) => a.symbol.localeCompare(b.symbol));
        setUniverse(sorted);
        if (sorted[0]) {
          setRunTarget(sorted[0]);
          setForm(emptyForm(sorted[0].id));
        }
        await loadSummaries();
      } catch (e) {
        setError(e instanceof Error ? e.message : String(e));
        setLoading(false);
      }
    })();
    return () => setPageActions(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    void loadSummaries();
  }, [loadSummaries]);

  async function onRunHistorical() {
    if (!runTarget || strategyFilter.length === 0) return;
    setRunning(true);
    setError(null);
    setIsSyncing(true);
    try {
      const strategies = strategiesForFilter(strategyFilter);
      for (const s of strategies) {
        const summary = await ActionFactory.runHistoricalBacktest(runTarget.id, s);
        setSummaries((prev) => mergeSummary(prev, summary));
      }
      await loadSummaries();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setRunning(false);
      setIsSyncing(false);
    }
  }

  function stopBatch() {
    cancelBatchRef.current = true;
  }

  async function onClearAutoBacktests() {
    if (clearing || running || runningAll || strategyFilter.length === 0) return;
    const strategies = [...strategiesForFilter(strategyFilter)];
    const label = strategySelectionLabel(strategyFilter);
    const ok = window.confirm(
      `Delete auto-generated backtest results for ${label}?\n\nManual notes are kept. Re-run 1Y afterward to rebuild from the current rules.`,
    );
    if (!ok) return;

    setClearing(true);
    setError(null);
    setInfo(null);
    setIsSyncing(true);
    try {
      const deleted = await ActionFactory.deleteBacktests(strategies, true);
      await loadSummaries();
      if (deleted === 0) {
        setInfo("No auto backtest rows to delete for the selected strategies.");
      } else {
        setInfo(`Cleared ${deleted} auto backtest row${deleted === 1 ? "" : "s"} for ${label}.`);
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setClearing(false);
      setIsSyncing(false);
    }
  }

  async function onRunAllHistorical() {
    if (universe.length === 0 || runningAll || strategyFilter.length === 0) return;
    const strategies = strategiesForFilter(strategyFilter);
    const queue = universe.filter(
      (stock) => !skipCompleted || !isStockCompleted(stock.id, strategies, summaries),
    );

    if (queue.length === 0) {
      setError("All stocks already have backtest data for the selected strategy.");
      return;
    }

    cancelBatchRef.current = false;
    setRunningAll(true);
    setError(null);
    setIsSyncing(true);
    let failed = 0;

    try {
      for (let i = 0; i < queue.length; i++) {
        if (cancelBatchRef.current) break;

        const stock = queue[i]!;
        setRunTarget(stock);
        setBatchProgress({
          current: i + 1,
          total: queue.length,
          symbol: stock.symbol,
          strategy: strategies[0] ?? "",
          failed,
        });

        for (const s of strategies) {
          if (cancelBatchRef.current) break;
          setBatchProgress((p) => (p ? { ...p, symbol: stock.symbol, strategy: s } : p));
          try {
            const summary = await ActionFactory.runHistoricalBacktest(stock.id, s);
            setSummaries((prev) => mergeSummary(prev, summary));
          } catch (e) {
            failed++;
            setError(
              `${stock.symbol} (${s}): ${e instanceof Error ? e.message : String(e)}` +
                (i < queue.length - 1 ? " — continuing with next stock…" : ""),
            );
          }
          if (!cancelBatchRef.current && s !== strategies[strategies.length - 1]) {
            await new Promise((r) => setTimeout(r, 1500));
          }
        }

        if (!cancelBatchRef.current && i < queue.length - 1) {
          await new Promise((r) => setTimeout(r, 2000));
        }
      }

      await loadSummaries();
    } finally {
      setRunningAll(false);
      setBatchProgress(null);
      setIsSyncing(false);
    }
  }

  async function onSave() {
    if (!runTarget) return;
    setSaving(true);
    setError(null);
    try {
      const payload: BacktestNoteInput = {
        ...form,
        instrumentId: runTarget.id,
        entryPrice: Number(form.entryPrice) || 0,
        initialStopLoss: Number(form.initialStopLoss) || 0,
        targetHitPct:
          form.result === "target" && form.targetHitPct == null ? 100 : form.targetHitPct,
        targetLevel: form.result === "target" ? form.targetLevel || "t1" : form.targetLevel,
      };
      await ActionFactory.upsertBacktestNote(payload);
      setForm(emptyForm(runTarget.id));
      await loadSummaries();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setSaving(false);
    }
  }

  const tableRows = useMemo(() => {
    if (isAllStrategies(strategyFilter)) return summaries;
    const selected = new Set(strategyFilter);
    return summaries.filter(
      (r) => r.strategyFilter != null && selected.has(r.strategyFilter as BacktestStrategy),
    );
  }, [summaries, strategyFilter]);

  const onVisibleRowsChange = useCallback((rows: BacktestSymbolSummary[]) => {
    setVisibleRows(rows);
  }, []);

  const stats = useMemo(() => aggregateStats(visibleRows), [visibleRows]);

  const uniqueResultSymbols = useMemo(() => {
    const ids = new Set(tableRows.map((r) => r.instrumentId));
    return ids.size;
  }, [tableRows]);

  function onExportPdf() {
    const filterLabel = strategySelectionLabel(strategyFilter);
    downloadPdfTable({
      title: `Backtest 1Y · ${filterLabel}${riskRewardCheck ? " · R:R ≥ 1" : ""}${sectorCheck ? " · Sector" : ""}`,
      fileName: exportStamp("backtest", "pdf"),
      columns: backtestExportColumns,
      rows: visibleRows,
      summary: [
        { label: "Universe", value: String(universe.length) },
        { label: "With results", value: String(uniqueResultSymbols) },
        { label: "Result rows", value: String(stats.stocks) },
        { label: "Setups", value: String(stats.setups) },
        { label: "Target hits", value: String(stats.targetHits) },
        { label: "SL hits", value: String(stats.slHits) },
        {
          label: "Avg hit rate",
          value:
            stats.avgHitRatePct != null ? `${stats.avgHitRatePct.toFixed(1)}%` : "—",
        },
        {
          label: "Avg target %",
          value:
            stats.avgTargetPct != null ? `${stats.avgTargetPct.toFixed(0)}%` : "—",
        },
        {
          label: "Avg R:R",
          value:
            stats.avgRiskReward != null ? stats.avgRiskReward.toFixed(2) : "—",
        },
        {
          label: "Avg R",
          value:
            stats.avgRMultiple != null ? stats.avgRMultiple.toFixed(2) : "—",
        },
      ],
    });
  }

  return (
    <MuiStack
      spacing={2}
      sx={{ height: "100%", overflow: "hidden", minHeight: 0 }}
    >
      <Box sx={{ flexShrink: 0 }}>
        {error ? <Alert severity="error">{error}</Alert> : null}
        {info ? <Alert severity="success" onClose={() => setInfo(null)}>{info}</Alert> : null}

        {batchProgress ? (
          <Box sx={{ mt: error ? 2 : 0 }}>
            <MuiStack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 0.5 }}>
              <Typography variant="body2" color="text.secondary">
                {batchProgress.symbol} · {batchProgress.current}/{batchProgress.total} ·{" "}
                {strategyLabel(batchProgress.strategy)}
                {batchProgress.failed > 0 ? ` · ${batchProgress.failed} failed` : ""}
              </Typography>
              <Button
                size="small"
                color="inherit"
                startIcon={<Stop size={DEFAULT_SMALL_ICON_SIZE} />}
                onClick={stopBatch}
              >
                Stop
              </Button>
            </MuiStack>
            <LinearProgress
              variant="determinate"
              value={(batchProgress.current / batchProgress.total) * 100}
            />
          </Box>
        ) : null}

        <MuiStack
          direction="row"
          spacing={1}
          alignItems="center"
          flexWrap="wrap"
          sx={{ mt: batchProgress ? 2 : 2 }}
        >
          <FormControl size="small" sx={{ minWidth: 180 }} disabled={runningAll}>
            <InputLabel>Strategy</InputLabel>
            <Select
              multiple
              label="Strategy"
              value={strategyFilter}
              onChange={onStrategyFilterChange}
              input={<OutlinedInput label="Strategy" />}
              renderValue={(selected) => strategySelectionLabel(selected as BacktestStrategy[])}
            >
              <MenuItem value="all">
                <Checkbox
                  size="small"
                  checked={isAllStrategies(strategyFilter)}
                  indeterminate={
                    strategyFilter.length > 0 &&
                    strategyFilter.length < ALL_BACKTEST_STRATEGIES.length
                  }
                />
                <ListItemText primary="All" />
              </MenuItem>
              {ALL_BACKTEST_STRATEGIES.map((s) => (
                <MenuItem key={s} value={s}>
                  <Checkbox size="small" checked={strategyFilter.includes(s)} />
                  <ListItemText primary={strategyLabel(s)} />
                </MenuItem>
              ))}
            </Select>
          </FormControl>
          <Autocomplete
            sx={{ minWidth: 260 }}
            size="small"
            options={universe}
            getOptionLabel={(o) => `${o.symbol} — ${o.name}`}
            value={runTarget}
            onChange={(_, v) => {
              setRunTarget(v);
              if (v) setForm((prev) => ({ ...emptyForm(v.id), strategy: prev.strategy || "signals" }));
            }}
            renderInput={(params) => <TextField {...params} label="Run 1Y for" />}
            isOptionEqualToValue={(a, b) => a.id === b.id}
            disabled={runningAll}
          />
          <Button
            size="small"
            variant="outlined"
            disabled={runningAll || refreshingStocks}
            startIcon={<ArrowsClockwise size={DEFAULT_SMALL_ICON_SIZE} />}
            onClick={() => void reloadUniverses()}
          >
            {refreshingStocks ? "Syncing…" : `Refresh stocks (${universe.length})`}
          </Button>
          <Button
            size="small"
            variant="contained"
            disabled={
              !runTarget || running || runningAll || loading || strategyFilter.length === 0
            }
            startIcon={<Play size={DEFAULT_SMALL_ICON_SIZE} />}
            onClick={() => void onRunHistorical()}
          >
            {running
              ? isAllStrategies(strategyFilter) || strategyFilter.length > 1
                ? "Running 1Y… (may take a few min)"
                : "Running 1Y…"
              : strategyFilter.length === 0
                ? "Run 1Y (select a strategy)"
                : isAllStrategies(strategyFilter)
                  ? "Run 1Y (all strategies)"
                  : strategyFilter.length === 1
                    ? `Run 1Y ${strategyLabel(strategyFilter[0]).toLowerCase()}`
                    : `Run 1Y (${strategyFilter.length} strategies)`}
          </Button>
          <Button
            size="small"
            variant="outlined"
            disabled={
              universe.length === 0 ||
              running ||
              runningAll ||
              loading ||
              strategyFilter.length === 0
            }
            startIcon={<Play size={DEFAULT_SMALL_ICON_SIZE} />}
            onClick={() => void onRunAllHistorical()}
          >
            {runningAll
              ? "Running all…"
              : strategyFilter.length === 0
                ? "Run all (select a strategy)"
                : isAllStrategies(strategyFilter)
                  ? "Run all 1Y"
                  : strategyFilter.length === 1
                    ? `Run all ${strategyLabel(strategyFilter[0]).toLowerCase()} 1Y`
                    : `Run all (${strategyFilter.length} strategies) 1Y`}
          </Button>
          <FormControlLabel
            control={
              <Switch
                size="small"
                checked={riskRewardCheck}
                disabled={runningAll}
                onChange={(e) => {
                  const next = e.target.checked;
                  setRiskRewardCheck(next);
                  sessionStorage.setItem("backtest.riskRewardCheck", String(next));
                }}
              />
            }
            label="R:R ≥ 1"
          />
          <FormControlLabel
            control={
              <Switch
                size="small"
                checked={sectorCheck}
                disabled={runningAll}
                onChange={(e) => {
                  const next = e.target.checked;
                  setSectorCheck(next);
                  sessionStorage.setItem("backtest.sectorCheck", String(next));
                }}
              />
            }
            label="Sector check"
          />
          <FormControlLabel
            control={
              <Switch
                size="small"
                checked={skipCompleted}
                disabled={runningAll}
                onChange={(e) => setSkipCompleted(e.target.checked)}
              />
            }
            label="Skip completed"
          />
          <FormControlLabel
            control={
              <Switch
                size="small"
                checked={showManualForm}
                onChange={(e) => setShowManualForm(e.target.checked)}
              />
            }
            label="Manual note"
          />
          <Button
            size="small"
            variant="outlined"
            color="warning"
            disabled={
              loading ||
              clearing ||
              running ||
              runningAll ||
              strategyFilter.length === 0
            }
            startIcon={<Trash size={DEFAULT_SMALL_ICON_SIZE} />}
            onClick={() => void onClearAutoBacktests()}
          >
            {clearing ? "Clearing…" : "Clear auto results"}
          </Button>
          <Button
            size="small"
            variant="outlined"
            disabled={loading || visibleRows.length === 0}
            startIcon={<FilePdf size={DEFAULT_SMALL_ICON_SIZE} />}
            onClick={onExportPdf}
          >
            PDF
          </Button>
        </MuiStack>

        <Box
          sx={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(110px, 1fr))",
            gap: 1.5,
            mt: 2,
            p: 1.5,
            bgcolor: "background.paper",
            border: "1px solid",
            borderColor: "divider",
            borderRadius: 1,
          }}
        >
          <Stat label="Universe" value={String(universe.length)} />
          <Stat label="With results" value={String(uniqueResultSymbols)} />
          <Stat label="Result rows" value={String(stats.stocks)} />
          <Stat label="Setups" value={String(stats.setups)} />
          <Stat label="Target hits" value={String(stats.targetHits)} />
          <Stat label="SL hits" value={String(stats.slHits)} />
          <Stat
            label="Avg hit rate"
            value={
              stats.avgHitRatePct != null ? `${stats.avgHitRatePct.toFixed(1)}%` : "—"
            }
          />
          <Stat
            label="Avg target %"
            value={
              stats.avgTargetPct != null ? `${stats.avgTargetPct.toFixed(0)}%` : "—"
            }
          />
          <Stat
            label="Avg R:R"
            value={
              stats.avgRiskReward != null ? stats.avgRiskReward.toFixed(2) : "—"
            }
          />
          <Stat
            label="Avg R"
            value={
              stats.avgRMultiple != null ? stats.avgRMultiple.toFixed(2) : "—"
            }
          />
        </Box>
      </Box>

      <Box
        sx={{
          flex: 1,
          minHeight: 0,
          overflow: "hidden",
          display: "flex",
          flexDirection: "column",
          gap: 2,
        }}
      >
        <Box sx={{ flex: 1, minHeight: 0, overflow: "hidden" }}>
          <ZenTable
            columns={summaryColumns}
            rows={tableRows}
            getRowId={summaryRowId}
            loading={loading}
            emptyMessage="No backtest data yet. Pick a symbol and run 1Y backtest."
            defaultPageSize={50}
            fillHeight
            enableSearch
            search={search}
            onSearchChange={setSearch}
            searchPlaceholder="Search symbol or stock name…"
            onVisibleRowsChange={onVisibleRowsChange}
          />
        </Box>

        {showManualForm ? (
          <Box
            sx={{
              flexShrink: 0,
              p: 2,
              bgcolor: "background.paper",
              border: "1px solid",
              borderColor: "divider",
              borderRadius: 1,
            }}
          >
            <Typography variant="subtitle1" sx={{ mb: 1.5 }}>
              {form.id ? "Edit manual note" : "Add manual note"}{" "}
              {runTarget ? `· ${runTarget.symbol}` : ""}
            </Typography>
            <MuiStack direction="row" spacing={1.5} flexWrap="wrap" useFlexGap>
              <FormControl size="small" sx={{ minWidth: 120 }}>
                <InputLabel>Strategy</InputLabel>
                <Select
                  label="Strategy"
                  value={form.strategy}
                  onChange={(e) => setForm({ ...form, strategy: e.target.value })}
                >
                  <MenuItem value="signals">Signals</MenuItem>
                  <MenuItem value="liquidity">Liquidity</MenuItem>
                  <MenuItem value="liquidity_fresh">Liquidity Fresh</MenuItem>
                  <MenuItem value="liquidity_v2">Liquidity V2</MenuItem>
                  <MenuItem value="confluence">Confluence</MenuItem>
                  <MenuItem value="breakout">Breakout</MenuItem>
                  <MenuItem value="trade_score">Trade Score</MenuItem>
                </Select>
              </FormControl>
              <FormControl size="small" sx={{ minWidth: 100 }}>
                <InputLabel>Side</InputLabel>
                <Select
                  label="Side"
                  value={form.side}
                  onChange={(e) => setForm({ ...form, side: e.target.value })}
                >
                  <MenuItem value="buy">BUY</MenuItem>
                  <MenuItem value="sell">SELL</MenuItem>
                </Select>
              </FormControl>
              <TextField
                size="small"
                label="Signal date"
                type="date"
                InputLabelProps={{ shrink: true }}
                value={form.signalDate}
                onChange={(e) => setForm({ ...form, signalDate: e.target.value })}
              />
              <TextField
                size="small"
                label="Entry"
                type="number"
                value={form.entryPrice || ""}
                onChange={(e) => setForm({ ...form, entryPrice: Number(e.target.value) || 0 })}
                sx={{ width: 110 }}
              />
              <TextField
                size="small"
                label="SL"
                type="number"
                value={form.initialStopLoss || ""}
                onChange={(e) =>
                  setForm({ ...form, initialStopLoss: Number(e.target.value) || 0 })
                }
                sx={{ width: 110 }}
              />
              <FormControl size="small" sx={{ minWidth: 120 }}>
                <InputLabel>Result</InputLabel>
                <Select
                  label="Result"
                  value={form.result}
                  onChange={(e) => setForm({ ...form, result: e.target.value })}
                >
                  <MenuItem value="target">Target</MenuItem>
                  <MenuItem value="sl">SL</MenuItem>
                  <MenuItem value="skipped">Skipped</MenuItem>
                  <MenuItem value="open">Open</MenuItem>
                  <MenuItem value="time_stop">Time stop</MenuItem>
                </Select>
              </FormControl>
              <Button
                variant="contained"
                size="small"
                disabled={!runTarget || saving}
                startIcon={<FloppyDisk size={DEFAULT_SMALL_ICON_SIZE} />}
                onClick={() => void onSave()}
              >
                {saving ? "Saving…" : form.id ? "Update" : "Save note"}
              </Button>
            </MuiStack>
          </Box>
        ) : null}
      </Box>
    </MuiStack>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <Box>
      <Typography variant="caption" color="text.secondary">
        {label}
      </Typography>
      <Typography variant="h6" sx={{ fontWeight: 600, lineHeight: 1.2 }}>
        {value}
      </Typography>
    </Box>
  );
}
