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
import { CaretUp, Play } from "@phosphor-icons/react";
import { columnFactories } from "../../zen_components/table/columnFactories";
import ZenTable from "../../zen_components/table/ZenTable";
import { useZenPrimaryLayoutContext } from "../../zen_components/layout/ZenPrimaryLayoutProvider";
import { DEFAULT_SMALL_ICON_SIZE } from "../../constants";
import { IndexOptionsApi } from "./api";
import type { NiftyOrbRecommendation } from "./types";
import {
  loadHistoricalHitRates,
  type HitRateByInstrument,
} from "../../utils/historicalHitRate";

const SOURCE_ORB = "nifty_orb";
const SOURCE_COMBO = "nifty_orb_liq_v2";
const SOURCE_LIQ_BO = "nifty_liq_breakout";
const SOURCE_BRK_VOL = "nifty_breakout_volume";
const SOURCE_HERO_ZERO = "nifty_hero_zero";
const POLL_MS = 45_000;

function isMarketHoursIst(): boolean {
  const ist = new Date(
    new Date().toLocaleString("en-US", { timeZone: "Asia/Kolkata" }),
  );
  const day = ist.getDay();
  if (day === 0 || day === 6) return false;
  const mins = ist.getHours() * 60 + ist.getMinutes();
  return mins >= 9 * 60 + 10 && mins <= 15 * 60 + 35;
}

function fmt(n: number | null | undefined, d = 2): string {
  if (n == null || !Number.isFinite(Number(n))) return "—";
  return Number(n).toFixed(d);
}

function fmtDelta(n: number | null | undefined, d = 3): string {
  if (n == null || !Number.isFinite(Number(n))) return "—";
  return Math.abs(Number(n)).toFixed(d);
}

function sourceLabel(source: string): string {
  if (source === SOURCE_COMBO) return "ORB + Liquidity V2";
  if (source === SOURCE_LIQ_BO) return "Liquidity + Breakout";
  if (source === SOURCE_BRK_VOL) return "Breakout + Volume";
  if (source === SOURCE_HERO_ZERO) return "Hero Zero";
  return "Nifty ORB";
}

function buildColumns() {
  return [
    columnFactories.createTextColumn<NiftyOrbRecommendation>({
      field: "side",
      headerName: "Side",
      width: 70,
      getValue: (r) => r.side,
    }),
    columnFactories.createTextColumn<NiftyOrbRecommendation>({
      field: "contractStrike",
      headerName: "Strike",
      width: 110,
      getValue: (r) =>
        r.contractStrike != null
          ? `${fmt(r.contractStrike, 0)} ${r.contractOptionType ?? ""}`.trim()
          : "—",
    }),
    columnFactories.createTextColumn<NiftyOrbRecommendation>({
      field: "premiumLtp",
      headerName: "Prem Entry",
      width: 100,
      getValue: (r) => fmt(r.premiumLtp),
    }),
    columnFactories.createTextColumn<NiftyOrbRecommendation>({
      field: "premiumStopLoss",
      headerName: "Prem SL",
      width: 90,
      getValue: (r) => fmt(r.premiumStopLoss),
    }),
    columnFactories.createTextColumn<NiftyOrbRecommendation>({
      field: "premiumTargetT1",
      headerName: "Prem T1",
      width: 90,
      getValue: (r) => fmt(r.premiumTargetT1),
    }),
    columnFactories.createTextColumn<NiftyOrbRecommendation>({
      field: "contractTradingSymbol",
      headerName: "Contract",
      width: 170,
      getValue: (r) => r.contractTradingSymbol ?? "—",
    }),
    columnFactories.createTextColumn<NiftyOrbRecommendation>({
      field: "spotLtp",
      headerName: "Spot",
      width: 90,
      getValue: (r) => fmt(r.spotLtp),
    }),
    columnFactories.createTextColumn<NiftyOrbRecommendation>({
      field: "underlyingEntry",
      headerName: "Nifty Entry",
      width: 100,
      getValue: (r) => fmt(r.underlyingEntry),
    }),
    columnFactories.createTextColumn<NiftyOrbRecommendation>({
      field: "underlyingStopLoss",
      headerName: "Nifty SL",
      width: 90,
      getValue: (r) => fmt(r.underlyingStopLoss),
    }),
    columnFactories.createTextColumn<NiftyOrbRecommendation>({
      field: "underlyingTargetT1",
      headerName: "Nifty T1",
      width: 90,
      getValue: (r) => fmt(r.underlyingTargetT1),
    }),
    columnFactories.createTextColumn<NiftyOrbRecommendation>({
      field: "delta",
      headerName: "Δ",
      width: 70,
      getValue: (r) => fmtDelta(r.delta, 3),
    }),
    columnFactories.createTextColumn<NiftyOrbRecommendation>({
      field: "status",
      headerName: "Status",
      width: 110,
      getValue: (r) => r.status,
    }),
  ];
}

