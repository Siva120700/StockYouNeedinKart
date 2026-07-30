import { useEffect, useMemo, useState } from "react";
import {
  Alert,
  Button,
  MenuItem,
  Stack,
  TextField,
  Typography,
} from "@mui/material";
import { ArrowsClockwise, DownloadSimple, Play } from "@phosphor-icons/react";
import { columnFactories } from "../../zen_components/table/columnFactories";
import ZenTable from "../../zen_components/table/ZenTable";
import { useZenPrimaryLayoutContext } from "../../zen_components/layout/ZenPrimaryLayoutProvider";
import { DEFAULT_SMALL_ICON_SIZE } from "../../constants";
import { OutcomesApi } from "./api";
import { strategyLabel, type SignalOutcome, type SignalOutcomeSummary } from "./types";

type StrategyFilter =
  | "all"
  | "signals"
  | "liquidity"
  | "liquidity_fresh"
  | "confluence"
  | "trade_score"
  | "breakout";

type ResultFilter = "all" | "open" | "target" | "sl" | "time_stop";

function fmt(n: number | null | undefined, digits = 2): string {
  if (n == null || !Number.isFinite(Number(n))) return "—";
  return Number(n).toFixed(digits);
}

export default function OutcomesPage() {
  const { setTitle, setBreadcrumbs, setPageActions, setIsSyncing } =
    useZenPrimaryLayoutContext();
  const [summaries, setSummaries] = useState<SignalOutcomeSummary[]>([]);
  const [rows, setRows] = useState<SignalOutcome[]>([]);
  const [strategy, setStrategy] = useState<StrategyFilter>("all");
  const [result, setResult] = useState<ResultFilter>("all");
  const [loading, setLoading] = useState(true);
  const [resolving, setResolving] = useState(false);
  const [importing, setImporting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);

  async function refresh() {
    setError(null);
    setIsSyncing(true);
    try {
      const strat = strategy === "all" ? null : strategy;
      const res = result === "all" ? null : result;
      const [sum, list] = await Promise.all([
        OutcomesApi.fetchSummaries(strat),
        OutcomesApi.fetchOutcomes(strat, res),
      ]);
      setSummaries(sum);
      setRows(list);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setLoading(false);
      setIsSyncing(false);
    }
  }

  async function onImport() {
    setImporting(true);
    setError(null);
    setInfo(null);
    try {
      const n = await OutcomesApi.backfillFromLive();
      setInfo(
        n > 0
          ? `Imported ${n} setup(s) from live Signals / Liquidity / Breakout / Trade Score.`
          : "No new setups to import (either live tables are empty, or they were already imported).",
      );
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setImporting(false);
    }
  }

  async function onResolve() {
    setResolving(true);
    setError(null);
    setInfo(null);
    try {
      const n = await OutcomesApi.resolveOpen();
      await refresh();
      if (n > 0) {
        setInfo(`Resolved ${n} outcome(s) to target / SL / time-stop.`);
        return;
      }
      const openToday = rows.filter((r) => r.result === "open").length;
      setInfo(
        openToday > 0
          ? `${openToday} still open. Resolve needs bars AFTER the signal date — same-day setups (e.g. today’s Signals) wait until the next trading day(s). Older setups stay open until price hits SL/target or the full horizon (20 days / 40 hours).`
          : "No open outcomes. Click Import live setups first (or run Signals / Liquidity).",
      );
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setResolving(false);
    }
  }

  const totals = useMemo(() => {
    const setups = summaries.reduce((s, r) => s + r.setups, 0);
    const targetHits = summaries.reduce((s, r) => s + r.targetHits, 0);
    const slHits = summaries.reduce((s, r) => s + r.slHits, 0);
    const openCount = summaries.reduce((s, r) => s + r.openCount, 0);
    const timeStops = summaries.reduce((s, r) => s + r.timeStops, 0);
    const decided = targetHits + slHits;
    const hitRate = decided > 0 ? (100 * targetHits) / decided : null;
    let rSum = 0;
    let rW = 0;
    for (const r of summaries) {
      if (r.avgRMultiple != null && Number.isFinite(Number(r.avgRMultiple))) {
        rSum += Number(r.avgRMultiple) * Math.max(1, r.setups);
        rW += Math.max(1, r.setups);
      }
    }
    return {
      setups,
      targetHits,
      slHits,
      openCount,
      timeStops,
      hitRate,
      avgR: rW > 0 ? rSum / rW : null,
    };
  }, [summaries]);

  useEffect(() => {
    setTitle("Accuracy");
    setBreadcrumbs([{ label: "Home" }, { label: "Accuracy" }]);
    void refresh();
    return () => setPageActions(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    void refresh();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [strategy, result]);

  useEffect(() => {
    setPageActions(
      <Stack direction="row" spacing={1} alignItems="center">
        <TextField
          select
          size="small"
          label="Strategy"
          value={strategy}
          onChange={(e) => setStrategy(e.target.value as StrategyFilter)}
          sx={{ minWidth: 160 }}
        >
          <MenuItem value="all">All</MenuItem>
          <MenuItem value="signals">Signals</MenuItem>
          <MenuItem value="liquidity">Liquidity</MenuItem>
          <MenuItem value="liquidity_fresh">Liquidity Fresh</MenuItem>
          <MenuItem value="confluence">Confluence</MenuItem>
          <MenuItem value="trade_score">Trade Score</MenuItem>
          <MenuItem value="breakout">Breakout</MenuItem>
        </TextField>
        <TextField
          select
          size="small"
          label="Result"
          value={result}
          onChange={(e) => setResult(e.target.value as ResultFilter)}
          sx={{ minWidth: 130 }}
        >
          <MenuItem value="all">All</MenuItem>
          <MenuItem value="open">Open</MenuItem>
          <MenuItem value="target">Target</MenuItem>
          <MenuItem value="sl">SL</MenuItem>
          <MenuItem value="time_stop">Time stop</MenuItem>
        </TextField>
        <Button
          size="small"
          variant="outlined"
          startIcon={<ArrowsClockwise size={DEFAULT_SMALL_ICON_SIZE} />}
          onClick={() => void refresh()}
          disabled={loading}
        >
          Refresh
        </Button>
        <Button
          size="small"
          variant="outlined"
          startIcon={<DownloadSimple size={DEFAULT_SMALL_ICON_SIZE} />}
          onClick={() => void onImport()}
          disabled={importing}
        >
          {importing ? "Importing…" : "Import live setups"}
        </Button>
        <Button
          size="small"
          variant="contained"
          startIcon={<Play size={DEFAULT_SMALL_ICON_SIZE} />}
          onClick={() => void onResolve()}
          disabled={resolving}
        >
          {resolving ? "Resolving…" : "Resolve open"}
        </Button>
      </Stack>,
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [strategy, result, loading, resolving, importing]);

  const summaryColumns = useMemo(
    () => [
      columnFactories.createTextColumn<SignalOutcomeSummary>({
        field: "strategyFilter",
        headerName: "Strategy",
        width: 140,
        getValue: (r) => strategyLabel(r.strategyFilter),
      }),
      columnFactories.createNumberColumn<SignalOutcomeSummary>({
        field: "setups",
        headerName: "Setups",
        width: 90,
        getValue: (r) => r.setups,
      }),
      columnFactories.createNumberColumn<SignalOutcomeSummary>({
        field: "targetHits",
        headerName: "Target",
        width: 90,
        getValue: (r) => r.targetHits,
      }),
      columnFactories.createNumberColumn<SignalOutcomeSummary>({
        field: "slHits",
        headerName: "SL",
        width: 80,
        getValue: (r) => r.slHits,
      }),
      columnFactories.createNumberColumn<SignalOutcomeSummary>({
        field: "timeStops",
        headerName: "Time stop",
        width: 100,
        getValue: (r) => r.timeStops,
      }),
      columnFactories.createNumberColumn<SignalOutcomeSummary>({
        field: "openCount",
        headerName: "Open",
        width: 80,
        getValue: (r) => r.openCount,
      }),
      columnFactories.createTextColumn<SignalOutcomeSummary>({
        field: "targetHitRatePct",
        headerName: "Hit rate",
        width: 100,
        getValue: (r) => (r.targetHitRatePct != null ? `${fmt(r.targetHitRatePct)}%` : "—"),
      }),
      columnFactories.createTextColumn<SignalOutcomeSummary>({
        field: "avgRMultiple",
        headerName: "Avg R",
        width: 90,
        getValue: (r) => fmt(r.avgRMultiple),
      }),
      columnFactories.createTextColumn<SignalOutcomeSummary>({
        field: "avgRiskReward",
        headerName: "Avg R:R",
        width: 90,
        getValue: (r) => fmt(r.avgRiskReward),
      }),
    ],
    [],
  );

  const detailColumns = useMemo(
    () => [
      columnFactories.createTextColumn<SignalOutcome>({
        field: "appSymbol",
        headerName: "Symbol",
        width: 100,
        getValue: (r) => r.appSymbol,
      }),
      columnFactories.createTextColumn<SignalOutcome>({
        field: "strategy",
        headerName: "Strategy",
        width: 120,
        getValue: (r) => strategyLabel(r.strategy),
      }),
      columnFactories.createTextColumn<SignalOutcome>({
        field: "side",
        headerName: "Side",
        width: 70,
        getValue: (r) => r.side,
      }),
      columnFactories.createTextColumn<SignalOutcome>({
        field: "signalDate",
        headerName: "Signal date",
        width: 110,
        getValue: (r) => r.signalDate,
      }),
      columnFactories.createTextColumn<SignalOutcome>({
        field: "result",
        headerName: "Result",
        width: 100,
        getValue: (r) => r.result,
      }),
      columnFactories.createTextColumn<SignalOutcome>({
        field: "targetLevel",
        headerName: "Level",
        width: 70,
        getValue: (r) => r.targetLevel ?? "—",
      }),
      columnFactories.createTextColumn<SignalOutcome>({
        field: "rMultiple",
        headerName: "R",
        width: 80,
        getValue: (r) => fmt(r.rMultiple),
      }),
      columnFactories.createTextColumn<SignalOutcome>({
        field: "pnlPct",
        headerName: "P&L %",
        width: 80,
        getValue: (r) => fmt(r.pnlPct),
      }),
      columnFactories.createTextColumn<SignalOutcome>({
        field: "exitDate",
        headerName: "Exit",
        width: 110,
        getValue: (r) => r.exitDate ?? "—",
      }),
    ],
    [],
  );

  return (
    <Stack spacing={2} p={2}>
      {error && <Alert severity="error">{error}</Alert>}
      {info && (
        <Alert severity="success" onClose={() => setInfo(null)}>
          {info}
        </Alert>
      )}
      <Alert severity="info">
        How this works: Accuracy tracks setups you already have. 1) Run Signals / Liquidity /
        Breakout / Trade Score (or click Import live setups). 2) Rows appear as open. 3) Click
        Resolve open (or wait for Worker) to mark target / SL / time-stop from future bars. Hit
        rate = target ÷ (target + SL).
      </Alert>
      {totals.setups === 0 && !loading && (
        <Alert severity="warning">
          No outcomes yet. Open Signals (or Liquidity / Breakout), click Run if the list is empty,
          then come back here and click Import live setups.
        </Alert>
      )}
      <Stack direction="row" spacing={3} flexWrap="wrap">
        <Typography variant="body2">
          Setups: <b>{totals.setups}</b>
        </Typography>
        <Typography variant="body2">
          Open: <b>{totals.openCount}</b>
        </Typography>
        <Typography variant="body2">
          Target: <b>{totals.targetHits}</b>
        </Typography>
        <Typography variant="body2">
          SL: <b>{totals.slHits}</b>
        </Typography>
        <Typography variant="body2">
          Time stop: <b>{totals.timeStops}</b>
        </Typography>
        <Typography variant="body2">
          Hit rate: <b>{totals.hitRate != null ? `${fmt(totals.hitRate)}%` : "—"}</b>
        </Typography>
        <Typography variant="body2">
          Avg R: <b>{fmt(totals.avgR)}</b>
        </Typography>
      </Stack>
      <Typography variant="subtitle1">By strategy</Typography>
      <ZenTable
        rows={summaries}
        columns={summaryColumns}
        loading={loading}
        getRowId={(r) => r.strategyFilter ?? "all"}
      />
      <Typography variant="subtitle1">Outcomes</Typography>
      <ZenTable rows={rows} columns={detailColumns} loading={loading} getRowId={(r) => r.id} />
    </Stack>
  );
}
