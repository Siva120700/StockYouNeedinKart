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
import { CaretLeft, CaretRight, FloppyDisk, Play, Trash } from "@phosphor-icons/react";
import { ActionFactory, DataFactory } from "../api/factories";
import type {
  BacktestNote,
  BacktestNoteInput,
  BacktestSymbolSummary,
  UniverseInstrument,
} from "../api/types";
import { columnFactories } from "../zen_components/table/columnFactories";
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

function numOrNull(v: string): number | null {
  if (v.trim() === "") return null;
  const n = Number(v);
  return Number.isFinite(n) ? n : null;
}

export default function BacktestPage() {
  const { setTitle, setBreadcrumbs, setPageActions, setIsSyncing } =
    useZenPrimaryLayoutContext();

  const [universe, setUniverse] = useState<UniverseInstrument[]>([]);
  const [index, setIndex] = useState(0);
  const [strategyFilter, setStrategyFilter] = useState<StrategyFilter>("all");
  const [notes, setNotes] = useState<BacktestNote[]>([]);
  const [summary, setSummary] = useState<BacktestSymbolSummary | null>(null);
  const [form, setForm] = useState<BacktestNoteInput>(emptyForm(""));
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [running, setRunning] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const current = universe[index] ?? null;
  const strategyArg = strategyFilter === "all" ? null : strategyFilter;

  const loadSymbolData = useCallback(
    async (instrumentId: string) => {
      setIsSyncing(true);
      setError(null);
      try {
        const [n, s] = await Promise.all([
          DataFactory.backtestNotes(instrumentId, strategyArg),
          DataFactory.backtestSummary(instrumentId, strategyArg),
        ]);
        setNotes(n);
        setSummary(s);
      } catch (e) {
        setError(e instanceof Error ? e.message : String(e));
      } finally {
        setIsSyncing(false);
        setLoading(false);
      }
    },
    [strategyArg, setIsSyncing],
  );

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
          setIndex(0);
          setForm(emptyForm(sorted[0].id));
          await loadSymbolData(sorted[0].id);
        } else {
          setLoading(false);
        }
      } catch (e) {
        setError(e instanceof Error ? e.message : String(e));
        setLoading(false);
      }
    })();
    return () => setPageActions(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (!current) return;
    setForm((prev) => ({ ...emptyForm(current.id), strategy: prev.strategy || "signals" }));
    void loadSymbolData(current.id);
  }, [current?.id, strategyFilter]); // eslint-disable-line react-hooks/exhaustive-deps

  function go(delta: number) {
    if (universe.length === 0) return;
    setIndex((i) => (i + delta + universe.length) % universe.length);
  }

  async function onRunHistorical() {
    if (!current) return;
    setRunning(true);
    setError(null);
    setIsSyncing(true);
    try {
      const strategies =
        strategyFilter === "all" ? (["signals", "liquidity"] as const) : ([strategyFilter] as const);
      for (const s of strategies) {
        await ActionFactory.runHistoricalBacktest(current.id, s);
      }
      await loadSymbolData(current.id);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setRunning(false);
      setIsSyncing(false);
    }
  }

  async function onSave() {
    if (!current) return;
    setSaving(true);
    setError(null);
    try {
      const payload: BacktestNoteInput = {
        ...form,
        instrumentId: current.id,
        entryPrice: Number(form.entryPrice) || 0,
        initialStopLoss: Number(form.initialStopLoss) || 0,
        targetHitPct:
          form.result === "target" && form.targetHitPct == null ? 100 : form.targetHitPct,
        targetLevel: form.result === "target" ? form.targetLevel || "t1" : form.targetLevel,
      };
      await ActionFactory.upsertBacktestNote(payload);
      setForm(emptyForm(current.id));
      await loadSymbolData(current.id);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setSaving(false);
    }
  }

  async function onDelete(noteId: string) {
    if (!current) return;
    setError(null);
    try {
      await ActionFactory.deleteBacktestNote(noteId);
      await loadSymbolData(current.id);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  }

  function loadIntoForm(note: BacktestNote) {
    setForm({
      id: note.id,
      instrumentId: note.instrumentId,
      strategy: note.strategy,
      side: note.side,
      signalDate: note.signalDate?.slice(0, 10) ?? todayIso(),
      entryPrice: note.entryPrice,
      initialStopLoss: note.initialStopLoss,
      targetT1: note.targetT1 ?? null,
      targetT2: note.targetT2 ?? null,
      targetT3: note.targetT3 ?? null,
      result: note.result,
      targetLevel: note.targetLevel ?? null,
      targetHitPct: note.targetHitPct ?? null,
      exitPrice: note.exitPrice ?? null,
      exitDate: note.exitDate?.slice(0, 10) ?? null,
      pnlPct: note.pnlPct ?? null,
      rMultiple: note.rMultiple ?? null,
      notes: note.notes ?? "",
      wouldTakeLive: note.wouldTakeLive ?? null,
    });
  }

  const columns = useMemo(
    () => [
      columnFactories.createTextColumn<BacktestNote>({
        field: "signalDate",
        headerName: "Date",
        width: 110,
        getValue: (r) => r.signalDate?.slice(0, 10) ?? "",
      }),
      columnFactories.createTextColumn<BacktestNote>({
        field: "strategy",
        headerName: "Strategy",
        width: 100,
        getValue: (r) => r.strategy,
      }),
      columnFactories.createTextColumn<BacktestNote>({
        field: "source",
        headerName: "Src",
        width: 70,
        getValue: (r) => r.source ?? "manual",
      }),
      columnFactories.createStatusColumn<BacktestNote>(
        {
          buy: { label: "BUY", color: "#2e7d32" },
          sell: { label: "SELL", color: "#c62828" },
        },
        { field: "side", headerName: "Side", width: 80, getValue: (r) => r.side },
      ),
      columnFactories.createNumberColumn<BacktestNote>({
        field: "entryPrice",
        headerName: "Entry",
        width: 90,
        minDecimalPlaces: 2,
        getValue: (r) => r.entryPrice,
      }),
      columnFactories.createTextColumn<BacktestNote>({
        field: "result",
        headerName: "Result",
        width: 90,
        getValue: (r) =>
          r.result === "target"
            ? `Target ${(r.targetLevel ?? "").toUpperCase()}`
            : r.result === "sl"
              ? "SL"
              : r.result,
      }),
      columnFactories.createTextColumn<BacktestNote>({
        field: "targetHitPct",
        headerName: "Target %",
        width: 90,
        getValue: (r) =>
          r.targetHitPct != null && Number.isFinite(Number(r.targetHitPct))
            ? `${Number(r.targetHitPct).toFixed(0)}%`
            : "",
      }),
      columnFactories.createTextColumn<BacktestNote>({
        field: "pnlPct",
        headerName: "PnL %",
        width: 80,
        getValue: (r) =>
          r.pnlPct != null && Number.isFinite(Number(r.pnlPct))
            ? `${Number(r.pnlPct).toFixed(2)}%`
            : "",
      }),
      columnFactories.createTextColumn<BacktestNote>({
        field: "notes",
        headerName: "Notes",
        width: 180,
        getValue: (r) => r.notes,
      }),
      columnFactories.createActionColumn<BacktestNote>(
        () => [
          {
            icon: <FloppyDisk size={DEFAULT_SMALL_ICON_SIZE} />,
            tooltip: "Edit",
            onClick: (r) => loadIntoForm(r),
          },
          {
            icon: <Trash size={DEFAULT_SMALL_ICON_SIZE} />,
            tooltip: "Delete",
            onClick: (r) => void onDelete(r.id),
          },
        ],
        { field: "actions", headerName: "", width: 96 },
      ),
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [current?.id],
  );

  return (
    <MuiStack spacing={2}>
      {error ? <Alert severity="error">{error}</Alert> : null}

      <MuiStack direction="row" spacing={1} alignItems="center" flexWrap="wrap">
        <Button
          size="small"
          variant="outlined"
          disabled={universe.length === 0}
          startIcon={<CaretLeft size={DEFAULT_SMALL_ICON_SIZE} />}
          onClick={() => go(-1)}
        >
          Prev
        </Button>
        <Autocomplete
          sx={{ minWidth: 260 }}
          size="small"
          options={universe}
          getOptionLabel={(o) => `${o.symbol} — ${o.name}`}
          value={current}
          onChange={(_, v) => {
            if (!v) return;
            const i = universe.findIndex((u) => u.id === v.id);
            if (i >= 0) setIndex(i);
          }}
          renderInput={(params) => <TextField {...params} label="Symbol" />}
          isOptionEqualToValue={(a, b) => a.id === b.id}
        />
        <Button
          size="small"
          variant="outlined"
          disabled={universe.length === 0}
          endIcon={<CaretRight size={DEFAULT_SMALL_ICON_SIZE} />}
          onClick={() => go(1)}
        >
          Next
        </Button>
        <Typography variant="body2" color="text.secondary">
          {universe.length ? `${index + 1} / ${universe.length}` : "No universe"}
        </Typography>
        <FormControl size="small" sx={{ minWidth: 140 }}>
          <InputLabel>Strategy</InputLabel>
          <Select
            label="Strategy"
            value={strategyFilter}
            onChange={(e) => setStrategyFilter(e.target.value as StrategyFilter)}
          >
            <MenuItem value="all">All</MenuItem>
            <MenuItem value="signals">Signals</MenuItem>
            <MenuItem value="liquidity">Liquidity</MenuItem>
          </Select>
        </FormControl>
        <Button
          size="small"
          variant="contained"
          disabled={!current || running || loading}
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
      </MuiStack>

      {summary && current ? (
        <Box
          sx={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(120px, 1fr))",
            gap: 1.5,
            p: 1.5,
            bgcolor: "background.paper",
            border: "1px solid",
            borderColor: "divider",
            borderRadius: 1,
          }}
        >
          <Stat label="In strategy" value={String(summary.timesInStrategy)} />
          <Stat label="Target hits" value={String(summary.targetHits)} />
          <Stat label="SL hits" value={String(summary.slHits)} />
          <Stat
            label="Target hit rate"
            value={
              summary.targetHitRatePct != null
                ? `${Number(summary.targetHitRatePct).toFixed(1)}%`
                : "—"
            }
          />
          <Stat
            label="Avg target %"
            value={
              summary.avgTargetHitPct != null
                ? `${Number(summary.avgTargetHitPct).toFixed(0)}%`
                : "—"
            }
          />
          <Stat label="Skipped" value={String(summary.skipped)} />
        </Box>
      ) : null}

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
          {form.id ? "Edit note" : "Add note"} {current ? `· ${current.symbol}` : ""}
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
            onChange={(e) => setForm({ ...form, initialStopLoss: Number(e.target.value) || 0 })}
            sx={{ width: 110 }}
          />
          <TextField
            size="small"
            label="T1"
            type="number"
            value={form.targetT1 ?? ""}
            onChange={(e) => setForm({ ...form, targetT1: numOrNull(e.target.value) })}
            sx={{ width: 100 }}
          />
          <TextField
            size="small"
            label="T2"
            type="number"
            value={form.targetT2 ?? ""}
            onChange={(e) => setForm({ ...form, targetT2: numOrNull(e.target.value) })}
            sx={{ width: 100 }}
          />
          <TextField
            size="small"
            label="T3"
            type="number"
            value={form.targetT3 ?? ""}
            onChange={(e) => setForm({ ...form, targetT3: numOrNull(e.target.value) })}
            sx={{ width: 100 }}
          />
          <FormControl size="small" sx={{ minWidth: 120 }}>
            <InputLabel>Result</InputLabel>
            <Select
              label="Result"
              value={form.result}
              onChange={(e) => {
                const result = e.target.value;
                setForm({
                  ...form,
                  result,
                  targetLevel: result === "target" ? form.targetLevel || "t1" : null,
                  targetHitPct:
                    result === "target"
                      ? form.targetHitPct ?? 100
                      : result === "sl"
                        ? 0
                        : form.targetHitPct,
                });
              }}
            >
              <MenuItem value="target">Target</MenuItem>
              <MenuItem value="sl">SL</MenuItem>
              <MenuItem value="skipped">Skipped</MenuItem>
              <MenuItem value="open">Open</MenuItem>
              <MenuItem value="time_stop">Time stop</MenuItem>
            </Select>
          </FormControl>
          {form.result === "target" ? (
            <FormControl size="small" sx={{ minWidth: 90 }}>
              <InputLabel>Level</InputLabel>
              <Select
                label="Level"
                value={form.targetLevel || "t1"}
                onChange={(e) => setForm({ ...form, targetLevel: e.target.value })}
              >
                <MenuItem value="t1">T1</MenuItem>
                <MenuItem value="t2">T2</MenuItem>
                <MenuItem value="t3">T3</MenuItem>
              </Select>
            </FormControl>
          ) : null}
          <TextField
            size="small"
            label="Target hit %"
            type="number"
            value={form.targetHitPct ?? ""}
            onChange={(e) => setForm({ ...form, targetHitPct: numOrNull(e.target.value) })}
            sx={{ width: 120 }}
          />
          <TextField
            size="small"
            label="Exit"
            type="number"
            value={form.exitPrice ?? ""}
            onChange={(e) => setForm({ ...form, exitPrice: numOrNull(e.target.value) })}
            sx={{ width: 100 }}
          />
          <TextField
            size="small"
            label="Exit date"
            type="date"
            InputLabelProps={{ shrink: true }}
            value={form.exitDate ?? ""}
            onChange={(e) => setForm({ ...form, exitDate: e.target.value || null })}
          />
          <TextField
            size="small"
            label="PnL %"
            type="number"
            value={form.pnlPct ?? ""}
            onChange={(e) => setForm({ ...form, pnlPct: numOrNull(e.target.value) })}
            sx={{ width: 100 }}
          />
          <TextField
            size="small"
            label="R multiple"
            type="number"
            value={form.rMultiple ?? ""}
            onChange={(e) => setForm({ ...form, rMultiple: numOrNull(e.target.value) })}
            sx={{ width: 110 }}
          />
          <FormControlLabel
            control={
              <Switch
                size="small"
                checked={form.wouldTakeLive === true}
                onChange={(e) =>
                  setForm({ ...form, wouldTakeLive: e.target.checked ? true : null })
                }
              />
            }
            label="Would take live"
          />
          <TextField
            size="small"
            label="Notes"
            value={form.notes ?? ""}
            onChange={(e) => setForm({ ...form, notes: e.target.value })}
            sx={{ minWidth: 220, flex: 1 }}
          />
          <Button
            variant="contained"
            size="small"
            disabled={!current || saving}
            startIcon={<FloppyDisk size={DEFAULT_SMALL_ICON_SIZE} />}
            onClick={() => void onSave()}
          >
            {saving ? "Saving…" : form.id ? "Update" : "Save note"}
          </Button>
          {form.id ? (
            <Button
              size="small"
              variant="text"
              onClick={() => current && setForm(emptyForm(current.id))}
            >
              Clear
            </Button>
          ) : null}
        </MuiStack>
      </Box>

      <ZenTable
        columns={columns}
        rows={notes}
        getRowId={(r) => r.id}
        loading={loading}
        emptyMessage={
          current
            ? "No backtest notes for this symbol yet. Add one above."
            : "No universe symbols. Start the API so seed can run."
        }
      />
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