function DetailPanel({
  row,
  onClose,
}: {
  row: NiftyOrbRecommendation;
  onClose: () => void;
}) {
  const isCombo = row.signalSource === SOURCE_COMBO;
  const isLiqBo = row.signalSource === SOURCE_LIQ_BO;
  const isBrkVol = row.signalSource === SOURCE_BRK_VOL;
  const isHeroZero = row.signalSource === SOURCE_HERO_ZERO;
  return (
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
        <Typography variant="h6">NIFTY {row.side.toUpperCase()}</Typography>
        <Chip size="small" label={sourceLabel(row.signalSource)} />
        <Chip
          size="small"
          color={
            row.status === "recommended"
              ? "success"
              : row.status === "waiting"
                ? "warning"
                : "default"
          }
          label={row.status}
        />
        <Button
          size="small"
          endIcon={<CaretUp size={DEFAULT_SMALL_ICON_SIZE} />}
          onClick={onClose}
        >
          Close
        </Button>
      </Stack>
      <Typography variant="body2" mb={1}>
        {row.status === "recommended" && row.contractTradingSymbol
          ? `Buy ${row.contractStrike != null ? `${fmt(row.contractStrike, 0)} ${row.contractOptionType ?? ""}` : row.contractTradingSymbol} @ ₹${fmt(row.premiumLtp)} | Prem SL ₹${fmt(row.premiumStopLoss)} | Prem T1 ₹${fmt(row.premiumTargetT1)} | Flat by ${row.flatByIst}`
          : (row.skipReason ?? row.status)}
      </Typography>
      <Stack direction={{ xs: "column", md: "row" }} spacing={4}>
        <Box flex={1}>
          <Typography variant="subtitle2" gutterBottom>
            Option ticket (buy)
          </Typography>
          <Typography variant="body2">
            Strike:{" "}
            {row.contractStrike != null
              ? `${fmt(row.contractStrike, 0)} ${row.contractOptionType ?? ""}`
              : "—"}
          </Typography>
          <Typography variant="body2">
            Contract: {row.contractTradingSymbol ?? "—"}
          </Typography>
          <Typography variant="body2">
            Entry (premium): ₹{fmt(row.premiumLtp)}
          </Typography>
          <Typography variant="body2">
            SL (premium): ₹{fmt(row.premiumStopLoss)}
          </Typography>
          <Typography variant="body2">
            T1 / T2 / T3 (premium): ₹{fmt(row.premiumTargetT1)} / ₹
            {fmt(row.premiumTargetT2)} / ₹{fmt(row.premiumTargetT3)}
          </Typography>
          <Typography variant="body2">
            Expiry: {row.contractExpiryLabel ?? "—"} | Lot:{" "}
            {row.contractLotSize ?? "—"} | Δ {fmtDelta(row.delta, 3)}
          </Typography>
          <Typography variant="body2">
            ATM alt: {row.altTradingSymbol ?? "—"} @ ₹{fmt(row.altPremiumLtp)}
          </Typography>
        </Box>
        <Box flex={1}>
          <Typography variant="subtitle2" gutterBottom>
            {isLiqBo
              ? "Nifty levels vs strike chart (ticket = strike premium)"
              : isBrkVol
                ? "Nifty Breakout + volume (Δ × Nifty option ticket)"
                : isHeroZero
                  ? "Hero Zero — far OTM lottery (full premium at risk)"
                  : isCombo
                ? "Composed Nifty levels (ORB + Liq V2)"
                : "Nifty ORB (structure)"}
          </Typography>
          <Typography variant="body2">Spot: {fmt(row.spotLtp)}</Typography>
          {!isLiqBo && !isBrkVol && !isHeroZero && (
            <Typography variant="body2">
              OR: {fmt(row.orbLow)} – {fmt(row.orbHigh)} ({fmt(row.orbRange, 1)} pts)
            </Typography>
          )}
          <Typography variant="body2">Entry: {fmt(row.underlyingEntry)}</Typography>
          <Typography variant="body2">SL: {fmt(row.underlyingStopLoss)}</Typography>
          <Typography variant="body2">
            T1 / T2 / T3: {fmt(row.underlyingTargetT1)} /{" "}
            {fmt(row.underlyingTargetT2)} / {fmt(row.underlyingTargetT3)}
          </Typography>
        </Box>
      </Stack>
      <Stack direction="row" flexWrap="wrap" gap={0.5} mt={1}>
        {(row.reasons ?? []).map((x) => (
          <Chip key={x} size="small" label={x} variant="outlined" />
        ))}
      </Stack>
    </Box>
  );
}

