import { useEffect, useMemo, useState } from "react";
import {
  Alert,
  Box,
  Button,
  Chip,
  FormControlLabel,
  LinearProgress,
  Stack,
  Switch,
  Tab,
  Tabs,
  Typography,
} from "@mui/material";
import { Handshake, Play } from "@phosphor-icons/react";
import { columnFactories } from "../../zen_components/table/columnFactories";
import type { ColumnConfig } from "../../zen_components/table/columnTypes";
import ZenTable from "../../zen_components/table/ZenTable";
import { useZenPrimaryLayoutContext } from "../../zen_components/layout/ZenPrimaryLayoutProvider";
import { DEFAULT_SMALL_ICON_SIZE } from "../../constants";
import { TradeScoreApi } from "./api";
import type { TradeConfidenceScore } from "./types";
import { ratingLabel } from "./types";
import {
  createHistoricalHitRateColumn,
  loadHistoricalHitRates,
  type HitRateByInstrument,
} from "../../utils/historicalHitRate";
import { createSectorRsColumn } from "../../utils/sectorRelativeStrength.tsx";
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

const HISTORY_SCOPE = "trade_score";

function riskReward(row: TradeConfidenceScore): number | null {
  const entry = Number(row.entryPrice);
  const sl = Number(row.initialStopLoss);
  const t1 = Number(row.targetT1);
  if (![entry, sl, t1].every(Number.isFinite) || entry === 0) return null;
  const risk = row.side === "sell" ? sl - entry : entry - sl;
  const reward = row.side === "sell" ? entry - t1 : t1 - entry;
  if (risk <= 0 || reward <= 0) return null;
  return reward / risk;
}

