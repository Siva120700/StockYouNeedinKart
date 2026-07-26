import { useEffect, useMemo, useState } from "react";
import { Alert, Button, Stack as MuiStack } from "@mui/material";
import { Play, ArrowSquareOut } from "@phosphor-icons/react";
import { ActionFactory, DataFactory } from "../api/factories";
import type { LiquiditySignal } from "../api/types";
import { columnFactories } from "../zen_components/table/columnFactories";
import ZenTable from "../zen_components/table/ZenTable";
import { useZenPrimaryLayoutContext } from "../zen_components/layout/ZenPrimaryLayoutProvider";
import { DEFAULT_SMALL_ICON_SIZE } from "../constants";

export default function LiquiditySignalsPage() {
  const { setTitle, setBreadcrumbs, setPageActions, setIsSyncing } =
    useZenPrimaryLayoutContext();
  const [rows, setRows] = useState<LiquiditySignal[]>([]);
  const [loading, setLoading] = useState(true);
  const [running, setRunning] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function refresh() {
    setError(null);
    setIsSyncing(true);
    try {
      setRows(await DataFactory.liquiditySignals());
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
      await ActionFactory.runLiquidityAnalysis();
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setRunning(false);
    }
  }

  async function onOpen(signalId: string) {
    try {
      await ActionFactory.openPositionFromLiquiditySignal(signalId);
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  }

  useEffect(() => {
    setTitle("Liquidity");
    setBreadcrumbs([{ label: "Home" }, { label: "Liquidity" }]);
    void refresh();
    return () => setPageActions(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    setPageActions(
      <MuiStack direction="row" spacing={1} alignItems="center">
        <Button
          variant="contained"
          size="small"
          disabled={running}
          startIcon={<Play size={DEFAULT_SMALL_ICON_SIZE} />}
          onClick={() => void onRun()}
        >
          {running ? "Running…" : "Run liquidity"}
        </Button>
      </MuiStack>,
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [running]);

  const columns = useMemo(() => {
    const formatTarget = (row: LiquiditySignal, target: number | null | undefined) => {
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
    };

    const formatSl = (row: LiquiditySignal) => {
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
    };

    return [
      columnFactories.createTextColumn<LiquiditySignal>({
        field: "appSymbol",
        headerName: "Symbol",
        width: 110,
        getValue: (r) => r.appSymbol,
      }),
      columnFactories.createStatusColumn<LiquiditySignal>(
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
      columnFactories.createNumberColumn<LiquiditySignal>({
        field: "entryPrice",
        headerName: "Entry",
        width: 100,
        minDecimalPlaces: 2,
        getValue: (r) => r.entryPrice,
      }),
      columnFactories.createTextColumn<LiquiditySignal>({
        field: "initialStopLoss",
        headerName: "SL",
        width: 130,
        getValue: (r) => formatSl(r),
      }),
      columnFactories.createTextColumn<LiquiditySignal>({
        field: "targetT1",
        headerName: "T1",
        width: 130,
        getValue: (r) => formatTarget(r, r.targetT1),
      }),
      columnFactories.createTextColumn<LiquiditySignal>({
        field: "targetT2",
        headerName: "T2",
        width: 130,
        getValue: (r) => formatTarget(r, r.targetT2),
      }),
      columnFactories.createTextColumn<LiquiditySignal>({
        field: "targetT3",
        headerName: "T3",
        width: 130,
        getValue: (r) => formatTarget(r, r.targetT3),
      }),
      columnFactories.createTextColumn<LiquiditySignal>({
        field: "relativeVolume",
        headerName: "RVOL",
        width: 90,
        getValue: (r) =>
          `${Number(r.relativeVolume).toFixed(2)} (${Math.round(Number(r.rvolPercentile) * 100)}%)`,
      }),
      columnFactories.createTextColumn<LiquiditySignal>({
        field: "sweptZoneType",
        headerName: "Sweep",
        width: 120,
        getValue: (r) =>
          r.sweptZoneType
            ? `${r.sweptZoneType}${r.sweptZonePrice != null ? ` @ ${Number(r.sweptZonePrice).toFixed(1)}` : ""}`
            : "",
      }),
      columnFactories.createTextColumn<LiquiditySignal>({
        field: "nearestZoneType",
        headerName: "Near zone",
        width: 120,
        getValue: (r) => {
          if (!r.nearestZoneType) return "";
          const dist =
            r.distancePct != null ? ` ${((Number(r.distancePct) * 100).toFixed(2))}%` : "";
          return `${r.nearestZoneType}${dist}`;
        },
      }),
      columnFactories.createBooleanColumn<LiquiditySignal>({
        field: "strongClose",
        headerName: "Strong",
        width: 80,
        getValue: (r) => r.strongClose,
      }),
      columnFactories.createActionColumn<LiquiditySignal>(
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
  }, []);

  return (
    <>
      {error ? (
        <Alert severity="error" sx={{ mb: 2 }}>
          {error}
        </Alert>
      ) : null}
      <ZenTable
        columns={columns}
        rows={rows}
        getRowId={(r) => r.id}
        loading={loading}
        emptyMessage="No liquidity signals. Click Run liquidity (needs 1H bars + 4H sweep + 1H confirm)."
      />
    </>
  );
}
