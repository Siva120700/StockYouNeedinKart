import { useEffect, useMemo, useState } from "react";
import {
  Alert,
  Box,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Typography,
} from "@mui/material";
import { CaretDown, CaretUp } from "@phosphor-icons/react";
import { useZenPrimaryLayoutContext } from "../../zen_components/layout/ZenPrimaryLayoutProvider";
import PageFrame from "../../zen_components/layout/PageFrame";
import { SectorScopeApi, type SectorScopeSector, type SectorScopeStock } from "./api";

const COLOR_CAP_PCT = 3;
/** Biggest |%| movers shown in the heatmap (up or down). */
const HEATMAP_TOP_N = 5;

const EXTREME_RED = { r: 211, g: 0, b: 0 };
const MID_WHITE = { r: 255, g: 255, b: 255 };
const EXTREME_GREEN = { r: 0, g: 176, b: 32 };

function fmtPct(n: number): string {
  const sign = n > 0 ? "+" : "";
  return `${sign}${n.toFixed(2)}%`;
}

function lerp(a: number, b: number, t: number): number {
  return Math.round(a + (b - a) * t);
}

function divergingColor(pct: number): string {
  const t = Math.min(1, Math.abs(pct) / COLOR_CAP_PCT);
  const punch = t ** 0.65;
  const from = MID_WHITE;
  const to = pct >= 0 ? EXTREME_GREEN : EXTREME_RED;
  const r = lerp(from.r, to.r, punch);
  const g = lerp(from.g, to.g, punch);
  const b = lerp(from.b, to.b, punch);
  return `rgb(${r}, ${g}, ${b})`;
}

function onExtremeColor(pct: number): boolean {
  return Math.abs(pct) >= COLOR_CAP_PCT * 0.45;
}

function labelColor(pct: number): string {
  if (Math.abs(pct) < 0.005) return "#757575";
  return pct >= 0
    ? `rgb(${EXTREME_GREEN.r}, ${EXTREME_GREEN.g}, ${EXTREME_GREEN.b})`
    : `rgb(${EXTREME_RED.r}, ${EXTREME_RED.g}, ${EXTREME_RED.b})`;
}

function ColorLegend() {
  return (
    <Stack direction="row" alignItems="center" spacing={0.75} mb={1}>
      <Typography variant="caption" color="text.secondary">
        −{COLOR_CAP_PCT}%
      </Typography>
      <Box
        sx={{
          height: 12,
          flex: 1,
          maxWidth: 320,
          borderRadius: 0.5,
          border: "1px solid",
          borderColor: "divider",
          background: `linear-gradient(to right, rgb(${EXTREME_RED.r},${EXTREME_RED.g},${EXTREME_RED.b}), rgb(${MID_WHITE.r},${MID_WHITE.g},${MID_WHITE.b}), rgb(${EXTREME_GREEN.r},${EXTREME_GREEN.g},${EXTREME_GREEN.b}))`,
        }}
      />
      <Typography variant="caption" color="text.secondary">
        +{COLOR_CAP_PCT}%
      </Typography>
    </Stack>
  );
}

function SectorBarChart({ sectors }: { sectors: SectorScopeSector[] }) {
  const maxAbs = Math.max(0.5, ...sectors.map((s) => Math.abs(s.medianChangePct)));
  return (
    <Stack spacing={0.75}>
      {sectors.map((s) => {
        const pct = s.medianChangePct;
        const widthPct = (Math.abs(pct) / maxAbs) * 50;
        const positive = pct >= 0;
        return (
          <Box
            key={s.instrumentId}
            sx={{
              display: "grid",
              gridTemplateColumns: "140px 1fr 72px",
              alignItems: "center",
              gap: 1,
            }}
          >
            <Typography variant="caption" noWrap title={s.displayName}>
              {s.displayName}
            </Typography>
            <Box sx={{ position: "relative", height: 18, bgcolor: "action.hover", borderRadius: 0.5 }}>
              <Box
                sx={{
                  position: "absolute",
                  top: 0,
                  bottom: 0,
                  left: "50%",
                  width: "1px",
                  bgcolor: "divider",
                }}
              />
              <Box
                sx={{
                  position: "absolute",
                  top: 2,
                  bottom: 2,
                  left: positive ? "50%" : `calc(50% - ${widthPct}%)`,
                  width: `${widthPct}%`,
                  bgcolor: divergingColor(pct),
                  borderRadius: 0.5,
                }}
              />
            </Box>
            <Typography
              variant="caption"
              textAlign="right"
              sx={{ color: labelColor(pct), fontWeight: 600 }}
            >
              {fmtPct(pct)}
            </Typography>
          </Box>
        );
      })}
    </Stack>
  );
}

