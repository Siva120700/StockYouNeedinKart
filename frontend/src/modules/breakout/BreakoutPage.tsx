import { useEffect, useMemo, useState } from "react";
import { Alert, Button, FormControlLabel, Stack, Switch } from "@mui/material";
import { Play } from "@phosphor-icons/react";
import { columnFactories } from "../../zen_components/table/columnFactories";
import ZenTable from "../../zen_components/table/ZenTable";
import { useZenPrimaryLayoutContext } from "../../zen_components/layout/ZenPrimaryLayoutProvider";
import PageFrame, { TablePane } from "../../zen_components/layout/PageFrame";
import { DEFAULT_SMALL_ICON_SIZE } from "../../constants";
import {
  createHistoricalHitRateColumn,
  loadHistoricalHitRates,
  type HitRateByInstrument,
} from "../../utils/historicalHitRate";
import { createSectorRsColumn } from "../../utils/sectorRelativeStrength.tsx";
import { BreakoutApi } from "./api";
import type { BreakoutConfirmation } from "./types";
import { patternLabel } from "./types";

export default function BreakoutPage() {
  const { setTitle, setBreadcrumbs, setPageActions, setIsSyncing } =
    useZenPrimaryLayoutContext();
  const [rows, setRows] = useState<BreakoutConfirmation[]>([]);
  const [hitRates, setHitRates] = useState<HitRateByInstrument>(() => new Map());
  const [loading, setLoading] = useState(true);
  const [running, setRunning] = useState(false);
  const [confirmedOnly, setConfirmedOnly] = useState(true);
  const [hideLaggingRs, setHideLaggingRs] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function refresh() {
    setError(null);
    setIsSyncing(true);
    try {
      const [confirmations, rates] = await Promise.all([
        BreakoutApi.fetchConfirmations(false),
        loadHistoricalHitRates("breakout"),
      ]);
      setRows(confirmations);
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
      await BreakoutApi.runAnalysis();
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setRunning(false);
    }
  }

  const visible = useMemo(() => {
    let list = confirmedOnly ? rows.filter((r) => r.confirmed) : rows;
    if (hideLaggingRs) list = list.filter((r) => !r.sectorRs?.downranked);
    return list;
  }, [rows, confirmedOnly, hideLaggingRs]);

  useEffect(() => {
    setTitle("Breakout");
    setBreadcrumbs([{ label: "Home" }, { label: "Breakout" }]);
    void refresh();
    return () => setPageActions(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    setPageActions(
      <Stack direction="row" spacing={1} alignItems="center">
        <FormControlLabel
          control={<Switch size="small" checked={confirmedOnly} onChange={(e) => setConfirmedOnly(e.target.checked)} />}
          label="Confirmed only"
        />
        <FormControlLabel
          control={<Switch size="small" checked={hideLaggingRs} onChange={(e) => setHideLaggingRs(e.target.checked)} />}
          label="Hide lagging RS"
        />
        <Button variant="contained" size="small" disabled={running} startIcon={<Play size={DEFAULT_SMALL_ICON_SIZE} />}
          onClick={() => void onRun()}>
          {running ? "Scanning…" : "Run breakout scan"}
        </Button>
      </Stack>,
    );
  }, [running, confirmedOnly, hideLaggingRs, setPageActions]);

  const columns = useMemo(
    () => [
      columnFactories.createTextColumn<BreakoutConfirmation>({ field: "appSymbol", headerName: "Symbol", width: 110, getValue: (r) => r.appSymbol }),
      createSectorRsColumn<BreakoutConfirmation>((r) => r.sectorRs),
      createHistoricalHitRateColumn<BreakoutConfirmation>(hitRates, (r) => r.instrumentId),
      columnFactories.createStatusColumn<BreakoutConfirmation>(
        { buy: { label: "BUY", color: "#2e7d32" }, sell: { label: "SELL", color: "#c62828" } },
        { field: "side", headerName: "Side", width: 90, getValue: (r) => r.side },
      ),
      columnFactories.createTextColumn<BreakoutConfirmation>({
        field: "patternType",
        headerName: "Pattern",
        width: 150,
        getValue: (r) => patternLabel(r.patternType),
      }),
      columnFactories.createBooleanColumn<BreakoutConfirmation>({ field: "confirmed", headerName: "OK", width: 70, getValue: (r) => r.confirmed }),
      columnFactories.createNumberColumn<BreakoutConfirmation>({ field: "closePrice", headerName: "Close", width: 100, minDecimalPlaces: 2, getValue: (r) => r.closePrice ?? null }),
      columnFactories.createNumberColumn<BreakoutConfirmation>({ field: "level20d", headerName: "Break lvl", width: 100, minDecimalPlaces: 2, getValue: (r) => r.level20d ?? null }),
      columnFactories.createTextColumn<BreakoutConfirmation>({
        field: "volumeRatio", headerName: "Vol×", width: 80,
        getValue: (r) => (r.volumeRatio != null ? `${Number(r.volumeRatio).toFixed(2)}×` : ""),
      }),
    ],
    [hitRates],
  );

  return (
    <PageFrame>
      {error ? <Alert severity="error">{error}</Alert> : null}
      <Alert severity="info">
        <strong>Pattern breakouts</strong> for F&amp;O confirmation: range, ascending/descending
        triangles, double top/bottom. First run may sync ~40 daily bars from Angel (a few minutes).
        Turn off “Confirmed only” to see scanned near-misses.
      </Alert>
      <TablePane>
        <ZenTable
          fillHeight
          columns={columns}
          rows={visible}
          getRowId={(r) => r.id}
          loading={loading}
          enableSearch
          emptyMessage="No pattern breakouts. Click Run breakout scan."
        />
      </TablePane>
    </PageFrame>
  );
}
