import { useEffect, useMemo, useState } from "react";
import { Alert, Button, FormControlLabel, Stack, Switch, Tab, Tabs } from "@mui/material";
import { Handshake, Play } from "@phosphor-icons/react";
import { columnFactories } from "../../zen_components/table/columnFactories";
import type { ColumnConfig } from "../../zen_components/table/columnTypes";
import ZenTable from "../../zen_components/table/ZenTable";
import { useZenPrimaryLayoutContext } from "../../zen_components/layout/ZenPrimaryLayoutProvider";
import { DEFAULT_SMALL_ICON_SIZE } from "../../constants";
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
import { ConfluenceApi } from "./api";
import type { ConfluenceSignal } from "./types";

const HISTORY_SCOPE = "confluence";

function riskReward(row: ConfluenceSignal): number | null {
  const entry = Number(row.entryPrice);
  const sl = Number(row.initialStopLoss);
  const t1 = Number(row.targetT1);
  if (![entry, sl, t1].every(Number.isFinite)) return null;
  const risk = row.side === "sell" ? sl - entry : entry - sl;
  const reward = row.side === "sell" ? entry - t1 : t1 - entry;
  if (risk <= 0 || reward <= 0) return null;
  return reward / risk;
}

export default function ConfluencePage() {
  const { setTitle, setBreadcrumbs, setPageActions, setIsSyncing } =
    useZenPrimaryLayoutContext();
  const [rows, setRows] = useState<ConfluenceSignal[]>([]);
  const [historyRows, setHistoryRows] = useState<SignalDayEntry<ConfluenceSignal>[]>([]);
  const [tradedRows, setTradedRows] = useState<SignalDayEntry<ConfluenceSignal>[]>([]);
  const [tab, setTab] = useState<SignalsTab>("active");
  const [selectedTradedIds, setSelectedTradedIds] = useState<string[]>([]);
  const [hitRates, setHitRates] = useState<HitRateByInstrument>(() => new Map());
  const [loading, setLoading] = useState(true);
  const [running, setRunning] = useState(false);
  const [sectorCheck, setSectorCheck] = useState(false);
  const [hideLaggingRs, setHideLaggingRs] = useState(false);
  const [rrCheck, setRrCheck] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);

  async function refresh() {
    setError(null);
    setIsSyncing(true);
    try {
      const [signals, rates] = await Promise.all([
        ConfluenceApi.fetchSignals(),
        loadHistoricalHitRates("confluence"),
      ]);
      setRows(signals);
      setHitRates(rates);
      const synced = syncSignalDayHistory(HISTORY_SCOPE, signals);
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
    try {
      await ConfluenceApi.runBothAnalyses();
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setRunning(false);
    }
  }

  async function onTrade(row: ConfluenceSignal) {
    setError(null);
    setInfo(null);
    try {
      await ConfluenceApi.openPosition(row);
      markSignalDayTraded(HISTORY_SCOPE, row);
      setInfo(`${row.appSymbol} moved to Positions (Traded).`);
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  }

  function tradedRowId(r: ConfluenceSignal) {
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
    let list = [...rows];
    if (sectorCheck) list = list.filter((r) => r.sectorConfirmed);
    if (hideLaggingRs) list = list.filter((r) => !r.sectorRs?.downranked);
    if (rrCheck) list = list.filter((r) => (riskReward(r) ?? 0) >= 1);
    return list;
  }, [rows, sectorCheck, hideLaggingRs, rrCheck]);

  const tableRows = useMemo(() => {
    if (tab === "history") return historyRows;
    if (tab === "traded") return tradedRows;
    return filteredActive;
  }, [tab, filteredActive, historyRows, tradedRows]);

  useEffect(() => {
    setTitle("Confluence");
    setBreadcrumbs([{ label: "Home" }, { label: "Confluence" }]);
    void refresh();
    return () => setPageActions(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    setPageActions(
      <Stack direction="row" spacing={1} alignItems="center">
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
              label="Sector"
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
            <FormControlLabel
              control={
                <Switch
                  size="small"
                  checked={rrCheck}
                  onChange={(e) => setRrCheck(e.target.checked)}
                />
              }
              label="R:R ≥ 1"
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
          {running ? "Running…" : "Run Signals + Liq V2"}
        </Button>
      </Stack>,
    );
  }, [running, sectorCheck, hideLaggingRs, rrCheck, tab, setPageActions]);

  const columns = useMemo(() => {
    const cols: ColumnConfig<ConfluenceSignal>[] = [
      columnFactories.createTextColumn<ConfluenceSignal>({
        field: "appSymbol",
        headerName: "Symbol",
        width: 110,
        getValue: (r) => r.appSymbol,
      }),
      createSectorRsColumn<ConfluenceSignal>((r) => r.sectorRs),
      createHistoricalHitRateColumn<ConfluenceSignal>(hitRates, (r) => r.instrumentId),
      columnFactories.createStatusColumn<ConfluenceSignal>(
        { buy: { label: "BUY", color: "#2e7d32" }, sell: { label: "SELL", color: "#c62828" } },
        { field: "side", headerName: "Side", width: 90, getValue: (r) => r.side },
      ),
      columnFactories.createNumberColumn<ConfluenceSignal>({
        field: "entryPrice",
        headerName: "Entry",
        width: 100,
        minDecimalPlaces: 2,
        getValue: (r) => r.entryPrice,
      }),
      columnFactories.createNumberColumn<ConfluenceSignal>({
        field: "initialStopLoss",
        headerName: "SL (tighter)",
        width: 110,
        minDecimalPlaces: 2,
        getValue: (r) => r.initialStopLoss,
      }),
      columnFactories.createNumberColumn<ConfluenceSignal>({
        field: "signalsStopLoss",
        headerName: "Sig SL",
        width: 90,
        minDecimalPlaces: 2,
        getValue: (r) => r.signalsStopLoss,
      }),
      columnFactories.createNumberColumn<ConfluenceSignal>({
        field: "liquidityStopLoss",
        headerName: "Liq SL",
        width: 90,
        minDecimalPlaces: 2,
        getValue: (r) => r.liquidityStopLoss,
      }),
      columnFactories.createNumberColumn<ConfluenceSignal>({
        field: "targetT1",
        headerName: "T1",
        width: 90,
        minDecimalPlaces: 2,
        getValue: (r) => r.targetT1 ?? null,
      }),
    ];

    if (tab === "history") {
      cols.push(
        columnFactories.createTextColumn<ConfluenceSignal>({
          field: "disappearedAt",
          headerName: "Left",
          width: 90,
          getValue: (r) =>
            formatIstTime((r as SignalDayEntry<ConfluenceSignal>).disappearedAt),
        }),
      );
    }
    if (tab === "traded") {
      cols.push(
        columnFactories.createTextColumn<ConfluenceSignal>({
          field: "tradedAt",
          headerName: "Traded",
          width: 90,
          getValue: (r) =>
            formatIstTime((r as SignalDayEntry<ConfluenceSignal>).tradedAt),
        }),
      );
    }
    if (tab !== "traded") {
      cols.push(
        columnFactories.createActionColumn<ConfluenceSignal>(
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
      ? "No confluence setups have left today. History keeps frozen levels from first sighting."
      : tab === "traded"
        ? "No traded confluence setups today. Use Trade on Active or History."
        : "No overlap. Run Signals + Liquidity V2 first.";

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
        <strong>Confluence</strong> = Signals + Liquidity V2 overlap only. SL = tighter stop
        (0.2% entry tolerance). Separate from Breakout and Trade Score.
      </Alert>
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
        emptyMessage={emptyMessage}
        enableSelection={tab === "traded"}
        selectedRowIds={tab === "traded" ? selectedTradedIds : undefined}
        onSelectedRowIdsChange={tab === "traded" ? setSelectedTradedIds : undefined}
      />
    </>
  );
}
