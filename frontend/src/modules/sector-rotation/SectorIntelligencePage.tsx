import { useEffect, useMemo, useState } from "react";
import {
  Accordion,
  AccordionDetails,
  AccordionSummary,
  Alert,
  Box,
  Chip,
  Collapse,
  LinearProgress,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Typography,
} from "@mui/material";
import { CaretDown } from "@phosphor-icons/react";
import { DEFAULT_SMALL_ICON_SIZE } from "../../constants";
import { useNavigate } from "react-router-dom";
import { useZenPrimaryLayoutContext } from "../../zen_components/layout/ZenPrimaryLayoutProvider";
import PageFrame from "../../zen_components/layout/PageFrame";
import {
  SectorRotationApi,
  type SectorRotationSector,
  type SectorRotationSnapshot,
  type SectorRotationStock,
} from "./api";

function fmtPct(n: number | null | undefined, d = 2): string {
  if (n == null || !Number.isFinite(n)) return "—";
  const sign = n > 0 ? "+" : "";
  return `${sign}${n.toFixed(d)}%`;
}

function bucketTitle(bucket: string): string {
  if (bucket === "capital_entering") return "Capital entering";
  if (bucket === "leading") return "Leading";
  if (bucket === "capital_leaving") return "Capital leaving";
  return "Neutral";
}

function bucketEmoji(bucket: string): string {
  if (bucket === "capital_entering") return "🔥";
  if (bucket === "leading") return "🟢";
  if (bucket === "capital_leaving") return "🔴";
  return "🟡";
}

function alignmentLabel(a: string): string {
  if (a === "a_plus") return "A+";
  if (a === "stock_only") return "Stock only";
  if (a === "watch") return "Watch";
  if (a === "avoid") return "Avoid";
  return "Neutral";
}

function alignmentColor(a: string): "success" | "warning" | "error" | "default" {
  if (a === "a_plus") return "success";
  if (a === "avoid" || a === "stock_only") return "warning";
  return "default";
}

const ALIGNMENT_LEGEND: {
  key: string;
  label: string;
  meaning: string;
  criteria: string;
}[] = [
  {
    key: "a_plus",
    label: "A+",
    meaning: "Best setup — strong sector and strong stock",
    criteria: "Sector score ≥ 70 and stock momentum ≥ 75",
  },
  {
    key: "watch",
    label: "Watch",
    meaning: "Sector is strong; stock hasn't caught up yet",
    criteria: "Sector score ≥ 70 but stock momentum < 60",
  },
  {
    key: "stock_only",
    label: "Stock only",
    meaning: "Stock looks good on its own, but the sector is weak",
    criteria: "Sector score < 50 and stock momentum ≥ 75",
  },
  {
    key: "avoid",
    label: "Avoid",
    meaning: "Weak link — skip for sector-aligned trades",
    criteria: "Sector score ≤ 40 or stock momentum ≤ 35",
  },
  {
    key: "neutral",
    label: "Neutral",
    meaning: "Neither great nor bad alignment",
    criteria: "Everything else",
  },
];

