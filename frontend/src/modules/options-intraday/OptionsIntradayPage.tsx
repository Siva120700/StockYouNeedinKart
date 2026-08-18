import { useEffect, useMemo, useState } from "react";
import {
  Alert,
  Box,
  Button,
  Chip,
  Collapse,
  MenuItem,
  Stack,
  Switch,
  FormControlLabel,
  Tab,
  Tabs,
  TextField,
  Typography,
} from "@mui/material";
import { CaretDown, CaretUp, Handshake, Play } from "@phosphor-icons/react";
import { columnFactories } from "../../zen_components/table/columnFactories";
import type { ColumnConfig } from "../../zen_components/table/columnTypes";
import ZenTable from "../../zen_components/table/ZenTable";
import { useZenPrimaryLayoutContext } from "../../zen_components/layout/ZenPrimaryLayoutProvider";
import PageFrame, { TablePane } from "../../zen_components/layout/PageFrame";
import { DEFAULT_SMALL_ICON_SIZE } from "../../constants";
import { OptionsIntradayApi } from "./api";
import type { OptionsIntradayRecommendation } from "./types";
import {
  createHistoricalHitRateColumn,
  loadHistoricalHitRates,
  type HitRateByInstrument,
} from "../../utils/historicalHitRate";
import { createSectorRsColumn } from "../../utils/sectorRelativeStrength.tsx";
import { addLocalDayPosition, closeLocalDayPositionsByIds } from "../../utils/localDayPositions";
import {
  formatIstTime,
  isSignalDayTraded,
  markSignalDayTraded,
  syncSignalDayHistory,
  unmarkSignalDayTraded,
  type SignalDayEntry,
  type SignalsTab,
} from "../../utils/signalDayHistory";
import TradedDeleteBar from "../../zen_components/shared/TradedDeleteBar";

const HISTORY_SCOPE = "options_intraday";

function fmt(n: number | null | undefined, d = 2): string {
  if (n == null || !Number.isFinite(Number(n))) return "—";
  return Number(n).toFixed(d);
}

/** Long-option delta is always shown positive (we buy CE/PE; never write). */
function fmtDelta(n: number | null | undefined, d = 3): string {
  if (n == null || !Number.isFinite(Number(n))) return "—";
  return Math.abs(Number(n)).toFixed(d);
}