function formatTarget(row: TradeConfidenceScore, target: number | null | undefined) {
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

function formatSl(row: TradeConfidenceScore) {
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

export default function TradeScorePage() {
  const { setTitle, setBreadcrumbs, setPageActions, setIsSyncing } =
    useZenPrimaryLayoutContext();
  const [rows, setRows] = useState<TradeConfidenceScore[]>([]);
  const [historyRows, setHistoryRows] = useState<SignalDayEntry<TradeConfidenceScore>[]>([]);
  const [tradedRows, setTradedRows] = useState<SignalDayEntry<TradeConfidenceScore>[]>([]);
  const [tab, setTab] = useState<SignalsTab>("active");
  const [selectedTradedIds, setSelectedTradedIds] = useState<string[]>([]);
  const [hitRates, setHitRates] = useState<HitRateByInstrument>(() => new Map());
  const [loading, setLoading] = useState(true);
  const [running, setRunning] = useState(false);
  const [minScore, setMinScore] = useState(60);
  const [hideLaggingRs, setHideLaggingRs] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);

  async function refresh() {
    setError(null);
    setIsSyncing(true);
    try {
      const [scores, rates] = await Promise.all([
        TradeScoreApi.fetchScores(),
        loadHistoricalHitRates("trade_score"),
      ]);
      setRows(scores);
      setHitRates(rates);
      const synced = syncSignalDayHistory(HISTORY_SCOPE, scores);
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
      await TradeScoreApi.runAnalysis(true, true);
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setRunning(false);
    }
  }

  async function onTrade(row: TradeConfidenceScore) {
    setError(null);
    setInfo(null);
    try {
      await TradeScoreApi.openPosition(row.id);
      markSignalDayTraded(HISTORY_SCOPE, row);
      setInfo(`${row.appSymbol} moved to Positions (Traded).`);
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  }

  function tradedRowId(r: TradeConfidenceScore) {
    return `${r.instrumentId}:${r.side}:${r.id}`;
  }

  function onDeleteSelectedTraded() {
    const selected = tradedRows.filter((r) => selectedTradedIds.includes(tradedRowId(r)));
    if (selected.length === 0) return;
    unmarkSignalDayTraded(HISTORY_SCOPE, selected);
    setSelectedTradedIds([]);
    const synced = syncSignalDayHistory(HISTORY_SCOPE, rows);
    setHistoryRows(synced.history);
    setTradedRows(synced.traded);
    setInfo(`Removed ${selected.length} from Traded.`);
  }

  const filteredActive = useMemo(() => {
    let list = rows.filter((r) => r.confidenceScore >= minScore);
    if (hideLaggingRs) list = list.filter((r) => !r.sectorRs?.downranked);
    return list;
  }, [rows, minScore, hideLaggingRs]);

  const tableRows = useMemo(() => {
    if (tab === "history") return historyRows;
    if (tab === "traded") return tradedRows;
    return filteredActive;
  }, [tab, filteredActive, historyRows, tradedRows]);

  useEffect(() => {
    setTitle("Trade Score");
    setBreadcrumbs([{ label: "Home" }, { label: "Trade Score" }]);
    void refresh();
    return () => setPageActions(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    setPageActions(
      <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap">
        {tab === "active" ? (
          <>
            <FormControlLabel
              control={
                <Switch
                  size="small"
                  checked={minScore >= 75}
                  onChange={(e) => setMinScore(e.target.checked ? 75 : 60)}
                />
              }
              label="Min ★★★★ (75+)"
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
            />
          </>
        ) : null}
        <Button
          variant="contained"
          size="small"
          disabled={running}
          startIcon={<Play size={DEFAULT_SMALL_ICON_SIZE} />}
          onClick={() => void onRun()}
        >
          {running ? "Scoring…" : "Run trade score"}
        </Button>
      </Stack>,
    );
  }, [running, minScore, hideLaggingRs, tab, setPageActions]);

  const columns = useMemo(() => {
    const cols: ColumnConfig<TradeConfidenceScore>[] = [
      columnFactories.createNumberColumn<TradeConfidenceScore>({
        field: "confidenceScore",
        headerName: "Score",
        width: 80,
        minDecimalPlaces: 0,
        getValue: (r) => r.confidenceScore,
      }),
      columnFactories.createTextColumn<TradeConfidenceScore>({
        field: "rating",
        headerName: "Rating",
        width: 150,
        getValue: (r) => ratingLabel(r.rating),
      }),
      columnFactories.createTextColumn<TradeConfidenceScore>({
        field: "appSymbol",
        headerName: "Symbol",
        width: 110,
        getValue: (r) => r.appSymbol,
      }),
      createSectorRsColumn<TradeConfidenceScore>((r) => r.sectorRs),
      createHistoricalHitRateColumn<TradeConfidenceScore>(hitRates, (r) => r.instrumentId),
      columnFactories.createStatusColumn<TradeConfidenceScore>(
        {
          buy: { label: "BUY", color: "#2e7d32" },
          sell: { label: "SELL", color: "#c62828" },
        },
        { field: "side", headerName: "Side", width: 90, getValue: (r) => r.side },
      ),
      columnFactories.createNumberColumn<TradeConfidenceScore>({
        field: "entryPrice",
        headerName: "Entry",
        width: 100,
        minDecimalPlaces: 2,
        getValue: (r) => r.entryPrice,
      }),
      columnFactories.createTextColumn<TradeConfidenceScore>({
        field: "initialStopLoss",
        headerName: "SL",
        width: 150,
        getValue: (r) => formatSl(r),
      }),
      columnFactories.createTextColumn<TradeConfidenceScore>({
        field: "targetT1",
        headerName: "T1",
        width: 150,
        getValue: (r) => formatTarget(r, r.targetT1),
      }),
      columnFactories.createTextColumn<TradeConfidenceScore>({
        field: "targetT2",
        headerName: "T2",
        width: 150,
        getValue: (r) => formatTarget(r, r.targetT2),
      }),
      columnFactories.createTextColumn<TradeConfidenceScore>({
        field: "targetT3",
        headerName: "T3",
        width: 150,
        getValue: (r) => formatTarget(r, r.targetT3),
      }),
      columnFactories.createTextColumn<TradeConfidenceScore>({
        field: "rr",
        headerName: "R:R",
        width: 70,
        getValue: (r) => {
          const rr = riskReward(r);
          return rr != null ? rr.toFixed(2) : "";
        },
      }),
      columnFactories.createTextColumn<TradeConfidenceScore>({
        field: "layers",
        headerName: "Layers",
        width: 200,
        getValue: (r) =>
          `S${r.signalsScore} L${r.liquidityScore} B${r.breakoutScore} F${r.futuresScore} O${r.optionsScore}`,
      }),
    ];

    if (tab === "history") {
      cols.push(
        columnFactories.createTextColumn<TradeConfidenceScore>({
          field: "disappearedAt",
          headerName: "Left",
          width: 90,
          getValue: (r) =>
            formatIstTime((r as SignalDayEntry<TradeConfidenceScore>).disappearedAt),
        }),
      );
    }
    if (tab === "traded") {
      cols.push(
        columnFactories.createTextColumn<TradeConfidenceScore>({
          field: "tradedAt",
          headerName: "Traded",
          width: 90,
          getValue: (r) =>
            formatIstTime((r as SignalDayEntry<TradeConfidenceScore>).tradedAt),
        }),
      );
    }
    if (tab !== "traded") {
      cols.push(
        columnFactories.createActionColumn<TradeConfidenceScore>(
          (row) => [
            {
              icon: <Handshake size={DEFAULT_SMALL_ICON_SIZE} />,
              tooltip: isSignalDayTraded(HISTORY_SCOPE, row)
                ? "Already traded"
                : "Trade — open in Positions",
              disabled: () => isSignalDayTraded(HISTORY_SCOPE, row),
              onClick: (r) => void onTrade(r),
            },
          ],
          { field: "actions", headerName: "Trade", width: 80 },
        ),
      );
    }
    return cols;
  }, [hitRates, tab]);

  const emptyMessage =
    tab === "history"
      ? "No scores have left the list today. History keeps the frozen levels from first sighting."
      : tab === "traded"
        ? "No traded scores today. Use Trade on Active or History."
        : "No trade scores yet. Run trade score (refreshes Signals + Liquidity Fresh, then scores).";

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
      <Alert severity="info" sx={{ mb: 2 }}>
        Separate high-probability engine. Primary <strong>Signals</strong> (40%) +{" "}
        <strong>Liquidity Fresh</strong> (20%) + <strong>Quality Breakout</strong> (20%).
        F&amp;O layers (20%) — Phase 3–4. SL = tighter stop, entries within 0.2%.
        Existing Signals / Liquidity pages are unchanged.
      </Alert>
      {running ? <LinearProgress sx={{ mb: 2 }} /> : null}
      {tab === "active" && filteredActive.length > 0 ? (
        <Box sx={{ mb: 2 }}>
          <Typography variant="subtitle2" sx={{ mb: 1 }}>
            Top pick
          </Typography>
          <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
            {filteredActive.slice(0, 3).map((r) => (
              <Chip
                key={r.id}
                label={`${r.appSymbol} ${r.side.toUpperCase()} · ${r.confidenceScore}% · ${ratingLabel(r.rating)}`}
                color={r.confidenceScore >= 90 ? "success" : "default"}
                variant="outlined"
              />
            ))}
          </Stack>
          {filteredActive[0]?.reasons?.length ? (
            <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
              {filteredActive[0].reasons.map((x) => `✓ ${x}`).join(" · ")}
            </Typography>
          ) : null}
        </Box>
      ) : null}
      <Tabs
        value={tab}
        onChange={(_, v: SignalsTab) => {
          setTab(v);
          setSelectedTradedIds([]);
        }}
        sx={{ mb: 1.5, minHeight: 40 }}
      >
        <Tab value="active" label={`Active (${filteredActive.length})`} />
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
        searchPlaceholder="Search symbol…"
        emptyMessage={emptyMessage}
        enableSelection={tab === "traded"}
        selectedRowIds={tab === "traded" ? selectedTradedIds : undefined}
        onSelectedRowIdsChange={tab === "traded" ? setSelectedTradedIds : undefined}
      />
    </>
  );
}
