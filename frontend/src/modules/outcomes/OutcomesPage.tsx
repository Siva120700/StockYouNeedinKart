import { useEffect, useMemo, useState } from "react";
import {
  Accordion,
  AccordionDetails,
  AccordionSummary,
  Alert,
  Button,
  Checkbox,
  Chip,
  FormControl,
  FormControlLabel,
  InputLabel,
  ListItemText,
  MenuItem,
  OutlinedInput,
  Select,
  type SelectChangeEvent,
  Stack,
  Switch,
  TextField,
  Typography,
} from "@mui/material";
import { ArrowsClockwise, CaretDown, DownloadSimple, Play } from "@phosphor-icons/react";
import { columnFactories } from "../../zen_components/table/columnFactories";
import ZenTable from "../../zen_components/table/ZenTable";
import { useZenPrimaryLayoutContext } from "../../zen_components/layout/ZenPrimaryLayoutProvider";
import { DEFAULT_SMALL_ICON_SIZE } from "../../constants";
import { OutcomesApi } from "./api";
import {
  resultLabel,
  strategyLabel,
  type SignalOutcome,
  type SignalOutcomeSummary,
} from "./types";

const ALL_OUTCOME_STRATEGIES = [
  "signals",
  "liquidity",
  "liquidity_fresh",
  "liquidity_v2",
  "confluence",
  "trade_score",
  "breakout",
  "options_intraday",
  "nifty_orb",
  "nifty_orb_liq_v2",
  "nifty_liq_breakout",
] as const;

type OutcomeStrategy = (typeof ALL_OUTCOME_STRATEGIES)[number];

type ResultFilter = "all" | "open" | "target" | "sl" | "time_stop";

function isOutcomeStrategy(value: string): value is OutcomeStrategy {
  return (ALL_OUTCOME_STRATEGIES as readonly string[]).includes(value);
}

function isAllOutcomeStrategies(selected: readonly OutcomeStrategy[]): boolean {
  return selected.length === ALL_OUTCOME_STRATEGIES.length;
}

function strategySelectionLabel(selected: readonly OutcomeStrategy[]): string {
  if (selected.length === 0) return "None";
  if (isAllOutcomeStrategies(selected)) return "All";
  if (selected.length === 1) return strategyLabel(selected[0]);
  return `${selected.length} strategies`;
}

function fmt(n: number | null | undefined, digits = 2): string {
  if (n == null || !Number.isFinite(Number(n))) return "—";
  return Number(n).toFixed(digits);
}

function strategyKey(summary: SignalOutcomeSummary): string {
  return summary.strategyFilter ?? "all";
}

function outcomesForStrategy(
  allOutcomes: SignalOutcome[],
  strategy: string | null,
): SignalOutcome[] {
  if (!strategy || strategy === "all") return allOutcomes;
  return allOutcomes.filter((o) => o.strategy === strategy);
}

/** Calendar days from entry (signal) date to exit date; null while still open. */
function durationDays(entryDate: string, exitDate: string | null | undefined): number | null {
  if (!exitDate) return null;
  const a = Date.parse(entryDate);
  const b = Date.parse(exitDate);
  if (!Number.isFinite(a) || !Number.isFinite(b)) return null;
  return Math.max(0, Math.round((b - a) / 86_400_000));
}

function avgDurationDays(list: SignalOutcome[]): number | null {
  const days = list
    .map((o) => durationDays(o.signalDate, o.exitDate))
    .filter((d): d is number => d != null);
  if (days.length === 0) return null;
  return days.reduce((s, d) => s + d, 0) / days.length;
}

function fmtDays(n: number | null | undefined): string {
  if (n == null || !Number.isFinite(n)) return "—";
  return `${n.toFixed(n < 10 ? 1 : 0)}d`;
}

