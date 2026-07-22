import { useEffect, useMemo, useState } from "react";
import { Alert, Button, FormControlLabel, Switch, Stack as MuiStack } from "@mui/material";
import { Play, ArrowSquareOut } from "@phosphor-icons/react";
import { ActionFactory, DataFactory } from "../api/factories";
import type { Signal } from "../api/types";
import { columnFactories } from "../zen_components/table/columnFactories";
import ZenTable from "../zen_components/table/ZenTable";
import { useZenPrimaryLayoutContext } from "../zen_components/layout/ZenPrimaryLayoutProvider";
import { DEFAULT_SMALL_ICON_SIZE } from "../constants";

export default function SignalsPage() {
  const { setTitle, setBreadcrumbs, setPageActions, setIsSyncing } =
    useZenPrimaryLayoutContext();
  const [rows, setRows] = useState<Signal[]>([]);
  const [loading, setLoading] = useState(true);
  const [running, setRunning] = useState(false);
  const [sectorCheck, setSectorCheck] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function refresh() {
    setError(null);
    setIsSyncing(true);
    try {
      setRows(await DataFactory.signals());
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
      await ActionFactory.runAnalysis(sectorCheck);
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

  useEffect(() => {
    setTitle("Signals");
    setBreadcrumbs([{ label: "Home" }, { label: "Signals" }]);
    setPageActions(
      <MuiStack direction="row" spacing={1} alignItems="center">
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
    void refresh();
    return () => setPageActions(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [running, sectorCheck]);

  const columns = useMemo(
    () => [
      columnFactories.createTextColumn<Signal>({
        field: "appSymbol",
        headerName: "Symbol",
        width: 120,
        getValue: (r) => r.appSymbol,
      }),
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
      columnFactories.createNumberColumn<Signal>({
        field: "initialStopLoss",
        headerName: "SL",
        width: 110,
        minDecimalPlaces: 2,
        getValue: (r) => r.initialStopLoss,
      }),
      columnFactories.createNumberColumn<Signal>({
        field: "targetT1",
        headerName: "T1",
        width: 100,
        minDecimalPlaces: 2,
        getValue: (r) => r.targetT1,
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
      <ZenTable
        columns={columns}
        rows={rows}
        getRowId={(r) => r.id}
        loading={loading}
        emptyMessage="No signals — click Run analysis after bars are synced."
      />
    </>
  );
}