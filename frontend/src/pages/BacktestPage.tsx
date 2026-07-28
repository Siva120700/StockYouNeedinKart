import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  Alert,
  Autocomplete,
  Box,
  Button,
  FormControl,
  FormControlLabel,
  InputLabel,
  LinearProgress,
  MenuItem,
  Select,
  Stack as MuiStack,
  Switch,
  TextField,
  Typography,
} from "@mui/material";
import { FloppyDisk, Play, Stop } from "@phosphor-icons/react";
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

type StrategyFilter = "all" | "signals" | "liquidity";

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

function strategyLabel(strategy: string | null | undefined): string {
  if (strategy === "liquidity") return "Liquidity";
  if (strategy === "signals") return "Signals";
  return strategy ?? "";
}

function summaryRowId(row: BacktestSymbolSummary): string {
  return `${row.instrumentId}-${row.strategyFilter ?? "unknown"}`;
}

function strategiesForFilter(filter: StrategyFilter): readonly ("signals" | "liquidity")[] {
  return filter === "all" ? (["signals", "liquidity"] as const) : ([filter] as const);
}

function isStockCompleted(
  instrumentId: string,
  strategies: readonly ("signals" | "liquidity")[],
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
      r.targetHitRatePct != null ? `${Number(r.targetHitRatePct).toFixed(1)}%` : "—",
  }),
  columnFactories.createTextColumn<BacktestSymbolSummary>({
    field: "avgTargetHitPct",
    headerName: "Avg target %",
    width: 120,
    getValue: (r) =>
      r.avgTargetHitPct != null ? `${Number(r.avgTargetHitPct).toFixed(0)}%` : "—",
  }),
];

export default function BacktestPage() {
  const { setTitle, setBreadcrumbs, setPageActions, setIsSyncing } =
    useZenPrimaryLayoutContext();

  const [universe, setUniverse] = useState<UniverseInstrument[]>([]);
  const [runTarget, setRunTarget] = useState<UniverseInstrument | null>(null);
  const [strategyFilter, setStrategyFilter] = useState<StrategyFilter>(() => {
    const saved = sessionStorage.getItem("backtest.strategyFilter");
    return saved === "signals" || saved === "liquidity" || saved === "all"
      ? saved
      : "all";
  });
  const [summaries, setSummaries] = useState<BacktestSymbolSummary[]>([]);
  const [form, setForm] = useState<BacktestNoteInput>(emptyForm(""));
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [running, setRunning] = useState(false);
  const [runningAll, setRunningAll] = useState(false);
  const [skipCompleted, setSkipCompleted] = useState(true);
  const [batchProgress, setBatchProgress] = useState<{
    current: number;
    total: number;
    symbol: string;
    strategy: string;
    failed: number;
  } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [showManualForm, setShowManualForm] = useState(false);
  const cancelBatchRef = useRef(false);

  const loadSummaries = useCallback(async () => {
    setIsSyncing(true);
    setError(null);
    try {
      const strat = strategyFilter === "all" ? null : strategyFilter;
      const rows = await DataFactory.backtestSummaries(strat);
      setSummaries(rows);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setIsSyncing(false);
      setLoading(false);
    }
  }, [strategyFilter, setIsSyncing]);

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
    if (!runTarget) return;
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

  async function onRunAllHistorical() {
    if (universe.length === 0 || runningAll) return;
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
    if (strategyFilter === "all") return summaries;
    return summaries.filter((r) => r.strategyFilter === strategyFilter);
  }, [summaries, strategyFilter]);

  return (
    <MuiStack
      spacing={2}
      sx={{ height: "100%", overflow: "hidden", minHeight: 0 }}
    >
      <Box sx={{ flexShrink: 0 }}>
        {error ? <Alert severity="error">{error}</Alert> : null}

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
          sx={{ mt: error || batchProgress ? 2 : 0 }}
        >
        <FormControl size="small" sx={{ minWidth: 140 }} disabled={runningAll}>
          <InputLabel>Strategy</InputLabel>
          <Select
            label="Strategy"
            value={strategyFilter}
            onChange={(e) => {
              const next = e.target.value as StrategyFilter;
              setStrategyFilter(next);
              sessionStorage.setItem("backtest.strategyFilter", next);
            }}
          >
            <MenuItem value="all">All</MenuItem>
            <MenuItem value="signals">Signals</MenuItem>
            <MenuItem value="liquidity">Liquidity</MenuItem>
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
          variant="contained"
          disabled={!runTarget || running || runningAll || loading}
          startIcon={<Play size={DEFAULT_SMALL_ICON_SIZE} />}
          onClick={() => void onRunHistorical()}
        >
          {running
            ? strategyFilter === "liquidity" || strategyFilter === "all"
              ? "Running 1Y… (may take a few min)"
              : "Running 1Y…"
            : strategyFilter === "all"
              ? "Run 1Y (both)"
              : "Run 1Y backtest"}
        </Button>
        <Button
          size="small"
          variant="outlined"
          disabled={universe.length === 0 || running || runningAll || loading}
          startIcon={<Play size={DEFAULT_SMALL_ICON_SIZE} />}
          onClick={() => void onRunAllHistorical()}
        >
          {runningAll ? "Running all…" : "Run all 1Y"}
        </Button>
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
        </MuiStack>
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