export default function OptionsIntradayPage() {
  const { setTitle, setBreadcrumbs, setPageActions, setIsSyncing } =
    useZenPrimaryLayoutContext();
  const [rows, setRows] = useState<OptionsIntradayRecommendation[]>([]);
  const [historyRows, setHistoryRows] = useState<
    SignalDayEntry<OptionsIntradayRecommendation>[]
  >([]);
  const [tradedRows, setTradedRows] = useState<
    SignalDayEntry<OptionsIntradayRecommendation>[]
  >([]);
  const [tab, setTab] = useState<SignalsTab>("active");
  const [selectedTradedIds, setSelectedTradedIds] = useState<string[]>([]);
  const [hitRates, setHitRates] = useState<HitRateByInstrument>(() => new Map());
  const [loading, setLoading] = useState(true);
  const [running, setRunning] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<"all" | "recommended" | "skipped">("all");
  const [hideLaggingRs, setHideLaggingRs] = useState(false);
  const [expandedId, setExpandedId] = useState<string | null>(null);

  async function refresh() {
    setError(null);
    setIsSyncing(true);
    try {
      const [recs, rates] = await Promise.all([
        OptionsIntradayApi.fetchRecommendations(),
        loadHistoricalHitRates("options_intraday"),
      ]);
      setRows(recs);
      setHitRates(rates);
      const synced = syncSignalDayHistory(HISTORY_SCOPE, recs);
      setHistoryRows(synced.history);
      setTradedRows(synced.traded);
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
      await OptionsIntradayApi.runAnalysis();
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setRunning(false);
    }
  }

  function onTrade(row: OptionsIntradayRecommendation) {
    setError(null);
    setInfo(null);
    const premium = Number(row.premiumLtp);
    const entry = Number.isFinite(premium) && premium > 0 ? premium : Number(row.underlyingEntry);
    const sl =
      row.premiumLtp != null && Number.isFinite(Number(row.premiumLtp))
        ? Number(row.premiumLtp) * 0.5
        : Number(row.underlyingStopLoss);
    markSignalDayTraded(HISTORY_SCOPE, row);
    addLocalDayPosition({
      id: `opt-intra-${row.id}`,
      scope: HISTORY_SCOPE,
      symbol: row.contractTradingSymbol ?? row.appSymbol,
      instrumentName: `${row.appSymbol} ${row.side.toUpperCase()} · stock options`,
      side: "buy",
      quantityLots: row.contractLotSize && row.contractLotSize > 0 ? 1 : 1,
      entryPrice: entry,
      currentStopLoss: Number.isFinite(sl) ? sl : entry * 0.5,
      lastPrice: entry,
      notes: `Stock SL ${fmt(row.underlyingStopLoss)} · T1 ${fmt(row.underlyingTargetT1)} · flat ${row.flatByIst}`,
    });
    setInfo(`${row.appSymbol} moved to Positions (Traded).`);
    const synced = syncSignalDayHistory(HISTORY_SCOPE, rows);
    setHistoryRows(synced.history);
    setTradedRows(synced.traded);
  }

  function tradedRowId(r: OptionsIntradayRecommendation) {
    return `${r.instrumentId}:${r.side}:${r.id}`;
  }

  function onDeleteSelectedTraded() {
    const selected = tradedRows.filter((r) => selectedTradedIds.includes(tradedRowId(r)));
    if (selected.length === 0) return;
    unmarkSignalDayTraded(HISTORY_SCOPE, selected);
    closeLocalDayPositionsByIds(selected.map((r) => `opt-intra-${r.id}`));
    setSelectedTradedIds([]);
    const synced = syncSignalDayHistory(HISTORY_SCOPE, rows);
    setHistoryRows(synced.history);
    setTradedRows(synced.traded);
    setInfo(`Removed ${selected.length} from Traded.`);
  }

  const filteredActive = useMemo(() => {
    let list = statusFilter === "all" ? rows : rows.filter((r) => r.status === statusFilter);
    if (hideLaggingRs) list = list.filter((r) => !r.sectorRs?.downranked);
    return list;
  }, [rows, statusFilter, hideLaggingRs]);

  const tableRows = useMemo(() => {
    if (tab === "history") return historyRows;
    if (tab === "traded") return tradedRows;
    return filteredActive;
  }, [tab, filteredActive, historyRows, tradedRows]);

  useEffect(() => {
    setTitle("Options Intraday");
    setBreadcrumbs([{ label: "Home" }, { label: "Options Intraday" }]);
    void refresh();
    return () => setPageActions(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    setPageActions(
      <Stack direction="row" spacing={1} alignItems="center">
        {tab === "active" ? (
          <>
            <TextField
              select
              size="small"
              label="Status"
              value={statusFilter}
              onChange={(e) => setStatusFilter(e.target.value as typeof statusFilter)}
              sx={{ minWidth: 140 }}
            >
              <MenuItem value="all">All</MenuItem>
              <MenuItem value="recommended">Recommended</MenuItem>
              <MenuItem value="skipped">Skipped</MenuItem>
            </TextField>
            <FormControlLabel
              control={
                <Switch
                  size="small"
                  checked={hideLaggingRs}
                  onChange={(e) => setHideLaggingRs(e.target.checked)}
                />
              }
              label="Hide lagging RS"
            />
          </>
        ) : null}
        <Button
          size="small"
          variant="contained"
          startIcon={<Play size={DEFAULT_SMALL_ICON_SIZE} />}
          onClick={() => void onRun()}
          disabled={running}
        >
          {running ? "Running…" : "Run"}
        </Button>
      </Stack>,
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [statusFilter, hideLaggingRs, running, tab]);

  const columns = useMemo(() => {
    const cols: ColumnConfig<OptionsIntradayRecommendation>[] = [
      columnFactories.createTextColumn<OptionsIntradayRecommendation>({
        field: "appSymbol",
        headerName: "Stock",
        width: 100,
        getValue: (r) => r.appSymbol,
      }),
      createSectorRsColumn<OptionsIntradayRecommendation>((r) => r.sectorRs),
      createHistoricalHitRateColumn<OptionsIntradayRecommendation>(
        hitRates,
        (r) => r.instrumentId,
      ),
      columnFactories.createTextColumn<OptionsIntradayRecommendation>({
        field: "side",
        headerName: "Side",
        width: 70,
        getValue: (r) => r.side,
      }),
      columnFactories.createTextColumn<OptionsIntradayRecommendation>({
        field: "spotLtp",
        headerName: "Spot",
        width: 80,
        getValue: (r) => fmt(r.spotLtp),
      }),
      columnFactories.createTextColumn<OptionsIntradayRecommendation>({
        field: "underlyingEntry",
        headerName: "Entry",
        width: 80,
        getValue: (r) => fmt(r.underlyingEntry),
      }),
      columnFactories.createTextColumn<OptionsIntradayRecommendation>({
        field: "underlyingStopLoss",
        headerName: "Stock SL",
        width: 90,
        getValue: (r) => fmt(r.underlyingStopLoss),
      }),
      columnFactories.createTextColumn<OptionsIntradayRecommendation>({
        field: "underlyingTargetT1",
        headerName: "Stock T1",
        width: 90,
        getValue: (r) => fmt(r.underlyingTargetT1),
      }),
      columnFactories.createTextColumn<OptionsIntradayRecommendation>({
        field: "contractTradingSymbol",
        headerName: "Option",
        width: 160,
        getValue: (r) => r.contractTradingSymbol ?? "—",
      }),
      columnFactories.createTextColumn<OptionsIntradayRecommendation>({
        field: "premiumLtp",
        headerName: "Premium",
        width: 90,
        getValue: (r) => fmt(r.premiumLtp),
      }),
      columnFactories.createTextColumn<OptionsIntradayRecommendation>({
        field: "delta",
        headerName: "Δ",
        width: 70,
        getValue: (r) => fmtDelta(r.delta, 3),
      }),
      columnFactories.createTextColumn<OptionsIntradayRecommendation>({
        field: "impliedVolatility",
        headerName: "IV",
        width: 70,
        getValue: (r) =>
          r.impliedVolatility != null ? `${fmt(r.impliedVolatility, 1)}%` : "—",
      }),
      columnFactories.createTextColumn<OptionsIntradayRecommendation>({
        field: "futuresBuildUp",
        headerName: "Futures",
        width: 120,
        getValue: (r) => r.futuresBuildUp ?? "—",
      }),
      columnFactories.createNumberColumn<OptionsIntradayRecommendation>({
        field: "confidenceScore",
        headerName: "Conf",
        width: 70,
        getValue: (r) => r.confidenceScore,
      }),
      columnFactories.createTextColumn<OptionsIntradayRecommendation>({
        field: "status",
        headerName: "Status",
        width: 110,
        getValue: (r) => r.status,
      }),
    ];

    if (tab === "history") {
      cols.push(
        columnFactories.createTextColumn<OptionsIntradayRecommendation>({
          field: "disappearedAt",
          headerName: "Left",
          width: 90,
          getValue: (r) =>
            formatIstTime(
              (r as SignalDayEntry<OptionsIntradayRecommendation>).disappearedAt,
            ),
        }),
      );
    }
    if (tab === "traded") {
      cols.push(
        columnFactories.createTextColumn<OptionsIntradayRecommendation>({
          field: "tradedAt",
          headerName: "Traded",
          width: 90,
          getValue: (r) =>
            formatIstTime((r as SignalDayEntry<OptionsIntradayRecommendation>).tradedAt),
        }),
      );
    }
    if (tab !== "traded") {
      cols.push(
        columnFactories.createActionColumn<OptionsIntradayRecommendation>(
          (row) => [
            {
              icon: <Handshake size={DEFAULT_SMALL_ICON_SIZE} />,
              tooltip: isSignalDayTraded(HISTORY_SCOPE, row)
                ? "Already traded"
                : "Trade — open in Positions",
              disabled: () => isSignalDayTraded(HISTORY_SCOPE, row),
              onClick: (r) => onTrade(r),
            },
          ],
          { field: "actions", headerName: "Trade", width: 80 },
        ),
      );
    }
    return cols;
  }, [hitRates, tab]);

  const expanded = tableRows.find((r) => r.id === expandedId) ?? null;

  const emptyMessage =
    tab === "history"
      ? "No options setups have left today. History keeps frozen premium/levels from first sighting."
      : tab === "traded"
        ? "No traded options today. Use Trade on Active or History."
        : "No recommendations. Click Run.";

  return (
    <PageFrame>
      {error && <Alert severity="error">{error}</Alert>}
      {info ? (
        <Alert severity="success" onClose={() => setInfo(null)}>
          {info}
        </Alert>
      ) : null}
      <Alert severity="info">
        Direction, entry, SL and T1 come only from the underlying stock. Recommendations require
        confidence ≥75 (Confluence or supportive futures OI), ATM/1ITM Δ 0.45–0.60, volume ≥100,
        and bid/ask spread ≤5%. Exit when spot hits Stock SL or T1. No new entries at/after 15:20
        IST; all positions must be flat by 15:20.
      </Alert>
      <Tabs
        value={tab}
        onChange={(_, v: SignalsTab) => {
          setTab(v);
          setSelectedTradedIds([]);
        }}
        sx={{ minHeight: 40 }}
      >
        <Tab value="active" label={`Active (${filteredActive.length})`} />
        <Tab value="history" label={`History (${historyRows.length})`} />
        <Tab value="traded" label={`Traded (${tradedRows.length})`} />
      </Tabs>
      {tab === "traded" ? (
        <TradedDeleteBar
          selectedCount={selectedTradedIds.length}
          onDelete={onDeleteSelectedTraded}
        />
      ) : null}
      <TablePane>
        <Box sx={{ flex: 1, minHeight: 0, display: "flex", flexDirection: "column" }}>
          <ZenTable
            fillHeight
            rows={tableRows}
            columns={columns}
            loading={loading}
            getRowId={(r) => (tab === "active" ? r.id : tradedRowId(r))}
            onRowClick={(r) => setExpandedId((id) => (id === r.id ? null : r.id))}
            emptyMessage={emptyMessage}
            enableSelection={tab === "traded"}
            selectedRowIds={tab === "traded" ? selectedTradedIds : undefined}
            onSelectedRowIdsChange={tab === "traded" ? setSelectedTradedIds : undefined}
          />
        </Box>
      <Collapse in={!!expanded}>
        {expanded && (
          <Box
            sx={{
              p: 2,
              border: "1px solid",
              borderColor: "divider",
              borderRadius: 1,
              bgcolor: "background.paper",
            }}
          >
            <Stack direction="row" alignItems="center" spacing={1} mb={1}>
              <Typography variant="h6">
                {expanded.appSymbol} {expanded.side.toUpperCase()}
              </Typography>
              <Chip size="small" label={expanded.signalSource} />
              <Chip
                size="small"
                color={expanded.status === "recommended" ? "success" : "default"}
                label={expanded.status}
              />
              <Button
                size="small"
                endIcon={
                  expandedId ? (
                    <CaretUp size={DEFAULT_SMALL_ICON_SIZE} />
                  ) : (
                    <CaretDown size={DEFAULT_SMALL_ICON_SIZE} />
                  )
                }
                onClick={() => setExpandedId(null)}
              >
                Close
              </Button>
            </Stack>
            <Typography variant="body2" mb={1}>
              {expanded.status === "recommended" && expanded.contractTradingSymbol
                ? `Buy ${expanded.contractTradingSymbol} @ ₹${fmt(expanded.premiumLtp)} | Exit if spot ${
                    expanded.side === "buy" ? "≤" : "≥"
                  } ${fmt(expanded.underlyingStopLoss)} (SL) or spot ${
                    expanded.side === "buy" ? "≥" : "≤"
                  } ${fmt(expanded.underlyingTargetT1)} (T1) | Flat by ${expanded.flatByIst}`
                : (expanded.skipReason ?? "Skipped")}
            </Typography>
            <Stack direction={{ xs: "column", md: "row" }} spacing={4}>
              <Box flex={1}>
                <Typography variant="subtitle2" gutterBottom>
                  Stock
                </Typography>
                <Typography variant="body2">Spot: {fmt(expanded.spotLtp)}</Typography>
                <Typography variant="body2">Entry: {fmt(expanded.underlyingEntry)}</Typography>
                <Typography variant="body2">SL: {fmt(expanded.underlyingStopLoss)}</Typography>
                <Typography variant="body2">
                  T1 / T2 / T3: {fmt(expanded.underlyingTargetT1)} /{" "}
                  {fmt(expanded.underlyingTargetT2)} / {fmt(expanded.underlyingTargetT3)}
                </Typography>
                <Typography variant="body2">
                  Futures: {expanded.futuresBuildUp ?? "—"}
                  {expanded.futuresPremiumPct != null
                    ? ` (${fmt(expanded.futuresPremiumPct)}%)`
                    : ""}
                </Typography>
              </Box>
              <Box flex={1}>
                <Typography variant="subtitle2" gutterBottom>
                  Option
                </Typography>
                <Typography variant="body2">
                  Contract: {expanded.contractTradingSymbol ?? "—"}
                </Typography>
                <Typography variant="body2">
                  Expiry: {expanded.contractExpiryLabel ?? "—"} | Lot:{" "}
                  {expanded.contractLotSize ?? "—"}
                </Typography>
                <Typography variant="body2">
                  Δ {fmtDelta(expanded.delta, 3)} · Γ {fmt(expanded.gamma, 4)} · Θ{" "}
                  {fmt(expanded.theta, 2)} · ν {fmt(expanded.vega, 2)} · IV{" "}
                  {expanded.impliedVolatility != null
                    ? `${fmt(expanded.impliedVolatility, 1)}%`
                    : "—"}
                </Typography>
                <Typography variant="body2">
                  Alt: {expanded.altTradingSymbol ?? "—"} (Δ {fmtDelta(expanded.altDelta, 3)}, prem{" "}
                  {fmt(expanded.altPremiumLtp)})
                </Typography>
              </Box>
            </Stack>
            <Stack direction="row" flexWrap="wrap" gap={0.5} mt={1}>
              {(expanded.reasons ?? []).map((x) => (
                <Chip key={x} size="small" label={x} variant="outlined" />
              ))}
            </Stack>
          </Box>
        )}
      </Collapse>
      </TablePane>
    </PageFrame>
  );
}