function ChipLegend() {
  return (
    <Accordion
      disableGutters
      elevation={0}
      sx={{
        border: "1px solid",
        borderColor: "divider",
        borderRadius: "4px !important",
        "&:before": { display: "none" },
      }}
    >
      <AccordionSummary expandIcon={<CaretDown size={DEFAULT_SMALL_ICON_SIZE} />}>
        <Typography variant="subtitle2" fontWeight={700}>
          How to read chips &amp; alignment tags
        </Typography>
      </AccordionSummary>
      <AccordionDetails sx={{ pt: 0 }}>
        <Stack spacing={2}>
          <Box>
            <Typography variant="caption" fontWeight={700} display="block" gutterBottom>
              Upcoming momentum (top section)
            </Typography>
            <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
              <Chip size="small" color="secondary" variant="outlined" label="Upcoming 72" />
              <Typography variant="body2" color="text.secondary">
                Early rotation score (0–100): flow acceleration, volume expansion, inflow z-score,
                and RS vs Nifty building before the sector tops the list. Higher = more likely to
                lead next.
              </Typography>
            </Stack>
          </Box>

          <Box>
            <Typography variant="caption" fontWeight={700} display="block" gutterBottom>
              On each sector card
            </Typography>
            <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
              <Chip size="small" color="primary" label="62" />
              <Typography variant="body2" color="text.secondary">
                Sector composite score (0–100) from flow, breadth, RS vs Nifty, trend, and volume.
              </Typography>
            </Stack>
          </Box>

          <Box>
            <Typography variant="caption" fontWeight={700} display="block" gutterBottom>
              On each stock row (expand a sector)
            </Typography>
            <Stack spacing={1}>
              <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
                <Chip size="small" color="primary" variant="outlined" label="78" />
                <Typography variant="body2" color="text.secondary">
                  Stock momentum score (0–100) — strength vs peers on 5D return, flow, and
                  today&apos;s move.
                </Typography>
              </Stack>
              <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
                <Chip size="small" color="success" label="A+" />
                <Typography variant="body2" color="text.secondary">
                  Alignment tag — whether sector strength and stock strength match (see table
                  below).
                </Typography>
              </Stack>
            </Stack>
          </Box>

          <Box>
            <Typography variant="caption" fontWeight={700} display="block" gutterBottom>
              Alignment tags
            </Typography>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
              Compare sector score vs stock momentum score. A+ setups are best for combined
              Signals / Trade Score trades; Stock only and Avoid are downranked on buy lists.
            </Typography>
            <Box sx={{ overflowX: "auto" }}>
              <Table size="small" sx={{ minWidth: 520 }}>
                <TableHead>
                  <TableRow>
                    <TableCell sx={{ fontWeight: 700, width: 100 }}>Tag</TableCell>
                    <TableCell sx={{ fontWeight: 700 }}>Meaning</TableCell>
                    <TableCell sx={{ fontWeight: 700 }}>When it appears</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {ALIGNMENT_LEGEND.map((row) => (
                    <TableRow key={row.key}>
                      <TableCell>
                        <Chip size="small" label={row.label} color={alignmentColor(row.key)} />
                      </TableCell>
                      <TableCell>
                        <Typography variant="body2">{row.meaning}</Typography>
                      </TableCell>
                      <TableCell>
                        <Typography variant="body2" color="text.secondary">
                          {row.criteria}
                        </Typography>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </Box>
          </Box>
        </Stack>
      </AccordionDetails>
    </Accordion>
  );
}

function MetricBar({ label, value, max = 100 }: { label: string; value: number; max?: number }) {
  const pct = Math.max(0, Math.min(100, (value / max) * 100));
  return (
    <Box>
      <Stack direction="row" justifyContent="space-between" mb={0.25}>
        <Typography variant="caption" color="text.secondary">
          {label}
        </Typography>
        <Typography variant="caption" fontWeight={600}>
          {value.toFixed(0)}
        </Typography>
      </Stack>
      <LinearProgress variant="determinate" value={pct} sx={{ height: 6, borderRadius: 1 }} />
    </Box>
  );
}

function StockRow({ stock, onClick }: { stock: SectorRotationStock; onClick?: () => void }) {
  return (
    <Stack
      direction="row"
      alignItems="center"
      justifyContent="space-between"
      py={0.5}
      sx={{ cursor: onClick ? "pointer" : "default", "&:hover": onClick ? { bgcolor: "action.hover" } : undefined }}
      onClick={onClick}
    >
      <Box>
        <Typography variant="body2" fontWeight={700}>
          {stock.symbol}
        </Typography>
        <Typography variant="caption" color="text.secondary">
          {fmtPct(stock.changePct)} · 5D {fmtPct(stock.return5dPct)} · flow ₹{stock.flowCr} Cr
        </Typography>
      </Box>
      <Stack direction="row" spacing={0.5} alignItems="center">
        <Chip size="small" label={`${stock.momentumScore}`} color="primary" variant="outlined" />
        <Chip size="small" label={alignmentLabel(stock.alignment)} color={alignmentColor(stock.alignment)} />
      </Stack>
    </Stack>
  );
}

function SectorCard({
  sector,
  expanded,
  onToggle,
  onStockClick,
  showUpcoming = false,
}: {
  sector: SectorRotationSector;
  expanded: boolean;
  onToggle: () => void;
  onStockClick: (symbol: string) => void;
  showUpcoming?: boolean;
}) {
  return (
    <Box
      sx={{
        border: "1px solid",
        borderColor: "divider",
        borderRadius: 1,
        overflow: "hidden",
      }}
    >
      <Box
        sx={{ px: 1.5, py: 1, bgcolor: "action.hover", cursor: "pointer" }}
        onClick={onToggle}
      >
        <Stack direction="row" justifyContent="space-between" alignItems="center">
          <Typography variant="subtitle2" fontWeight={700}>
            {sector.displayName}
          </Typography>
          <Stack direction="row" spacing={0.5} alignItems="center">
            {showUpcoming && (
              <Chip
                size="small"
                color="secondary"
                variant="outlined"
                label={`Upcoming ${sector.upcomingMomentumScore}`}
              />
            )}
            <Chip size="small" color="primary" label={sector.score} />
          </Stack>
        </Stack>
        {showUpcoming && sector.upcomingMomentumReasons.length > 0 && (
          <Stack direction="row" flexWrap="wrap" gap={0.5} mt={0.75}>
            {sector.upcomingMomentumReasons.map((r) => (
              <Chip key={r} size="small" variant="outlined" label={r} />
            ))}
          </Stack>
        )}
        <Typography variant="caption" color="text.secondary" display="block" sx={{ mt: 0.5 }}>
          Flow {sector.flowZScore.toFixed(2)}σ · Accel {fmtPct(sector.flowAccelerationPct, 1)} ·
          Breadth {sector.breadthPct.toFixed(0)}% · RS 5D {fmtPct(sector.relativeStrength5dPct)}
        </Typography>
      </Box>
      <Collapse in={expanded}>
        <Box sx={{ p: 1.5 }}>
          <Stack spacing={1} mb={1.5}>
            <MetricBar label="Capital flow (z-score mapped)" value={Math.min(100, (sector.flowZScore + 2) * 25)} />
            <MetricBar label="Breadth" value={sector.breadthPct} />
            <MetricBar label="Relative strength" value={Math.min(100, (sector.relativeStrength5dPct + 5) * 10)} />
            <MetricBar label="Trend" value={sector.trendScore} />
            <MetricBar label="Volume expansion" value={Math.min(100, sector.volumeExpansionPct)} />
          </Stack>
          <Typography variant="caption" fontWeight={700} display="block" mb={0.5}>
            All stocks ({sector.topStocks.length})
          </Typography>
          <Box sx={{ maxHeight: 360, overflowY: "auto", pr: 0.5 }}>
            {sector.topStocks.map((st) => (
              <StockRow key={st.instrumentId} stock={st} onClick={() => onStockClick(st.symbol)} />
            ))}
          </Box>
        </Box>
      </Collapse>
    </Box>
  );
}

function SectorBucket({
  title,
  emoji,
  sectors,
  expandedId,
  onExpand,
  onStockClick,
  emptyHint,
  showUpcoming = false,
}: {
  title: string;
  emoji: string;
  sectors: SectorRotationSector[];
  expandedId: string | null;
  onExpand: (id: string | null) => void;
  onStockClick: (symbol: string) => void;
  emptyHint?: string;
  showUpcoming?: boolean;
}) {
  return (
    <Stack spacing={1}>
      <Typography variant="h6">
        {emoji} {title}
        <Typography component="span" variant="body2" color="text.secondary" sx={{ ml: 1 }}>
          ({sectors.length})
        </Typography>
      </Typography>
      {sectors.length === 0 ? (
        <Typography variant="body2" color="text.secondary" sx={{ pl: 0.5 }}>
          {emptyHint ?? "None right now."}
        </Typography>
      ) : (
        sectors.map((s) => (
          <SectorCard
            key={s.sectorInstrumentId}
            sector={s}
            expanded={expandedId === s.sectorInstrumentId}
            onToggle={() =>
              onExpand(expandedId === s.sectorInstrumentId ? null : s.sectorInstrumentId)
            }
            onStockClick={onStockClick}
            showUpcoming={showUpcoming}
          />
        ))
      )}
    </Stack>
  );
}

export default function SectorIntelligencePage() {
  const navigate = useNavigate();
  const { setTitle, setBreadcrumbs, setPageActions, setIsSyncing } =
    useZenPrimaryLayoutContext();
  const [snap, setSnap] = useState<SectorRotationSnapshot | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [expandedId, setExpandedId] = useState<string | null>(null);

  async function refresh() {
    setError(null);
    setIsSyncing(true);
    try {
      setSnap(await SectorRotationApi.fetch());
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setLoading(false);
      setIsSyncing(false);
    }
  }

  useEffect(() => {
    setTitle("Sector Intelligence");
    setBreadcrumbs([{ label: "Home" }, { label: "Sector Intelligence" }]);
    void refresh();
    const id = window.setInterval(() => void refresh(), 60_000);
    return () => {
      window.clearInterval(id);
      setPageActions(null);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const regimeLabel = useMemo(() => {
    const l = snap?.regime.label ?? "neutral";
    if (l === "risk_on") return "Risk ON";
    if (l === "risk_off") return "Risk OFF";
    return "Neutral";
  }, [snap]);

  return (
    <PageFrame scroll>
      <Stack spacing={3} sx={{ pb: 3 }}>
        {error && <Alert severity="error">{error}</Alert>}

        <Alert severity="info">
          Top-down sector rotation: directional capital-flow proxy (return × traded value),
          flow z-score, acceleration, breadth, RS vs Nifty, and trend.{" "}
          <strong>Upcoming momentum</strong> flags sectors where flow and volume are building
          before they become leaders. Use with Signals / Trade Score — A+ setups need sector +
          stock alignment.
        </Alert>

        <ChipLegend />

        {snap && (
          <Box
            sx={{
              p: 2,
              border: "1px solid",
              borderColor: "divider",
              borderRadius: 1,
            }}
          >
            <Typography variant="subtitle1" fontWeight={700} gutterBottom>
              Market regime · {regimeLabel}
            </Typography>
            <Stack direction="row" flexWrap="wrap" gap={1} mb={1}>
              <Chip
                size="small"
                label={`Breadth ${snap.regime.marketBreadthPct.toFixed(0)}% (${snap.regime.advancers}↑ / ${snap.regime.decliners}↓)`}
              />
              <Chip
                size="small"
                label={snap.regime.niftyAboveEma20 ? "Nifty above EMA20" : "Nifty below EMA20"}
                color={snap.regime.niftyAboveEma20 ? "success" : "warning"}
                variant="outlined"
              />
              {snap.regime.niftyReturn5dPct != null && (
                <Chip size="small" label={`Nifty 5D ${fmtPct(snap.regime.niftyReturn5dPct)}`} variant="outlined" />
              )}
            </Stack>
            {snap.regime.reasons.map((r) => (
              <Typography key={r} variant="caption" color="text.secondary" display="block">
                {r}
              </Typography>
            ))}
          </Box>
        )}

        {loading && !snap ? (
          <Typography color="text.secondary">Loading sector rotation…</Typography>
        ) : snap ? (
          <>
            <SectorBucket
              title="Upcoming momentum"
              emoji="📈"
              sectors={snap.momentumBuilding}
              expandedId={expandedId}
              onExpand={setExpandedId}
              onStockClick={(sym) => navigate(`/analyze?symbol=${encodeURIComponent(sym)}`)}
              showUpcoming
              emptyHint="No sector showing early rotation (flow acceleration, volume pick-up, or improving RS)."
            />
            <SectorBucket
              title="Capital entering"
              emoji="🔥"
              sectors={snap.capitalEntering}
              expandedId={expandedId}
              onExpand={setExpandedId}
              onStockClick={(sym) => navigate(`/analyze?symbol=${encodeURIComponent(sym)}`)}
              emptyHint="No sector with accelerating inflow (positive flow z-score + acceleration) today."
            />
            <SectorBucket
              title="Leading"
              emoji="🟢"
              sectors={snap.leading}
              expandedId={expandedId}
              onExpand={setExpandedId}
              onStockClick={(sym) => navigate(`/analyze?symbol=${encodeURIComponent(sym)}`)}
              emptyHint="No sector scoring above 52 with non-negative flow."
            />
            <SectorBucket
              title="Neutral"
              emoji="🟡"
              sectors={snap.neutral}
              expandedId={expandedId}
              onExpand={setExpandedId}
              onStockClick={(sym) => navigate(`/analyze?symbol=${encodeURIComponent(sym)}`)}
            />
            <SectorBucket
              title="Capital leaving"
              emoji="🔴"
              sectors={snap.capitalLeaving}
              expandedId={expandedId}
              onExpand={setExpandedId}
              onStockClick={(sym) => navigate(`/analyze?symbol=${encodeURIComponent(sym)}`)}
            />
          </>
        ) : null}
      </Stack>
    </PageFrame>
  );
}
