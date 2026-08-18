import {
  Alert,
  Box,
  Chip,
  CircularProgress,
  Grid,
  Stack,
  Typography,
} from "@mui/material";
import type { MomentumFuturesSuggestion, MomentumSignal } from "../../api/types";

function fmt(n: number | null | undefined, d = 2): string {
  if (n == null || !Number.isFinite(Number(n))) return "—";
  return Number(n).toLocaleString("en-IN", {
    minimumFractionDigits: d,
    maximumFractionDigits: d,
  });
}

function fmtInr(n: number | null | undefined): string {
  if (n == null || !Number.isFinite(Number(n))) return "—";
  return `₹${Number(n).toLocaleString("en-IN", { maximumFractionDigits: 0 })}`;
}

function pnlColor(n: number | null | undefined): "success.main" | "error.main" | undefined {
  if (n == null || !Number.isFinite(Number(n)) || n === 0) return undefined;
  return n > 0 ? "success.main" : "error.main";
}

function buildUpLabel(buildUp: string | null | undefined): string {
  if (!buildUp) return "—";
  return buildUp.replace(/_/g, " ");
}

type Props = {
  signal: MomentumSignal;
  futures: MomentumFuturesSuggestion | null;
  loading: boolean;
};

export default function MomentumFuturesDetailPanel({ signal, futures, loading }: Props) {
  if (loading) {
    return (
      <Stack direction="row" alignItems="center" spacing={1} py={1}>
        <CircularProgress size={20} />
        <Typography variant="body2" color="text.secondary">
          Loading futures contract… first open may sync NFO from Angel (a few seconds).
        </Typography>
      </Stack>
    );
  }

  if (!futures) {
    return (
      <Typography variant="body2" color="text.secondary">
        Expand to load futures details.
      </Typography>
    );
  }

  if (futures.skipReason) {
    return <Alert severity="warning">{futures.skipReason}</Alert>;
  }

  return (
    <Stack spacing={2}>
      <Stack direction="row" alignItems="center" spacing={1} flexWrap="wrap">
        <Typography variant="subtitle1" fontWeight={600}>
          {signal.appSymbol} · {futures.tradingSymbol ?? "FUTSTK"}
        </Typography>
        {futures.expiryLabel ? <Chip size="small" label={futures.expiryLabel} /> : null}
        {futures.lotSize ? <Chip size="small" variant="outlined" label={`Lot ${futures.lotSize}`} /> : null}
        {futures.buildUp ? (
          <Chip size="small" color="default" label={buildUpLabel(futures.buildUp)} />
        ) : null}
        {futures.futuresConflict ? (
          <Chip size="small" color="warning" label="Futures OI conflict" />
        ) : null}
      </Stack>

      <Grid container spacing={2}>
        <Grid item xs={12} md={4}>
          <Typography variant="overline" color="text.secondary">
            Underlying (spot)
          </Typography>
          <Stack spacing={0.5} mt={0.5}>
            <Row label="Entry" value={fmt(signal.entryPrice)} />
            <Row label="Exit (SL)" value={fmt(signal.initialStopLoss)} />
            <Row label="Target T1" value={fmt(signal.targetT1)} />
            <Row label="Target T2" value={fmt(signal.targetT2)} />
            <Row label="Target T3" value={fmt(signal.targetT3)} />
            <Row label="Spot LTP" value={fmt(futures.spotLtp)} />
          </Stack>
        </Grid>
        <Grid item xs={12} md={4}>
          <Typography variant="overline" color="text.secondary">
            Futures contract
          </Typography>
          <Stack spacing={0.5} mt={0.5}>
            <Row label="Entry" value={fmt(futures.futuresEntry)} highlight />
            <Row label="Exit (SL)" value={fmt(futures.futuresExit)} />
            <Row label="Target T1" value={fmt(futures.futuresTargetT1)} />
            <Row label="Target T2" value={fmt(futures.futuresTargetT2)} />
            <Row label="Target T3" value={fmt(futures.futuresTargetT3)} />
            <Row
              label="Premium vs spot"
              value={
                futures.premiumPct != null
                  ? `${futures.premiumPct >= 0 ? "+" : ""}${futures.premiumPct.toFixed(2)}%`
                  : "—"
              }
            />
          </Stack>
        </Grid>
        <Grid item xs={12} md={4}>
          <Typography variant="overline" color="text.secondary">
            1 lot economics
          </Typography>
          <Stack spacing={0.5} mt={0.5}>
            <Row label="Contract value" value={fmtInr(futures.contractValue)} />
            <Row label="Margin required" value={fmtInr(futures.marginRequired)} highlight />
            <Row
              label="Expected profit (T1)"
              value={fmtInr(futures.expectedProfitT1)}
              color={pnlColor(futures.expectedProfitT1)}
            />
            <Row
              label="Expected profit (T2)"
              value={fmtInr(futures.expectedProfitT2)}
              color={pnlColor(futures.expectedProfitT2)}
            />
            <Row
              label="Expected profit (T3)"
              value={fmtInr(futures.expectedProfitT3)}
              color={pnlColor(futures.expectedProfitT3)}
            />
            <Row
              label="Expected SL"
              value={fmtInr(futures.expectedStopLoss)}
              color="error.main"
            />
          </Stack>
        </Grid>
      </Grid>

      <Typography variant="caption" color="text.secondary">
        P&amp;L = points × lot size ({futures.lotSize || 1}). Margin is ~18% of contract value (SPAN +
        exposure estimate, not broker RMS). Exit when spot hits SL or targets.
      </Typography>
    </Stack>
  );
}

function Row({
  label,
  value,
  highlight,
  color,
}: {
  label: string;
  value: string;
  highlight?: boolean;
  color?: "success.main" | "error.main";
}) {
  return (
    <Box display="flex" justifyContent="space-between" gap={2}>
      <Typography variant="body2" color="text.secondary">
        {label}
      </Typography>
      <Typography variant="body2" fontWeight={highlight ? 600 : 400} color={color}>
        {value}
      </Typography>
    </Box>
  );
}
