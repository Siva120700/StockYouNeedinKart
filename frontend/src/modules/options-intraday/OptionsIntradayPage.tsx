import { useEffect, useMemo, useState } from "react";
import {
  Alert,
  Box,
  Button,
  Chip,
  Collapse,
  MenuItem,
  Stack,
  TextField,
  Typography,
} from "@mui/material";
import { CaretDown, CaretUp, Play } from "@phosphor-icons/react";
import { columnFactories } from "../../zen_components/table/columnFactories";
import ZenTable from "../../zen_components/table/ZenTable";
import { useZenPrimaryLayoutContext } from "../../zen_components/layout/ZenPrimaryLayoutProvider";
import { DEFAULT_SMALL_ICON_SIZE } from "../../constants";
import { OptionsIntradayApi } from "./api";
import type { OptionsIntradayRecommendation } from "./types";

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
  const [loading, setLoading] = useState(true);
  const [running, setRunning] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<"all" | "recommended" | "skipped">("all");
  const [expandedId, setExpandedId] = useState<string | null>(null);

  async function refresh() {
    setError(null);
    setIsSyncing(true);
    try {
      setRows(await OptionsIntradayApi.fetchRecommendations());
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

  const visible = useMemo(() => {
    if (statusFilter === "all") return rows;
    return rows.filter((r) => r.status === statusFilter);
  }, [rows, statusFilter]);

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
  }, [statusFilter, running]);

  const columns = useMemo(
    () => [
      columnFactories.createTextColumn<OptionsIntradayRecommendation>({
        field: "appSymbol",
        headerName: "Stock",
        width: 100,
        getValue: (r) => r.appSymbol,
      }),
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
        getValue: (r) => (r.impliedVolatility != null ? `${fmt(r.impliedVolatility, 1)}%` : "—"),
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
    ],
    [],
  );

  const expanded = rows.find((r) => r.id === expandedId) ?? null;

  return (
    <Stack spacing={2} p={2}>
      {error && <Alert severity="error">{error}</Alert>}
      <Alert severity="info">
        Direction, entry, SL and T1 come only from the underlying stock. Recommendations require
        confidence ≥75 (Confluence or supportive futures OI), ATM/1ITM Δ 0.45–0.60, volume ≥100,
        and bid/ask spread ≤5%. Exit when spot hits Stock SL or T1. No new entries at/after 15:20
        IST; all positions must be flat by 15:20.
      </Alert>
      <ZenTable
        rows={visible}
        columns={columns}
        loading={loading}
        getRowId={(r) => r.id}
        onRowClick={(r) => setExpandedId((id) => (id === r.id ? null : r.id))}
      />
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
                : expanded.skipReason ?? "Skipped"}
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
    </Stack>
  );
}
