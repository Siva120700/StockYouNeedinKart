import { useEffect, useMemo, useState } from "react";
import { Alert, Button } from "@mui/material";
import { ArrowsClockwise, XCircle } from "@phosphor-icons/react";
import { ActionFactory, DataFactory } from "../api/factories";
import type { OpenPosition } from "../api/types";
import { columnFactories } from "../zen_components/table/columnFactories";
import ZenTable from "../zen_components/table/ZenTable";
import { useZenPrimaryLayoutContext } from "../zen_components/layout/ZenPrimaryLayoutProvider";
import PageFrame, { TablePane } from "../zen_components/layout/PageFrame";
import { DEFAULT_SMALL_ICON_SIZE } from "../constants";
import {
  closeLocalDayPosition,
  listLocalDayPositions,
  type LocalDayPosition,
} from "../utils/localDayPositions";

type PositionRow = OpenPosition & {
  source: "paper" | "local";
  notes?: string | null;
};

export default function PositionsPage() {
  const { setTitle, setBreadcrumbs, setPageActions, setIsSyncing } =
    useZenPrimaryLayoutContext();
  const [rows, setRows] = useState<PositionRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  function mergeLocal(paper: OpenPosition[]): PositionRow[] {
    const local: PositionRow[] = listLocalDayPositions().map((p: LocalDayPosition) => ({
      id: p.id,
      symbol: p.symbol,
      instrumentName: p.instrumentName,
      side: p.side,
      quantityLots: p.quantityLots,
      entryPrice: p.entryPrice,
      currentStopLoss: p.currentStopLoss,
      lastPrice: p.lastPrice,
      computedUnrealizedPnl: p.computedUnrealizedPnl ?? null,
      source: "local",
      notes: p.notes,
    }));
    const paperRows: PositionRow[] = paper.map((p) => ({ ...p, source: "paper" }));
    return [...paperRows, ...local];
  }

  async function refresh() {
    setError(null);
    setIsSyncing(true);
    try {
      const paper = await DataFactory.openPositions();
      setRows(mergeLocal(paper));
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
      setRows(mergeLocal([]));
    } finally {
      setLoading(false);
      setIsSyncing(false);
    }
  }

  async function onClose(row: PositionRow) {
    try {
      if (row.source === "local") {
        closeLocalDayPosition(row.id);
        await refresh();
        return;
      }
      await ActionFactory.closePosition(row.id, row.lastPrice ?? row.entryPrice);
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
      columnFactories.createTextColumn<PositionRow>({
        field: "symbol",
        headerName: "Symbol",
        width: 140,
        getValue: (r) => r.symbol,
      }),
      columnFactories.createTextColumn<PositionRow>({
        field: "instrumentName",
        headerName: "Name",
        width: 180,
        getValue: (r) => r.instrumentName,
      }),
      columnFactories.createStatusColumn<PositionRow>(
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
      columnFactories.createNumberColumn<PositionRow>({
        field: "quantityLots",
        headerName: "Lots",
        width: 80,
        getValue: (r) => r.quantityLots,
      }),
      columnFactories.createNumberColumn<PositionRow>({
        field: "entryPrice",
        headerName: "Entry",
        width: 110,
        minDecimalPlaces: 2,
        getValue: (r) => r.entryPrice,
      }),
      columnFactories.createNumberColumn<PositionRow>({
        field: "lastPrice",
        headerName: "LTP",
        width: 110,
        minDecimalPlaces: 2,
        getValue: (r) => r.lastPrice,
      }),
      columnFactories.createCurrencyColumn<PositionRow>({
        field: "computedUnrealizedPnl",
        headerName: "P&L",
        width: 120,
        minDecimalPlaces: 2,
        prefix: "INR",
        getValue: (r) => r.computedUnrealizedPnl,
      }),
      columnFactories.createTextColumn<PositionRow>({
        field: "notes",
        headerName: "Notes",
        width: 220,
        getValue: (r) => r.notes ?? (r.source === "local" ? "Options trade" : ""),
      }),
      columnFactories.createActionColumn<PositionRow>(
        () => [
          {
            icon: <XCircle size={DEFAULT_SMALL_ICON_SIZE} />,
            tooltip: "Close",
            color: "#c62828",
            onClick: (r) => void onClose(r),
          },
        ],
        { field: "actions", headerName: "", width: 72 },
      ),
    ],
    [],
  );

  return (
    <PageFrame>
      {error ? (
        <Alert severity="error">
          {error}
        </Alert>
      ) : null}
      <TablePane>
        <ZenTable
          fillHeight
          columns={columns}
          rows={rows}
          getRowId={(r) => r.id}
          loading={loading}
          enableSearch
          searchPlaceholder="Search symbol or name…"
          emptyMessage="No open positions. Use Trade on a signals page."
        />
      </TablePane>
    </PageFrame>
  );
}