function SectionTable({
  title,
  blurb,
  rows,
  columns,
  loading,
  expandedId,
  onExpand,
}: {
  title: string;
  blurb: string;
  rows: NiftyOrbRecommendation[];
  columns: ReturnType<typeof buildColumns>;
  loading: boolean;
  expandedId: string | null;
  onExpand: (id: string | null) => void;
}) {
  const expanded = rows.find((r) => r.id === expandedId) ?? null;
  return (
    <Stack spacing={1.5}>
      <Box>
        <Typography variant="h6">{title}</Typography>
        <Typography variant="body2" color="text.secondary">
          {blurb}
        </Typography>
      </Box>
      <ZenTable
        rows={rows}
        columns={columns}
        loading={loading}
        getRowId={(r) => r.id}
        onRowClick={(r) => onExpand(expandedId === r.id ? null : r.id)}
      />
      <Collapse in={!!expanded}>
        {expanded && (
          <DetailPanel row={expanded} onClose={() => onExpand(null)} />
        )}
      </Collapse>
    </Stack>
  );
}

export default function IndexOptionsPage() {
  const { setTitle, setBreadcrumbs, setPageActions, setIsSyncing } =
    useZenPrimaryLayoutContext();
  const [rows, setRows] = useState<NiftyOrbRecommendation[]>([]);
  const [, setHitRates] = useState<HitRateByInstrument>(() => new Map());
  const [loading, setLoading] = useState(true);
  const [running, setRunning] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<"all" | "recommended" | "waiting" | "skipped">(
    "all",
  );
  const [expandedOrbId, setExpandedOrbId] = useState<string | null>(null);
  const [expandedComboId, setExpandedComboId] = useState<string | null>(null);
  const [expandedLiqBoId, setExpandedLiqBoId] = useState<string | null>(null);
  const [expandedBrkVolId, setExpandedBrkVolId] = useState<string | null>(null);
  const [expandedHeroZeroId, setExpandedHeroZeroId] = useState<string | null>(null);

  async function refresh(silent = false) {
    if (!silent) setError(null);
    if (!silent) setIsSyncing(true);
    try {
      const [recs, ratesOrb, ratesCombo, ratesLiqBo, ratesBrkVol, ratesHeroZero] =
        await Promise.all([
        IndexOptionsApi.fetchRecommendations(),
        loadHistoricalHitRates("nifty_orb"),
        loadHistoricalHitRates("nifty_orb_liq_v2"),
        loadHistoricalHitRates("nifty_liq_breakout"),
        loadHistoricalHitRates("nifty_breakout_volume"),
        loadHistoricalHitRates("nifty_hero_zero"),
      ]);
      setRows(recs);
      const merged = new Map(ratesOrb);
      for (const [k, v] of ratesCombo) merged.set(k, v);
      for (const [k, v] of ratesLiqBo) merged.set(k, v);
      for (const [k, v] of ratesBrkVol) merged.set(k, v);
      for (const [k, v] of ratesHeroZero) merged.set(k, v);
      setHitRates(merged);
    } catch (e) {
      if (!silent) setError(e instanceof Error ? e.message : String(e));
    } finally {
      setLoading(false);
      if (!silent) setIsSyncing(false);
    }
  }

  async function onRun() {
    setRunning(true);
    setError(null);
    try {
      await IndexOptionsApi.runAnalysis();
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setRunning(false);
    }
  }

  const orbRows = useMemo(() => {
    const list = rows.filter((r) => (r.signalSource || SOURCE_ORB) === SOURCE_ORB);
    if (statusFilter === "all") return list;
    return list.filter((r) => r.status === statusFilter);
  }, [rows, statusFilter]);

  const comboRows = useMemo(() => {
    const list = rows.filter((r) => r.signalSource === SOURCE_COMBO);
    if (statusFilter === "all") return list;
    return list.filter((r) => r.status === statusFilter);
  }, [rows, statusFilter]);

  const liqBoRows = useMemo(() => {
    const list = rows.filter((r) => r.signalSource === SOURCE_LIQ_BO);
    if (statusFilter === "all") return list;
    return list.filter((r) => r.status === statusFilter);
  }, [rows, statusFilter]);

  const brkVolRows = useMemo(() => {
    const list = rows.filter((r) => r.signalSource === SOURCE_BRK_VOL);
    if (statusFilter === "all") return list;
    return list.filter((r) => r.status === statusFilter);
  }, [rows, statusFilter]);

  const heroZeroRows = useMemo(() => {
    const list = rows.filter((r) => r.signalSource === SOURCE_HERO_ZERO);
    if (statusFilter === "all") return list;
    return list.filter((r) => r.status === statusFilter);
  }, [rows, statusFilter]);

  const columns = useMemo(() => buildColumns(), []);

  useEffect(() => {
    setTitle("Index Options");
    setBreadcrumbs([{ label: "Home" }, { label: "Index Options" }]);
    void refresh();

    const poll = () => {
      if (isMarketHoursIst() || document.visibilityState === "visible") void refresh(true);
    };
    const intervalId = window.setInterval(poll, POLL_MS);
    const onVisible = () => {
      if (document.visibilityState === "visible") void refresh(true);
    };
    document.addEventListener("visibilitychange", onVisible);

    return () => {
      setPageActions(null);
      window.clearInterval(intervalId);
      document.removeEventListener("visibilitychange", onVisible);
    };
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
          <MenuItem value="waiting">Waiting</MenuItem>
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

  return (
    <Stack spacing={3} p={2}>
      {error && <Alert severity="error">{error}</Alert>}

      <SectionTable
        title="Nifty ORB"
        blurb="30-min ORB (9:15–9:45). CE when OR high breaks · PE when OR low breaks — up to two independent tickets. High-probability strikes (score ≥80, combos ≥85) trigger bell + browser alerts. Flat by 14:30 IST."
        rows={orbRows}
        columns={columns}
        loading={loading}
        expandedId={expandedOrbId}
        onExpand={setExpandedOrbId}
      />

      <SectionTable
        title="ORB + Liquidity V2"
        blurb="Same session: ORB trigger must align with Liquidity V2 (same side, entry within 0.5%). Entry from ORB; SL is the nearer of ORB and Liq V2; T1–T3 at 2R/3R/4R. Same strike + premium ticket as ORB."
        rows={comboRows}
        columns={columns}
        loading={loading}
        expandedId={expandedComboId}
        onExpand={setExpandedComboId}
      />

      <SectionTable
        title="Liquidity + Breakout"
        blurb="When Liq V2 and Breakout agree on side → Δ × Nifty option ticket (1 ITM + ATM). Single-engine setups use strike premium chart with match ≥55. Flat by 14:30 IST."
        rows={liqBoRows}
        columns={columns}
        loading={loading}
        expandedId={expandedLiqBoId}
        onExpand={setExpandedLiqBoId}
      />

      <SectionTable
        title="Breakout + Volume"
        blurb="Nifty 2-day high/low breakout with volume confirmation only (no Liquidity V2). Option ticket via Δ × Nifty entry / SL / T1 — same 1 ITM + ATM alt as ORB. High-probability (≥80) triggers bell alerts."
        rows={brkVolRows}
        columns={columns}
        loading={loading}
        expandedId={expandedBrkVolId}
        onExpand={setExpandedBrkVolId}
      />

      <SectionTable
        title="Hero Zero"
        blurb="Far OTM lottery when ORB break and/or Breakout+Volume gives a clear direction. Buy cheap CE/PE (₹8–₹45, Δ 0.04–0.22) — risk full premium, targets 2× / 3× / 5× premium. Speculative only; no bell alerts. Flat by 14:30 IST."
        rows={heroZeroRows}
        columns={columns}
        loading={loading}
        expandedId={expandedHeroZeroId}
        onExpand={setExpandedHeroZeroId}
      />
    </Stack>
  );
}
