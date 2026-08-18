import { useEffect, useMemo, useState } from "react";
import { Alert, Box, Stack, Typography } from "@mui/material";
import { useZenPrimaryLayoutContext } from "../../zen_components/layout/ZenPrimaryLayoutProvider";
import PageFrame from "../../zen_components/layout/PageFrame";
import { SectorScopeApi, type SectorScopeSector, type SectorScopeStock } from "./api";

const FLAT_HIDE = 0.05;
/** ± this % hits full red / full green (StepOne-style). */
const COLOR_CAP_PCT = 3;

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
  const blocks = sectors
    .map((s) => ({
      ...s,
      visible: s.stocks.filter((st) => Math.abs(st.changePct) >= FLAT_HIDE),
    }))
    .filter((s) => s.visible.length > 0);

  const maxMove = Math.max(
    1,
    ...blocks.map((s) => s.visible.reduce((a, st) => a + Math.abs(st.changePct), 0)),
  );

  return (
    <Box sx={{ display: "flex", flexWrap: "wrap", gap: 1, alignItems: "stretch" }}>
      {blocks.map((s) => {
        const weight = s.visible.reduce((a, st) => a + Math.abs(st.changePct), 0);
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
                {s.displayName} {s.visible.length}
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
              {s.visible.map((st) => (
                <StockCell key={st.instrumentId} stock={st} />
              ))}
            </Box>
          </Box>
        );
      })}
    </Box>
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
      setSectors(snap.sectors);
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
          Color and size follow % change. Flat names (|Δ| &lt; 0.05%) are hidden.
        </Typography>
        <ColorLegend />
        <SectorTreemap sectors={sectors} />
      </Box>
    </PageFrame>
  );
}
