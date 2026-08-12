import { useEffect, useMemo, useState } from "react";
import { Alert, Button, FormControlLabel, Switch, Stack as MuiStack } from "@mui/material";
import { Play, ArrowSquareOut, FilePdf, FileXls } from "@phosphor-icons/react";
import { ActionFactory, DataFactory } from "../api/factories";
import type { Signal } from "../api/types";
import { columnFactories } from "../zen_components/table/columnFactories";
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

export default function SignalsPage() {
  const { setTitle, setBreadcrumbs, setPageActions, setIsSyncing } =
    useZenPrimaryLayoutContext();
  const [rows, setRows] = useState<Signal[]>([]);
  const [hitRates, setHitRates] = useState<HitRateByInstrument>(() => new Map());
  const [loading, setLoading] = useState(true);
  const [running, setRunning] = useState(false);
  const [sectorCheck, setSectorCheck] = useState(false);
  const [riskRewardCheck, setRiskRewardCheck] = useState(false);
  const [freshCrossCheck, setFreshCrossCheck] = useState(false);
  const [hideLaggingRs, setHideLaggingRs] = useState(false);
  const [error, setError] = useState<string | null>(null);

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

  async function onOpen(signalId: string) {
    try {
      await ActionFactory.openPositionFromSignal(signalId);
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
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
  const visibleRows = useMemo(() => {
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
      rows: visibleRows,
    });
  }

  function onExportExcel() {
    downloadExcelTable({
      sheetName: "Signals",
      fileName: exportStamp("signals", "xlsx"),
      columns: signalExportColumns,
      rows: visibleRows,
    });
  }

  useEffect(() => {
    const exportDisabled = loading || visibleRows.length === 0;
    setPageActions(
      <MuiStack direction="row" spacing={1} alignItems="center" flexWrap="wrap">
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
  }, [running, loading, sectorCheck, riskRewardCheck, freshCrossCheck, hideLaggingRs, visibleRows]);

  const columns = useMemo(() => {
    return [
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
      columnFactories.createActionColumn<Signal>(
        () => [
          {
            icon: <ArrowSquareOut size={DEFAULT_SMALL_ICON_SIZE} />,
            tooltip: "Open position",
            onClick: (r) => void onOpen(r.id),
          },
        ],
        { field: "actions", headerName: "", width: 72 },
      ),
    ];
  }, [hitRates]);

  return (
    <>
      {error ? (
        <Alert severity="error" sx={{ mb: 2 }}>
          {error}
        </Alert>
      ) : null}
      <ZenTable
        columns={columns}
        rows={visibleRows}
        getRowId={(r) => r.id}
        loading={loading}
        enableSearch
        searchPlaceholder="Search symbol or name…"
        emptyMessage={
          sectorCheck || riskRewardCheck || freshCrossCheck
            ? "No signals match the active filters. Turn filters off, or Run analysis again."
            : "No actionable signals (approaching entry, open T1). Click Run analysis."
        }
      />
    </>
  );
}
