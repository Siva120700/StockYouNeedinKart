import { useEffect, useMemo, useState } from "react";
import { Alert, Button } from "@mui/material";
import { Play } from "@phosphor-icons/react";
import { ActionFactory, DataFactory } from "../api/factories";
import type { SpikeScanRow } from "../api/types";
import { columnFactories } from "../zen_components/table/columnFactories";
import ZenTable from "../zen_components/table/ZenTable";
import {
  useZenPrimaryLayoutContext,
} from "../zen_components/layout/ZenPrimaryLayoutProvider";
import PageFrame, { TablePane } from "../zen_components/layout/PageFrame";
import { DEFAULT_SMALL_ICON_SIZE } from "../constants";

function fmtIstTime(iso: string | null | undefined): string {
  if (!iso) return "";
  try {
    return new Date(iso).toLocaleString("en-IN", {
      timeZone: "Asia/Kolkata",
      hour: "2-digit",
      minute: "2-digit",
      day: "2-digit",
      month: "short",
    });
  } catch {
    return "";
  }
}

function fmtPct(n: number): string {
  const sign = n > 0 ? "+" : "";
  return `${sign}${n.toFixed(2)}%`;
}

export default function SpikeScanPage() {
  const { setTitle, setBreadcrumbs, setPageActions, setIsSyncing } =
    useZenPrimaryLayoutContext();
  const [rows, setRows] = useState<SpikeScanRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [running, setRunning] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);

  async function loadCached() {
    setError(null);
    setIsSyncing(true);
    try {
      setRows(await DataFactory.spikeScan());
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setLoading(false);
      setIsSyncing(false);
    }
  }

  async function runScan() {
    setError(null);
    setInfo(null);
    setRunning(true);
    setIsSyncing(true);
    try {
      const next = await ActionFactory.runSpikeScan();
      setRows(next);
      setInfo(
        next.length === 0
          ? "No 15-min spikes right now (need ≥0.50% body or ≥0.70% range, RVOL ≥ 1.8, directional candle)."
          : `${next.length} sudden 15-min spike${next.length === 1 ? "" : "s"}.`,
      );
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setRunning(false);
      setLoading(false);
      setIsSyncing(false);
    }
  }

  useEffect(() => {
    setTitle("15m Spike");
    setBreadcrumbs([{ label: "Home" }, { label: "15m Spike" }]);
    void loadCached();
    return () => {
      setPageActions(null);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    setPageActions(
      <Button
        variant="contained"
        size="small"
        disabled={running}
        startIcon={<Play size={DEFAULT_SMALL_ICON_SIZE} />}
        onClick={() => void runScan()}
      >
        {running ? "Scanning 15m…" : "Run"}
      </Button>,
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [running]);

  const columns = useMemo(
    () => [
      columnFactories.createTextColumn<SpikeScanRow>({
        field: "appSymbol",
        headerName: "Symbol",
        width: 110,
        getValue: (r) => r.appSymbol,
      }),
      columnFactories.createTextColumn<SpikeScanRow>({
        field: "side",
        headerName: "Side",
        width: 70,
        getValue: (r) => (r.side === "sell" ? "SELL" : "BUY"),
      }),
      columnFactories.createTextColumn<SpikeScanRow>({
        field: "barTime",
        headerName: "15m bar",
        width: 120,
        getValue: (r) => `${fmtIstTime(r.barTime)}${r.forming ? " •" : ""}`,
      }),
      columnFactories.createTextColumn<SpikeScanRow>({
        field: "changePct",
        headerName: "Body %",
        width: 90,
        getValue: (r) => fmtPct(r.changePct),
      }),
      columnFactories.createNumberColumn<SpikeScanRow>({
        field: "rangePct",
        headerName: "Range %",
        width: 90,
        minDecimalPlaces: 2,
        getValue: (r) => r.rangePct,
      }),
      columnFactories.createNumberColumn<SpikeScanRow>({
        field: "relativeVolume",
        headerName: "RVOL",
        width: 80,
        minDecimalPlaces: 2,
        getValue: (r) => r.relativeVolume,
      }),
      columnFactories.createNumberColumn<SpikeScanRow>({
        field: "spikeScore",
        headerName: "Score",
        width: 80,
        minDecimalPlaces: 2,
        getValue: (r) => r.spikeScore,
      }),
      columnFactories.createNumberColumn<SpikeScanRow>({
        field: "entryPrice",
        headerName: "Entry",
        width: 100,
        minDecimalPlaces: 2,
        getValue: (r) => r.entryPrice,
      }),
      columnFactories.createNumberColumn<SpikeScanRow>({
        field: "initialStopLoss",
        headerName: "SL",
        width: 100,
        minDecimalPlaces: 2,
        getValue: (r) => r.initialStopLoss,
      }),
      columnFactories.createNumberColumn<SpikeScanRow>({
        field: "targetT1",
        headerName: "T1",
        width: 100,
        minDecimalPlaces: 2,
        getValue: (r) => r.targetT1,
      }),
      columnFactories.createNumberColumn<SpikeScanRow>({
        field: "volume",
        headerName: "Vol",
        width: 100,
        getValue: (r) => r.volume,
      }),
    ],
    [],
  );

  return (
    <PageFrame>
      <Alert severity="info">
        Sudden 15-minute spikes on F&amp;O names: last candle body ≥ 0.50% or range ≥ 0.70%,
        volume at least 1.8× the prior 20 bars, and a directional body (not a doji). First Run
        pulls 15-min history (a few minutes); later Runs only refresh stale bars. A trailing •
        on the bar time means the candle is still forming.
      </Alert>
      {error ? <Alert severity="error">{error}</Alert> : null}
      {info ? (
        <Alert severity="success" onClose={() => setInfo(null)}>
          {info}
        </Alert>
      ) : null}
      <TablePane>
        <ZenTable
          fillHeight
          columns={columns}
          rows={rows}
          getRowId={(r) => `${r.instrumentId}:${r.barTime}`}
          loading={loading || running}
          enableSearch
          searchPlaceholder="Search symbol…"
          emptyMessage={
            running
              ? "Fetching 15-min candles…"
              : "No spikes yet — press Run during market hours."
          }
        />
      </TablePane>
    </PageFrame>
  );
}