function uniqueStocks(stocks: SectorScopeStock[] | null | undefined): SectorScopeStock[] {
  const seenId = new Set<string>();
  const seenSym = new Set<string>();
  const out: SectorScopeStock[] = [];
  for (const st of stocks ?? []) {
    const id = (st.instrumentId ?? "").toLowerCase();
    const sym = (st.appSymbol ?? "").toUpperCase();
    if (id && seenId.has(id)) continue;
    if (sym && seenSym.has(sym)) continue;
    if (id) seenId.add(id);
    if (sym) seenSym.add(sym);
    out.push(st);
  }
  return out;
}

function splitHeatmapAndTiles(sectors: SectorScopeSector[]) {
  const heatmapSectors = sectors
    .map((s) => ({
      ...s,
      stocks: uniqueStocks(s.stocks)
        .sort((a, b) => Math.abs(b.changePct) - Math.abs(a.changePct))
        .slice(0, HEATMAP_TOP_N),
    }))
    .filter((s) => s.stocks.length > 0);

  const tileSectors = sectors
    .map((s) => ({
      ...s,
      stocks: uniqueStocks(s.stocks).sort(
        (a, b) => Math.abs(b.changePct) - Math.abs(a.changePct),
      ),
    }))
    .filter((s) => s.stocks.length > 0)
    .sort((a, b) => Math.abs(b.medianChangePct) - Math.abs(a.medianChangePct));

  return { heatmapSectors, tileSectors };
}

function StockCell({ stock }: { stock: SectorScopeStock }) {
  const abs = Math.abs(stock.changePct);
  const flex = Math.max(1, Math.min(8, abs / 0.4));
  return (
    <Box
      sx={{
        flex: `${flex} 1 72px`,
        minHeight: 44,
        px: 0.75,
        py: 0.5,
        bgcolor: divergingColor(stock.changePct),
        color: onExtremeColor(stock.changePct) ? "#fff" : "text.primary",
        display: "flex",
        flexDirection: "column",
        justifyContent: "center",
      }}
      title={`${stock.instrumentName} ${fmtPct(stock.changePct)}`}
    >
      <Typography variant="caption" fontWeight={700} lineHeight={1.1} noWrap>
        {stock.appSymbol}
      </Typography>
      <Typography variant="caption" lineHeight={1.1}>
        {fmtPct(stock.changePct)}
      </Typography>
    </Box>
  );
}

function SectorTreemap({ sectors }: { sectors: SectorScopeSector[] }) {
  const blocks = sectors.filter((s) => s.stocks.length > 0);
  const maxMove = Math.max(
    1,
    ...blocks.map((s) => s.stocks.reduce((a, st) => a + Math.abs(st.changePct), 0)),
  );

  return (
    <Box sx={{ display: "flex", flexWrap: "wrap", gap: 1, alignItems: "stretch" }}>
      {blocks.map((s) => {
        const weight = s.stocks.reduce((a, st) => a + Math.abs(st.changePct), 0);
        const flex = Math.max(1.2, (weight / maxMove) * 8);
        return (
          <Box
            key={s.instrumentId}
            sx={{
              flex: `${flex} 1 220px`,
              border: "1px solid",
              borderColor: "divider",
              borderRadius: 1,
              overflow: "hidden",
              minHeight: 120,
              display: "flex",
              flexDirection: "column",
            }}
          >
            <Box
              sx={{
                px: 1,
                py: 0.5,
                bgcolor: divergingColor(s.medianChangePct),
                color: onExtremeColor(s.medianChangePct) ? "#fff" : "text.primary",
                borderBottom: "1px solid",
                borderColor: "divider",
              }}
            >
              <Typography variant="caption" fontWeight={700}>
                {s.displayName} {s.stocks.length}
              </Typography>
              <Typography
                variant="caption"
                sx={{
                  ml: 1,
                  color: onExtremeColor(s.medianChangePct) ? "#fff" : labelColor(s.medianChangePct),
                  fontWeight: 600,
                }}
              >
                {fmtPct(s.medianChangePct)}
              </Typography>
            </Box>
            <Box sx={{ display: "flex", flexWrap: "wrap", flex: 1 }}>
              {uniqueStocks(s.stocks).map((st) => (
                <StockCell key={`${st.instrumentId}:${st.appSymbol}`} stock={st} />
              ))}
            </Box>
          </Box>
        );
      })}
    </Box>
  );
}