function filterByStrategies<T>(
  items: T[],
  selected: readonly OutcomeStrategy[],
  getStrategy: (item: T) => string | null | undefined,
): T[] {
  if (isAllOutcomeStrategies(selected)) return items;
  const set = new Set<string>(selected);
  return items.filter((item) => {
    const s = getStrategy(item);
    return s != null && set.has(s);
  });
}

export default function OutcomesPage() {
  const { setTitle, setBreadcrumbs, setPageActions, setIsSyncing } =
    useZenPrimaryLayoutContext();
  const [summaries, setSummaries] = useState<SignalOutcomeSummary[]>([]);
  const [rows, setRows] = useState<SignalOutcome[]>([]);
  const [accordionRows, setAccordionRows] = useState<SignalOutcome[]>([]);
  const [strategyFilter, setStrategyFilter] = useState<OutcomeStrategy[]>([
    ...ALL_OUTCOME_STRATEGIES,
  ]);
  const [result, setResult] = useState<ResultFilter>("all");
  const [fromDate, setFromDate] = useState("");
  const [toDate, setToDate] = useState("");
  const [sectorCheck, setSectorCheck] = useState(false);
  const [expanded, setExpanded] = useState<string | false>(false);
  const [loading, setLoading] = useState(true);
  const [resolving, setResolving] = useState(false);
  const [importing, setImporting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);

  function onStrategyFilterChange(event: SelectChangeEvent<string[]>) {
    const raw = event.target.value;
    const value = typeof raw === "string" ? raw.split(",") : raw;
    if (value.includes("all")) {
      // "All" toggles: clear when everything is already selected.
      setStrategyFilter(
        isAllOutcomeStrategies(strategyFilter) ? [] : [...ALL_OUTCOME_STRATEGIES],
      );
      return;
    }
    setStrategyFilter(value.filter(isOutcomeStrategy));
  }

  async function refresh() {
    setError(null);
    setIsSyncing(true);
    try {
      const strat =
        strategyFilter.length === 1 ? strategyFilter[0]! : null;
      const res = result === "all" ? null : result;
      const sectorOnly = sectorCheck || null;
      const range = {
        fromDate: fromDate || null,
        toDate: toDate || null,
      };
      const [sum, list, accordionList] = await Promise.all([
        OutcomesApi.fetchSummaries(strat, sectorOnly, range),
        OutcomesApi.fetchOutcomes(strat, res, sectorOnly, range),
        OutcomesApi.fetchOutcomes(null, res, sectorOnly, range),
      ]);
      setSummaries(filterByStrategies(sum, strategyFilter, (s) => s.strategyFilter));
      setRows(filterByStrategies(list, strategyFilter, (o) => o.strategy));
      setAccordionRows(filterByStrategies(accordionList, strategyFilter, (o) => o.strategy));
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
      avgDuration: avgDurationDays(accordionRows),
    };
  }, [summaries, accordionRows]);

  const byStrategy = useMemo(() => {
    const map = new Map<string, SignalOutcome[]>();
    for (const s of summaries) {
      const key = strategyKey(s);
      const list = outcomesForStrategy(accordionRows, s.strategyFilter)
        .slice()
        .sort((a, b) => {
          const dateCmp = b.signalDate.localeCompare(a.signalDate);
          if (dateCmp !== 0) return dateCmp;
          return a.appSymbol.localeCompare(b.appSymbol);
        });
      map.set(key, list);
    }
    return map;
  }, [summaries, accordionRows]);

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
  }, [strategyFilter, result, sectorCheck, fromDate, toDate]);

  useEffect(() => {
    setPageActions(
      <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap">
        <FormControl size="small" sx={{ minWidth: 180 }}>
          <InputLabel>Strategy</InputLabel>
          <Select
            multiple
            label="Strategy"
            value={strategyFilter}
            onChange={onStrategyFilterChange}
            input={<OutlinedInput label="Strategy" />}
            renderValue={(selected) =>
              strategySelectionLabel(selected as OutcomeStrategy[])
            }
          >
            <MenuItem value="all">
              <Checkbox
                size="small"
                checked={isAllOutcomeStrategies(strategyFilter)}
                indeterminate={
                  strategyFilter.length > 0 &&
                  strategyFilter.length < ALL_OUTCOME_STRATEGIES.length
                }
              />
              <ListItemText primary="All" />
            </MenuItem>
            {ALL_OUTCOME_STRATEGIES.map((s) => (
              <MenuItem key={s} value={s}>
                <Checkbox size="small" checked={strategyFilter.includes(s)} />
                <ListItemText primary={strategyLabel(s)} />
              </MenuItem>
            ))}
          </Select>
        </FormControl>
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
        <TextField
          size="small"
          label="From"
          type="date"
          InputLabelProps={{ shrink: true }}
          value={fromDate}
          onChange={(e) => setFromDate(e.target.value)}
          sx={{ minWidth: 150 }}
        />
        <TextField
          size="small"
          label="To"
          type="date"
          InputLabelProps={{ shrink: true }}
          value={toDate}
          onChange={(e) => setToDate(e.target.value)}
          inputProps={fromDate ? { min: fromDate } : undefined}
          sx={{ minWidth: 150 }}
        />
        <FormControlLabel
          control={
            <Switch
              size="small"
              checked={sectorCheck}
              onChange={(e) => setSectorCheck(e.target.checked)}
            />
          }
          label="Sector check"
        />
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
  }, [strategyFilter, result, sectorCheck, fromDate, toDate, loading, resolving, importing]);

  /** Shared columns for accordion + Outcomes table (sortable + searchable via ZenTable). */
  const outcomeColumns = useMemo(
    () => [
      columnFactories.createTextColumn<SignalOutcome>({
        field: "appSymbol",
        headerName: "Stock",
        width: 100,
        getValue: (r) => r.appSymbol,
      }),
      columnFactories.createTextColumn<SignalOutcome>({
        field: "instrumentName",
        headerName: "Name",
        width: 140,
        getValue: (r) => r.instrumentName,
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
        getValue: (r) => r.side.toUpperCase(),
      }),
      columnFactories.createBooleanColumn<SignalOutcome>({
        field: "sectorConfirmed",
        headerName: "Sector",
        width: 80,
        getValue: (r) => r.sectorConfirmed === true,
      }),
      columnFactories.createTextColumn<SignalOutcome>({
        field: "signalDate",
        headerName: "Entry date",
        width: 110,
        getValue: (r) => r.signalDate,
      }),
      columnFactories.createTextColumn<SignalOutcome>({
        field: "exitDate",
        headerName: "Exit date",
        width: 110,
        getValue: (r) => r.exitDate ?? "—",
      }),
      columnFactories.createTextColumn<SignalOutcome>({
        field: "durationDays",
        headerName: "Days",
        width: 70,
        getValue: (r) => {
          const d = durationDays(r.signalDate, r.exitDate);
          return d == null ? "—" : String(d);
        },
      }),
      columnFactories.createTextColumn<SignalOutcome>({
        field: "entryPrice",
        headerName: "Entry",
        width: 90,
        getValue: (r) => fmt(r.entryPrice),
      }),
      columnFactories.createTextColumn<SignalOutcome>({
        field: "initialStopLoss",
        headerName: "SL",
        width: 90,
        getValue: (r) => fmt(r.initialStopLoss),
      }),
      columnFactories.createTextColumn<SignalOutcome>({
        field: "targetT1",
        headerName: "T1",
        width: 90,
        getValue: (r) => fmt(r.targetT1),
      }),
      columnFactories.createTextColumn<SignalOutcome>({
        field: "result",
        headerName: "Result",
        width: 100,
        getValue: (r) =>
          r.result === "target" && r.targetLevel
            ? `Target ${r.targetLevel}`
            : resultLabel(r.result),
      }),
      columnFactories.createTextColumn<SignalOutcome>({
        field: "exitPrice",
        headerName: "Exit",
        width: 90,
        getValue: (r) => fmt(r.exitPrice),
      }),
      columnFactories.createTextColumn<SignalOutcome>({
        field: "rMultiple",
        headerName: "R",
        width: 70,
        getValue: (r) => fmt(r.rMultiple),
      }),
      columnFactories.createTextColumn<SignalOutcome>({
        field: "pnlPct",
        headerName: "P&L %",
        width: 80,
        getValue: (r) => fmt(r.pnlPct),
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
        rate = target ÷ (target + SL). Expand a strategy to sort/filter stocks; Days = exit date −
        entry date. Sector check keeps only setups where the linked sector index also broke the prior
        2 sessions (same rule as live screeners).
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
        <Typography variant="body2">
          Avg duration: <b>{fmtDays(totals.avgDuration)}</b>
        </Typography>
      </Stack>

      <Typography variant="subtitle1">By strategy</Typography>
      {loading && summaries.length === 0 ? (
        <Typography color="text.secondary">Loading…</Typography>
      ) : summaries.length === 0 ? (
        <Typography color="text.secondary">No strategy summaries yet.</Typography>
      ) : (
        <Stack spacing={1}>
          {summaries.map((s) => {
            const key = strategyKey(s);
            const stocks = byStrategy.get(key) ?? [];
            const avgDur = avgDurationDays(stocks);
            return (
              <Accordion
                key={key}
                disableGutters
                expanded={expanded === key}
                onChange={(_, isExpanded) => setExpanded(isExpanded ? key : false)}
                sx={{ border: "1px solid", borderColor: "divider", borderRadius: 1 }}
              >
                <AccordionSummary expandIcon={<CaretDown size={DEFAULT_SMALL_ICON_SIZE} />}>
                  <Stack
                    direction={{ xs: "column", md: "row" }}
                    spacing={1.5}
                    alignItems={{ md: "center" }}
                    justifyContent="space-between"
                    width="100%"
                    pr={1}
                  >
                    <Typography fontWeight={700} minWidth={140}>
                      {strategyLabel(s.strategyFilter)}
                    </Typography>
                    <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
                      <Chip size="small" label={`${s.setups} setups`} />
                      <Chip size="small" color="success" variant="outlined" label={`Target ${s.targetHits}`} />
                      <Chip size="small" color="error" variant="outlined" label={`SL ${s.slHits}`} />
                      <Chip size="small" color="warning" variant="outlined" label={`Time ${s.timeStops}`} />
                      <Chip size="small" color="info" variant="outlined" label={`Open ${s.openCount}`} />
                      <Chip
                        size="small"
                        label={`Hit ${s.targetHitRatePct != null ? `${fmt(s.targetHitRatePct)}%` : "—"}`}
                      />
                      <Chip size="small" label={`Avg R ${fmt(s.avgRMultiple)}`} />
                      <Chip size="small" label={`Avg R:R ${fmt(s.avgRiskReward)}`} />
                      <Chip size="small" label={`Avg days ${fmtDays(avgDur)}`} />
                    </Stack>
                  </Stack>
                </AccordionSummary>
                <AccordionDetails sx={{ pt: 0 }}>
                  <ZenTable
                    rows={stocks}
                    columns={outcomeColumns}
                    loading={loading}
                    getRowId={(r) => r.id}
                    enableSearch
                    searchPlaceholder="Filter stocks…"
                    dense
                    defaultPageSize={25}
                    emptyMessage="No stock rows for this strategy with the current result filter."
                  />
                </AccordionDetails>
              </Accordion>
            );
          })}
        </Stack>
      )}

      <Typography variant="subtitle1">Outcomes</Typography>
      <ZenTable
        rows={rows}
        columns={outcomeColumns}
        loading={loading}
        getRowId={(r) => r.id}
        enableSearch
        searchPlaceholder="Filter outcomes…"
      />
    </Stack>
  );
}
