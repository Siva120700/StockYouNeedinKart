import { useEffect, useMemo, useState } from "react";
import {
  Alert,
  Button,
  FormControlLabel,
  Switch,
  Stack as MuiStack,
  Tab,
  Tabs,
} from "@mui/material";
import { Play, Handshake, FilePdf, FileXls } from "@phosphor-icons/react";
import { ActionFactory, DataFactory } from "../api/factories";
import type { Signal } from "../api/types";
import { columnFactories } from "../zen_components/table/columnFactories";
import type { ColumnConfig } from "../zen_components/table/columnTypes";
import ZenTable from "../zen_components/table/ZenTable";
import { useZenPrimaryLayoutContext } from "../zen_components/layout/ZenPrimaryLayoutProvider";
import PageFrame, { TablePane } from "../zen_components/layout/PageFrame";
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
  type SignalsTab,
} from "../utils/signalDayHistory";
import TradedDeleteBar from "../zen_components/shared/TradedDeleteBar";

const HISTORY_SCOPE = "signals";

function formatTarget(row: Signal, target: number | null | undefined) {
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

function formatSl(row: Signal) {
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

export default function SignalsPage() {
  const { setTitle, setBreadcrumbs, setPageActions, setIsSyncing } =
    useZenPrimaryLayoutContext();
  const [rows, setRows] = useState<Signal[]>([]);
  const [historyRows, setHistoryRows] = useState<SignalDayEntry<Signal>[]>([]);
  const [tradedRows, setTradedRows] = useState<SignalDayEntry<Signal>[]>([]);
  const [tab, setTab] = useState<SignalsTab>("active");
  const [selectedTradedIds, setSelectedTradedIds] = useState<string[]>([]);
  const [hitRates, setHitRates] = useState<HitRateByInstrument>(() => new Map());
  const [loading, setLoading] = useState(true);
  const [running, setRunning] = useState(false);
  const [sectorCheck, setSectorCheck] = useState(false);
  const [riskRewardCheck, setRiskRewardCheck] = useState(false);
  const [freshCrossCheck, setFreshCrossCheck] = useState(false);
  const [hideLaggingRs, setHideLaggingRs] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);

  const signalExportColumns: ExportColumn<Signal>[] = useMemo(
    () => [
      { header: "Symbol", value: (r) => r.appSymbol },
      {
        header: "Hit %",
        value: (r) => formatHitRatePct(hitRates.get(r.instrumentId)),
      },
      { header: "Side", value: (r) => (r.side === "sell" ? "SELL" : "BUY") },
      { header: "Entry", value: (r) => r.entryPrice },
      { header: "SL", value: (r) => formatSl(r) },
      { header: "T1", value: (r) => formatTarget(r, r.targetT1) },
      { header: "T2", value: (r) => formatTarget(r, r.targetT2) },
      { header: "T3", value: (r) => formatTarget(r, r.targetT3) },
      { header: "Vol OK", value: (r) => (r.volumeOk ? "Yes" : "No") },
    ],
    [hitRates],
  );

  async function refresh() {
    setError(null);
    setIsSyncing(true);
    try {
      const [signals, rates] = await Promise.all([
        DataFactory.signals(),
        loadHistoricalHitRates("signals"),
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
    setError(null);
    try {
      await ActionFactory.runAnalysis();
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setRunning(false);
    }
  }

  async function onTrade(row: Signal) {
    setError(null);
    setInfo(null);
    try {
      await ActionFactory.openPositionFromSignal(row.id);
      markSignalDayTraded(HISTORY_SCOPE, row);
      setInfo(`${row.appSymbol} moved to Positions (Traded).`);
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  }

  function tradedRowId(r: Signal) {
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

  /** Risk-reward vs T1: reward/risk. Buy (T1-entry)/(entry-SL); sell (entry-T1)/(SL-entry). */
  function riskRewardRatio(row: Signal): number | null {
    const entry = Number(row.entryPrice);
    const sl = Number(row.initialStopLoss);
    const target = Number(row.targetT1 ?? row.targetT2 ?? row.targetT3);
    if (![entry, sl, target].every((n) => Number.isFinite(n)) || entry === 0) return null;
    const risk = row.side === "sell" ? sl - entry : entry - sl;
    const reward = row.side === "sell" ? entry - target : target - entry;
    if (risk <= 0 || reward <= 0) return null;
    return reward / risk;
  }

  // Toggles filter already-loaded rows — no backend call.
  const filteredActive = useMemo(() => {
    let list = rows;
    if (sectorCheck) list = list.filter((r) => r.sectorConfirmed);
    if (freshCrossCheck) list = list.filter((r) => r.freshCross);
    if (hideLaggingRs) list = list.filter((r) => !r.sectorRs?.downranked);
    if (riskRewardCheck) {
      list = list.filter((r) => {
        const rr = riskRewardRatio(r);
        return rr != null && rr >= 1;
      });
    }
    return list;
  }, [rows, sectorCheck, riskRewardCheck, freshCrossCheck, hideLaggingRs]);

  const tableRows: Signal[] = useMemo(() => {
    if (tab === "history") return historyRows;
    if (tab === "traded") return tradedRows;
    return filteredActive;
  }, [tab, filteredActive, historyRows, tradedRows]);

  useEffect(() => {
    setTitle("Signals");
    setBreadcrumbs([{ label: "Home" }, { label: "Signals" }]);
    void refresh();
    return () => setPageActions(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function onExportPdf() {
    downloadPdfTable({
      title: "Breakout Signals",
      fileName: exportStamp("signals", "pdf"),
      columns: signalExportColumns,
      rows: tableRows,
    });
  }

  function onExportExcel() {
    downloadExcelTable({
      sheetName: "Signals",
      fileName: exportStamp("signals", "xlsx"),
      columns: signalExportColumns,
      rows: tableRows,
    });
  }

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
                  checked={freshCrossCheck}
                  onChange={(e) => setFreshCrossCheck(e.target.checked)}
                />
              }
              label="Fresh cross"
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
          {running ? "Running…" : "Run analysis"}
        </Button>
      </MuiStack>,
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [
    running,
    loading,
    sectorCheck,
    riskRewardCheck,
    freshCrossCheck,
    hideLaggingRs,
    tableRows,
    tab,
  ]);

  const columns = useMemo(() => {
    const base: ColumnConfig<Signal>[] = [
      columnFactories.createTextColumn<Signal>({
        field: "appSymbol",
        headerName: "Symbol",
        width: 120,
        getValue: (r) => r.appSymbol,
      }),
      createSectorRsColumn<Signal>((r) => r.sectorRs),
      createHistoricalHitRateColumn<Signal>(hitRates, (r) => r.instrumentId),
      columnFactories.createStatusColumn<Signal>(
        {
          buy: { label: "BUY", color: "#2e7d32" },
          sell: { label: "SELL", color: "#c62828" },
        },
        {
          field: "side",
          headerName: "Side",
          width: 100,
          getValue: (r) => r.side,
        },
      ),
      columnFactories.createNumberColumn<Signal>({
        field: "entryPrice",
        headerName: "Entry",
        width: 110,
        minDecimalPlaces: 2,
        getValue: (r) => r.entryPrice,
      }),
      columnFactories.createTextColumn<Signal>({
        field: "initialStopLoss",
        headerName: "SL",
        width: 150,
        getValue: (r) => formatSl(r),
      }),
      columnFactories.createTextColumn<Signal>({
        field: "targetT1",
        headerName: "T1",
        width: 150,
        getValue: (r) => formatTarget(r, r.targetT1),
      }),
      columnFactories.createTextColumn<Signal>({
        field: "targetT2",
        headerName: "T2",
        width: 150,
        getValue: (r) => formatTarget(r, r.targetT2),
      }),
      columnFactories.createTextColumn<Signal>({
        field: "targetT3",
        headerName: "T3",
        width: 150,
        getValue: (r) => formatTarget(r, r.targetT3),
      }),
      columnFactories.createBooleanColumn<Signal>({
        field: "volumeOk",
        headerName: "Vol OK",
        width: 90,
        getValue: (r) => r.volumeOk,
      }),
    ];

    if (tab === "history") {
      base.push(
        columnFactories.createTextColumn<Signal>({
          field: "disappearedAt",
          headerName: "Left",
          width: 90,
          getValue: (r) =>
            formatTime((r as SignalDayEntry<Signal>).disappearedAt),
        }),
      );
    }

    if (tab === "traded") {
      base.push(
        columnFactories.createTextColumn<Signal>({
          field: "tradedAt",
          headerName: "Traded",
          width: 90,
          getValue: (r) => formatTime((r as SignalDayEntry<Signal>).tradedAt),
        }),
      );
    }

    if (tab !== "traded") {
      base.push(
        columnFactories.createActionColumn<Signal>(
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

    return base;
  }, [hitRates, tab]);

  const emptyMessage =
    tab === "history"
      ? "No signals have left the list today. History keeps the frozen entry/SL/targets from when each name first appeared."
      : tab === "traded"
        ? "No traded signals today. Use Trade on Active or History to open a position."
        : sectorCheck || riskRewardCheck || freshCrossCheck
          ? "No signals match the active filters. Turn filters off, or Run analysis again."
          : "No actionable signals (approaching entry, open T1). Click Run analysis.";

  return (
    <PageFrame>
      {error ? (
        <Alert severity="error">
          {error}
        </Alert>
      ) : null}
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
      <TablePane>
        <ZenTable
          fillHeight
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
      </TablePane>
    </PageFrame>
  );
}