function fmtPrice(n: number | null | undefined): string {
  if (n == null || !Number.isFinite(Number(n))) return "—";
  return Number(n).toLocaleString("en-IN", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
}

function SectorListTile({ sector }: { sector: SectorScopeSector }) {
  const up = sector.stocks.filter((s) => s.changePct > 0).length;
  const down = sector.stocks.filter((s) => s.changePct < 0).length;
  const total = sector.stocks.length;
  const downPct = total > 0 ? (down / total) * 100 : 0;
  const upPct = total > 0 ? (up / total) * 100 : 0;

  return (
    <Paper
      elevation={0}
      sx={{
        border: "1px solid",
        borderColor: "divider",
        borderRadius: 2,
        overflow: "hidden",
        height: "100%",
        display: "flex",
        flexDirection: "column",
      }}
    >
      <Box sx={{ px: 1.5, pt: 1.25, pb: 1 }}>
        <Stack direction="row" alignItems="center" justifyContent="space-between" gap={1}>
          <Typography variant="subtitle2" fontWeight={700} noWrap title={sector.displayName}>
            {sector.displayName}
          </Typography>
          <Stack direction="row" alignItems="center" spacing={1} flexShrink={0}>
            <Stack direction="row" alignItems="center" spacing={0.25} sx={{ color: "success.main" }}>
              <CaretUp size={14} weight="bold" />
              <Typography variant="caption" fontWeight={600}>
                {up}
              </Typography>
            </Stack>
            <Stack direction="row" alignItems="center" spacing={0.25} sx={{ color: "error.main" }}>
              <CaretDown size={14} weight="bold" />
              <Typography variant="caption" fontWeight={600}>
                {down}
              </Typography>
            </Stack>
            <Typography variant="caption" color="text.secondary">
              {total}
            </Typography>
          </Stack>
        </Stack>
        <Stack direction="row" alignItems="center" spacing={1} mt={0.75}>
          <Typography variant="caption" color="error.main" sx={{ minWidth: 88, whiteSpace: "nowrap" }}>
            {down} down ({downPct.toFixed(0)}%)
          </Typography>
          <Box
            sx={{
              flex: 1,
              height: 6,
              borderRadius: 1,
              overflow: "hidden",
              display: "flex",
              bgcolor: "action.hover",
            }}
          >
            <Box sx={{ width: `${downPct}%`, bgcolor: "error.main" }} />
            <Box sx={{ width: `${upPct}%`, bgcolor: "success.main" }} />
          </Box>
          <Typography
            variant="caption"
            color="success.main"
            sx={{ minWidth: 72, textAlign: "right", whiteSpace: "nowrap" }}
          >
            {up} up ({upPct.toFixed(0)}%)
          </Typography>
        </Stack>
      </Box>
      <Box sx={{ flex: 1, overflow: "auto", maxHeight: 320 }}>
        <Table size="small" stickyHeader>
          <TableHead>
            <TableRow>
              <TableCell sx={{ fontWeight: 600, py: 0.5 }}>Symbol</TableCell>
              <TableCell align="right" sx={{ fontWeight: 600, py: 0.5 }}>
                Price
              </TableCell>
              <TableCell align="right" sx={{ fontWeight: 600, py: 0.5 }}>
                %
              </TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {uniqueStocks(sector.stocks).map((st) => (
              <TableRow key={`${st.instrumentId}:${st.appSymbol}`} hover>
                <TableCell sx={{ py: 0.6, fontWeight: 700 }}>{st.appSymbol}</TableCell>
                <TableCell align="right" sx={{ py: 0.6 }}>
                  {fmtPrice(st.ltp)}
                </TableCell>
                <TableCell
                  align="right"
                  sx={{ py: 0.6, fontWeight: 600, color: labelColor(st.changePct) }}
                >
                  {fmtPct(st.changePct)}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Box>
    </Paper>
  );
}

export default function SectorScopePage() {
  const { setTitle, setBreadcrumbs, setPageActions, setIsSyncing } =
    useZenPrimaryLayoutContext();
  const [sectors, setSectors] = useState<SectorScopeSector[]>([]);
  const [niftyPct, setNiftyPct] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  async function refresh() {
    setError(null);
    setIsSyncing(true);
    try {
      const snap = await SectorScopeApi.fetch();
      setSectors(
        (snap.sectors ?? []).map((s) => {
          const stocks = uniqueStocks(s.stocks);
          return { ...s, stocks, constituentCount: stocks.length };
        }),
      );
      setNiftyPct(snap.niftyChangePct);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setLoading(false);
      setIsSyncing(false);
    }
  }

  useEffect(() => {
    setTitle("Sector Scope");
    setBreadcrumbs([{ label: "Home" }, { label: "Sector Scope" }]);
    void refresh();
    const id = window.setInterval(() => void refresh(), 30_000);
    return () => {
      window.clearInterval(id);
      setPageActions(null);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const leading = useMemo(
    () => sectors.filter((s) => !s.lagging).slice(0, 3).map((s) => s.displayName),
    [sectors],
  );

  const { heatmapSectors, tileSectors } = useMemo(
    () => splitHeatmapAndTiles(sectors),
    [sectors],
  );

  return (
    <PageFrame scroll>
      {error && <Alert severity="error">{error}</Alert>}
      <Alert severity="info">
        Median % change by sector vs Nifty 50
        {niftyPct != null ? ` (${fmtPct(niftyPct)})` : ""}. Equity BUY signals in
        lagging sectors (and SELLs in leading sectors) are down-ranked on Signals,
        Liquidity, Confluence, Breakout, Trade Score, and Options Intraday.
        {leading.length > 0 ? ` Leading: ${leading.join(", ")}.` : ""}
      </Alert>

      <Box>
        <Typography variant="h6" gutterBottom>
          Median % change by sector · sorted best to worst
        </Typography>
        {loading && sectors.length === 0 ? (
          <Typography variant="body2" color="text.secondary">
            Loading…
          </Typography>
        ) : (
          <>
            <ColorLegend />
            <SectorBarChart sectors={sectors} />
          </>
        )}
      </Box>

      <Box>
        <Typography variant="h6" gutterBottom>
          Sector heatmap
        </Typography>
        <Typography variant="body2" color="text.secondary" mb={1}>
          Color and size follow % change. Each sector shows its top {HEATMAP_TOP_N} names by
          absolute % move (up or down).
        </Typography>
        <ColorLegend />
        {heatmapSectors.length === 0 ? (
          <Typography variant="body2" color="text.secondary">
            {loading ? "Loading…" : "No movers yet."}
          </Typography>
        ) : (
          <SectorTreemap sectors={heatmapSectors} />
        )}
      </Box>

      <Box>
        <Typography variant="h6" gutterBottom>
          Sector lists
        </Typography>
        <Typography variant="body2" color="text.secondary" mb={1}>
          All names in each sector — symbol, price, and % change.
        </Typography>
        {tileSectors.length === 0 ? (
          <Typography variant="body2" color="text.secondary">
            {loading ? "Loading…" : "No sector names."}
          </Typography>
        ) : (
          <Box
            sx={{
              display: "grid",
              gridTemplateColumns: { xs: "1fr", md: "1fr 1fr" },
              gap: 2,
            }}
          >
            {tileSectors.map((s) => (
              <SectorListTile key={s.instrumentId} sector={s} />
            ))}
          </Box>
        )}
      </Box>
    </PageFrame>
  );
}
