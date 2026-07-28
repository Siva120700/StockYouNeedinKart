import { useEffect, useMemo, useState } from "react";
import { Alert, Button, FormControlLabel, Stack as MuiStack, Switch } from "@mui/material";
import { ArrowSquareOut, Play } from "@phosphor-icons/react";
import { ActionFactory, DataFactory } from "../api/factories";
import type { ConfluenceSignal } from "../api/types";
import { columnFactories } from "../zen_components/table/columnFactories";
import ZenTable from "../zen_components/table/ZenTable";
import { useZenPrimaryLayoutContext } from "../zen_components/layout/ZenPrimaryLayoutProvider";
import { DEFAULT_SMALL_ICON_SIZE } from "../constants";

function formatTarget(row: ConfluenceSignal, target: number | null | undefined) {
  if (target == null || !Number.isFinite(Number(target)) || !row.entryPrice) return "";
  const t = Number(target);
  const entry = Number(row.entryPrice);
  if (entry === 0) return t.toFixed(2);
  const pct =
    row.side === "sell" ? ((entry - t) / entry) * 100 : ((t - entry) / entry) * 100;
  return `${t.toFixed(2)} (${pct >= 0 ? "+" : ""}${pct.toFixed(2)}%)`;
}

function formatSl(entry: number, sl: number, side: string) {
  if (!Number.isFinite(sl) || !Number.isFinite(entry) || entry === 0) return "";
  const pct = ((sl - entry) / entry) * 100;
  const riskPct = side === "sell" ? -pct : -Math.abs(pct);
  return `${sl.toFixed(2)} (${riskPct >= 0 ? "+" : ""}${riskPct.toFixed(2)}%)`;
}

function riskRewardRatio(row: ConfluenceSignal): number | null {
  const entry = Number(row.entryPrice);
  const sl = Number(row.initialStopLoss);
  const target = Number(row.targetT1 ?? row.targetT2 ?? row.targetT3);
  if (![entry, sl, target].every((n) => Number.isFinite(n)) || entry === 0) return null;
  const risk = row.side === "sell" ? sl - entry : entry - sl;
  const reward = row.side === "sell" ? entry - target : target - entry;
  if (risk <= 0 || reward <= 0) return null;
  return reward / risk;
}

export default function ConfluencePage() {
  const { setTitle, setBreadcrumbs, setPageActions, setIsSyncing } =
    useZenPrimaryLayoutContext();
  const [rows, setRows] = useState<ConfluenceSignal[]>([]);
  const [loading, setLoading] = useState(true);
  const [running, setRunning] = useState(false);
  const [sectorCheck, setSectorCheck] = useState(false);
  const [riskRewardCheck, setRiskRewardCheck] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function refresh() {
    setError(null);
    setIsSyncing(true);
    try {
      setRows(await DataFactory.confluenceSignals());
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setLoading(false);
      setIsSyncing(false);
    }
  }

  async function onRunBoth() {
    setRunning(true);
    setError(null);
    try {
      await ActionFactory.runAnalysis();
      await ActionFactory.runLiquidityAnalysis("fresh");
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setRunning(false);
    }
  }

  async function onOpen(row: ConfluenceSignal) {
    try {
      await ActionFactory.openPositionFromConfluence(
        row.liquiditySignalId,
        row.analysisSignalId,
      );
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  }

  const visibleRows = useMemo(() => {
    let list = [...rows];
    if (sectorCheck) list = list.filter((r) => r.sectorConfirmed);
    if (riskRewardCheck) {
      list = list.filter((r) => {
        const rr = riskRewardRatio(r);
        return rr != null && rr >= 1;
      });
    }
    return list;
  }, [rows, sectorCheck, riskRewardCheck]);

  useEffect(() => {
    setTitle("Confluence");
    setBreadcrumbs([{ label: "Home" }, { label: "Confluence" }]);
    void refresh();
    return () => setPageActions(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
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
        />
        <Button
          variant="contained"
          size="small"
          disabled={running}
          startIcon={<Play size={DEFAULT_SMALL_ICON_SIZE} />}
          onClick={() => void onRunBoth()}
        >
          {running ? "Running…" : "Run Signals + Liquidity Fresh"}
        </Button>
      </MuiStack>,
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [running, sectorCheck, riskRewardCheck]);

  const columns = useMemo(
    () => [
      columnFactories.createTextColumn<ConfluenceSignal>({
        field: "appSymbol",
        headerName: "Symbol",
        width: 110,
        getValue: (r) => r.appSymbol,
      }),
      columnFactories.createStatusColumn<ConfluenceSignal>(
        {
          buy: { label: "BUY", color: "#2e7d32" },
          sell: { label: "SELL", color: "#c62828" },
        },
        {
          field: "side",
          headerName: "Side",
          width: 90,
          getValue: (r) => r.side,
        },
      ),
      columnFactories.createNumberColumn<ConfluenceSignal>({
        field: "entryPrice",
        headerName: "Entry",
        width: 100,
        minDecimalPlaces: 2,
        getValue: (r) => r.entryPrice,
      }),
      columnFactories.createTextColumn<ConfluenceSignal>({
        field: "initialStopLoss",
        headerName: "SL (tighter)",
        width: 140,
        getValue: (r) => formatSl(Number(r.entryPrice), Number(r.initialStopLoss), r.side),
      }),
      columnFactories.createTextColumn<ConfluenceSignal>({
        field: "signalsStopLoss",
        headerName: "Sig SL",
        width: 110,
        getValue: (r) => Number(r.signalsStopLoss).toFixed(2),
      }),
      columnFactories.createTextColumn<ConfluenceSignal>({
        field: "liquidityStopLoss",
        headerName: "Liq SL",
        width: 110,
        getValue: (r) => Number(r.liquidityStopLoss).toFixed(2),
      }),
      columnFactories.createTextColumn<ConfluenceSignal>({
        field: "targetT1",
        headerName: "T1",
        width: 130,
        getValue: (r) => formatTarget(r, r.targetT1),
      }),
      columnFactories.createBooleanColumn<ConfluenceSignal>({
        field: "sectorConfirmed",
        headerName: "Sector",
        width: 80,
        getValue: (r) => r.sectorConfirmed,
      }),
      columnFactories.createBooleanColumn<ConfluenceSignal>({
        field: "freshCross",
        headerName: "Fresh",
        width: 80,
        getValue: (r) => r.freshCross,
      }),
      columnFactories.createActionColumn<ConfluenceSignal>(
        () => [
          {
            icon: <ArrowSquareOut size={DEFAULT_SMALL_ICON_SIZE} />,
            tooltip: "Open position (combined SL)",
            onClick: (r) => void onOpen(r),
          },
        ],
        { field: "actions", headerName: "", width: 72 },
      ),
    ],
    [],
  );

  return (
    <>
      {error ? (
        <Alert severity="error" sx={{ mb: 2 }}>
          {error}
        </Alert>
      ) : null}
      <Alert severity="info" sx={{ mb: 2 }}>
        Signals + Liquidity Fresh overlap: same side, entries within 0.2%, SL = tighter of
        both stops (minimizes risk). Targets from Liquidity Fresh.
      </Alert>
      <ZenTable
        columns={columns}
        rows={visibleRows}
        getRowId={(r) => r.id}
        loading={loading}
        enableSearch
        searchPlaceholder="Search symbol or name…"
        emptyMessage={
          sectorCheck || riskRewardCheck
            ? "No confluence rows match filters."
            : "No overlap yet. Run Signals + Liquidity Fresh, or check both pages have matching setups."
        }
      />
    </>
  );
}
