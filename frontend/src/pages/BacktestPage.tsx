import { useCallback, useEffect, useMemo, useState } from "react";
import {
  Alert,
  Autocomplete,
  Box,
  Button,
  FormControl,
  FormControlLabel,
  InputLabel,
  MenuItem,
  Select,
  Stack as MuiStack,
  Switch,
  TextField,
  Typography,
} from "@mui/material";
import { FloppyDisk, Play } from "@phosphor-icons/react";
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
  const [error, setError] = useState<string | null>(null);
  const [showManualForm, setShowManualForm] = useState(false);

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
      const strategies =
        strategyFilter === "all" ? (["signals", "liquidity"] as const) : ([strategyFilter] as const);
      for (const s of strategies) {
        await ActionFactory.runHistoricalBacktest(runTarget.id, s);
      }
      await loadSummaries();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setRunning(false);
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
    <MuiStack spacing={2}>
      {error ? <Alert severity="error">{error}</Alert> : null}

      <MuiStack direction="row" spacing={1} alignItems="center" flexWrap="wrap">
        <FormControl size="small" sx={{ minWidth: 140 }}>
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
        />
        <Button
          size="small"
          variant="contained"
          disabled={!runTarget || running || loading}
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

      <ZenTable
        columns={summaryColumns}
        rows={tableRows}
        getRowId={summaryRowId}
        loading={loading}
        emptyMessage="No backtest data yet. Pick a symbol and run 1Y backtest."
        defaultPageSize={50}
      />

      {showManualForm ? (
        <Box
          sx={{
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
    </MuiStack>
  );
}
