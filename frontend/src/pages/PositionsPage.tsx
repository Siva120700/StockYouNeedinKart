import { useEffect, useMemo, useState } from "react";
import { Alert, Button } from "@mui/material";
import { ArrowsClockwise, XCircle } from "@phosphor-icons/react";
import { ActionFactory, DataFactory } from "../api/factories";
import type { OpenPosition } from "../api/types";
import { columnFactories } from "../zen_components/table/columnFactories";
import ZenTable from "../zen_components/table/ZenTable";
import { useZenPrimaryLayoutContext } from "../zen_components/layout/ZenPrimaryLayoutProvider";
import { DEFAULT_SMALL_ICON_SIZE } from "../constants";

export default function PositionsPage() {
  const { setTitle, setBreadcrumbs, setPageActions, setIsSyncing } =
    useZenPrimaryLayoutContext();
  const [rows, setRows] = useState<OpenPosition[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function refresh() {
    setError(null);
    setIsSyncing(true);
    try {
      setRows(await DataFactory.openPositions());
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setLoading(false);
      setIsSyncing(false);
    }
  }

  async function onClose(positionId: string, exitPrice: number) {
    try {
      await ActionFactory.closePosition(positionId, exitPrice);
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  }

  useEffect(() => {
    setTitle("Open positions");
    setBreadcrumbs([{ label: "Home" }, { label: "Positions" }]);
    setPageActions(
      <Button
        variant="outlined"
        size="small"
        startIcon={<ArrowsClockwise size={DEFAULT_SMALL_ICON_SIZE} />}
        onClick={() => void refresh()}
      >
        Refresh
      </Button>,
    );
    void refresh();
    const id = window.setInterval(() => void refresh(), 15_000);
    return () => {
      window.clearInterval(id);
      setPageActions(null);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const columns = useMemo(
    () => [
      columnFactories.createTextColumn<OpenPosition>({
        field: "symbol",
        headerName: "Symbol",
        width: 120,
        getValue: (r) => r.symbol,
      }),
      columnFactories.createStatusColumn<OpenPosition>(
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
      columnFactories.createNumberColumn<OpenPosition>({
        field: "quantityLots",
        headerName: "Lots",
        width: 80,
        getValue: (r) => r.quantityLots,
      }),
      columnFactories.createNumberColumn<OpenPosition>({
        field: "entryPrice",
        headerName: "Entry",
        width: 110,
        minDecimalPlaces: 2,
        getValue: (r) => r.entryPrice,
      }),
      columnFactories.createNumberColumn<OpenPosition>({
        field: "lastPrice",
        headerName: "LTP",
        width: 110,
        minDecimalPlaces: 2,
        getValue: (r) => r.lastPrice,
      }),
      columnFactories.createCurrencyColumn<OpenPosition>({
        field: "computedUnrealizedPnl",
        headerName: "P&L",
        width: 120,
        minDecimalPlaces: 2,
        prefix: "INR",
        getValue: (r) => r.computedUnrealizedPnl,
      }),
      columnFactories.createActionColumn<OpenPosition>(
        () => [
          {
            icon: <XCircle size={DEFAULT_SMALL_ICON_SIZE} />,
            tooltip: "Close",
            color: "#c62828",
            onClick: (r) =>
              void onClose(r.id, r.lastPrice ?? r.entryPrice),
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
        enableSearch
        searchPlaceholder="Search symbol or name…"
        emptyMessage="No open positions."
      />
    </>
  );
}
