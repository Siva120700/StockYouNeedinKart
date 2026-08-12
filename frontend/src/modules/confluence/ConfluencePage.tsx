import { useEffect, useMemo, useState } from "react";
import { Alert, Button, FormControlLabel, Stack, Switch } from "@mui/material";
import { ArrowSquareOut, Play } from "@phosphor-icons/react";
import { columnFactories } from "../../zen_components/table/columnFactories";
import ZenTable from "../../zen_components/table/ZenTable";
import { useZenPrimaryLayoutContext } from "../../zen_components/layout/ZenPrimaryLayoutProvider";
import { DEFAULT_SMALL_ICON_SIZE } from "../../constants";
import {
  createHistoricalHitRateColumn,
  loadHistoricalHitRates,
  type HitRateByInstrument,
} from "../../utils/historicalHitRate";
import { createSectorRsColumn } from "../../utils/sectorRelativeStrength.tsx";
import { ConfluenceApi } from "./api";
import type { ConfluenceSignal } from "./types";

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
  const [hitRates, setHitRates] = useState<HitRateByInstrument>(() => new Map());
  const [loading, setLoading] = useState(true);
  const [running, setRunning] = useState(false);
  const [sectorCheck, setSectorCheck] = useState(false);
  const [hideLaggingRs, setHideLaggingRs] = useState(false);
  const [rrCheck, setRrCheck] = useState(true);
  const [error, setError] = useState<string | null>(null);

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

  const visible = useMemo(() => {
    let list = [...rows];
    if (sectorCheck) list = list.filter((r) => r.sectorConfirmed);
    if (hideLaggingRs) list = list.filter((r) => !r.sectorRs?.downranked);
    if (rrCheck) list = list.filter((r) => (riskReward(r) ?? 0) >= 1);
    return list;
  }, [rows, sectorCheck, hideLaggingRs, rrCheck]);

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
        <FormControlLabel
          control={<Switch size="small" checked={sectorCheck} onChange={(e) => setSectorCheck(e.target.checked)} />}
          label="Sector"
        />
        <FormControlLabel
          control={<Switch size="small" checked={hideLaggingRs} onChange={(e) => setHideLaggingRs(e.target.checked)} />}
          label="Hide lagging RS"
        />
        <FormControlLabel
          control={<Switch size="small" checked={rrCheck} onChange={(e) => setRrCheck(e.target.checked)} />}
          label="R:R ≥ 1"
        />
        <Button variant="contained" size="small" disabled={running} startIcon={<Play size={DEFAULT_SMALL_ICON_SIZE} />}
          onClick={() => void onRun()}>
          {running ? "Running…" : "Run Signals + Liq V2"}
        </Button>
      </Stack>,
    );
  }, [running, sectorCheck, hideLaggingRs, rrCheck, setPageActions]);

  const columns = useMemo(
    () => [
      columnFactories.createTextColumn<ConfluenceSignal>({ field: "appSymbol", headerName: "Symbol", width: 110, getValue: (r) => r.appSymbol }),
      createSectorRsColumn<ConfluenceSignal>((r) => r.sectorRs),
      createHistoricalHitRateColumn<ConfluenceSignal>(hitRates, (r) => r.instrumentId),
      columnFactories.createStatusColumn<ConfluenceSignal>(
        { buy: { label: "BUY", color: "#2e7d32" }, sell: { label: "SELL", color: "#c62828" } },
        { field: "side", headerName: "Side", width: 90, getValue: (r) => r.side },
      ),
      columnFactories.createNumberColumn<ConfluenceSignal>({ field: "entryPrice", headerName: "Entry", width: 100, minDecimalPlaces: 2, getValue: (r) => r.entryPrice }),
      columnFactories.createNumberColumn<ConfluenceSignal>({ field: "initialStopLoss", headerName: "SL (tighter)", width: 110, minDecimalPlaces: 2, getValue: (r) => r.initialStopLoss }),
      columnFactories.createNumberColumn<ConfluenceSignal>({ field: "signalsStopLoss", headerName: "Sig SL", width: 90, minDecimalPlaces: 2, getValue: (r) => r.signalsStopLoss }),
      columnFactories.createNumberColumn<ConfluenceSignal>({ field: "liquidityStopLoss", headerName: "Liq SL", width: 90, minDecimalPlaces: 2, getValue: (r) => r.liquidityStopLoss }),
      columnFactories.createNumberColumn<ConfluenceSignal>({ field: "targetT1", headerName: "T1", width: 90, minDecimalPlaces: 2, getValue: (r) => r.targetT1 ?? null }),
      columnFactories.createActionColumn<ConfluenceSignal>(
        () => [{ icon: <ArrowSquareOut size={DEFAULT_SMALL_ICON_SIZE} />, tooltip: "Open", onClick: (r) => void ConfluenceApi.openPosition(r) }],
        { field: "actions", headerName: "", width: 72 },
      ),
    ],
    [hitRates],
  );

  return (
    <>
      {error ? <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert> : null}
      <Alert severity="info" sx={{ mb: 2 }}>
        <strong>Confluence</strong> = Signals + Liquidity V2 overlap only. SL = tighter stop (0.2% entry tolerance).
        Separate from Breakout and Trade Score.
      </Alert>
      <ZenTable columns={columns} rows={visible} getRowId={(r) => r.id} loading={loading} enableSearch
        emptyMessage="No overlap. Run Signals + Liquidity V2 first." />
    </>
  );
}
