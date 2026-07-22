import { useEffect, useMemo, useState } from "react";
import { Alert, Button } from "@mui/material";
import { ArrowsClockwise } from "@phosphor-icons/react";
import { DataFactory } from "../api/factories";
import type { LtpQuote } from "../api/types";
import { columnFactories } from "../zen_components/table/columnFactories";
import ZenTable from "../zen_components/table/ZenTable";
import {
  useZenPrimaryLayoutContext,
} from "../zen_components/layout/ZenPrimaryLayoutProvider";
import { DEFAULT_SMALL_ICON_SIZE } from "../constants";

export default function LtpPage() {
  const { setTitle, setBreadcrumbs, setPageActions, setIsSyncing } =
    useZenPrimaryLayoutContext();
  const [rows, setRows] = useState<LtpQuote[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function refresh() {
    setError(null);
    setIsSyncing(true);
    try {
      setRows(await DataFactory.ltp());
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setLoading(false);
      setIsSyncing(false);
    }
  }

  useEffect(() => {
    setTitle("Live LTP");
    setBreadcrumbs([{ label: "Home" }, { label: "Live LTP" }]);
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
      columnFactories.createTextColumn<LtpQuote>({
        field: "appSymbol",
        headerName: "Symbol",
        width: 120,
        getValue: (r) => r.appSymbol,
      }),
      columnFactories.createTextColumn<LtpQuote>({
        field: "instrumentName",
        headerName: "Name",
        width: 220,
        getValue: (r) => r.instrumentName,
      }),
      columnFactories.createTextColumn<LtpQuote>({
        field: "exchange",
        headerName: "Exch",
        width: 80,
        getValue: (r) => r.exchange,
      }),
      columnFactories.createNumberColumn<LtpQuote>({
        field: "ltp",
        headerName: "LTP",
        width: 110,
        minDecimalPlaces: 2,
        getValue: (r) => r.ltp,
      }),
      columnFactories.createDateTimeColumn<LtpQuote>({
        field: "fetchedAt",
        headerName: "As of",
        width: 180,
        getValue: (r) => r.fetchedAt,
      }),
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
        getRowId={(r) => r.instrumentId}
        loading={loading}
        emptyMessage="No LTP yet — start Worker with Angel Enabled=true."
      />
    </>
  );
}
