import { useEffect, useMemo, useState } from "react";
import { Alert, Button } from "@mui/material";
import { ArrowsClockwise } from "@phosphor-icons/react";
import { ActionFactory, DataFactory } from "../api/factories";
import type { LtpQuote } from "../api/types";
import { columnFactories } from "../zen_components/table/columnFactories";
import ZenTable from "../zen_components/table/ZenTable";
import {
  useZenPrimaryLayoutContext,
} from "../zen_components/layout/ZenPrimaryLayoutProvider";
import PageFrame, { TablePane } from "../zen_components/layout/PageFrame";
import { DEFAULT_SMALL_ICON_SIZE } from "../constants";

function fmtFetchedAtIst(iso: string | null | undefined): string {
  if (!iso) return "—";
  try {
    return new Date(iso).toLocaleString("en-IN", {
      timeZone: "Asia/Kolkata",
      day: "2-digit",
      month: "short",
      hour: "2-digit",
      minute: "2-digit",
      second: "2-digit",
      hour12: true,
    });
  } catch {
    return "—";
  }
}

export default function LtpPage() {
  const { setTitle, setBreadcrumbs, setPageActions, setIsSyncing } =
    useZenPrimaryLayoutContext();
  const [rows, setRows] = useState<LtpQuote[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);

  const liveCount = useMemo(
    () => rows.filter((r) => r.ltp > 0 && r.fetchedAt).length,
    [rows],
  );

  async function loadCached() {
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

  /** Sync Angel tokens for full universe, fetch LTP, then reload the table. */
  async function refreshFromAngel() {
    setError(null);
    setInfo(null);
    setRefreshing(true);
    setIsSyncing(true);
    try {
      const tokens = await ActionFactory.syncUniverseTokens();
      const updated = await ActionFactory.refreshLtp();
      const next = await DataFactory.ltp();
      setRows(next);
      const live = next.filter((r) => r.ltp > 0 && r.fetchedAt).length;
      setInfo(
        `${next.length} symbols · ${live} with live LTP (${tokens} Angel tokens mapped, ${updated} quotes updated).`,
      );
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setRefreshing(false);
      setLoading(false);
      setIsSyncing(false);
    }
  }

  useEffect(() => {
    setTitle(`Live LTP (${liveCount}/${rows.length || "…"})`);
  }, [liveCount, rows.length, setTitle]);

  useEffect(() => {
    setBreadcrumbs([{ label: "Home" }, { label: "Live LTP" }]);
    void loadCached();
    // Light re-read of DB every 15s (Worker also writes during market hours).
    const id = window.setInterval(() => void loadCached(), 15_000);
    return () => {
      window.clearInterval(id);
      setPageActions(null);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    setPageActions(
      <Button
        variant="outlined"
        size="small"
        disabled={refreshing}
        startIcon={<ArrowsClockwise size={DEFAULT_SMALL_ICON_SIZE} />}
        onClick={() => void refreshFromAngel()}
      >
        {refreshing ? "Fetching…" : "Refresh"}
      </Button>,
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [refreshing]);

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
        getValue: (r) => (r.ltp > 0 ? r.ltp : null),
        displayRenderer: (v) =>
          v != null && Number(v) > 0 ? Number(v).toFixed(2) : "—",
      }),
      columnFactories.createDateTimeColumn<LtpQuote>({
        field: "fetchedAt",
        headerName: "As of (IST)",
        width: 160,
        getValue: (r) => r.fetchedAt,
        displayRenderer: (v) => fmtFetchedAtIst(v ? String(v) : null),
      }),
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
          getRowId={(r) => r.instrumentId}
          loading={loading}
          enableSearch
          searchPlaceholder="Search symbol or name…"
          emptyMessage="No symbols in universe — restart API to seed F&O."
        />
      </TablePane>
    </PageFrame>
  );
}
