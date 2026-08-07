import { useEffect, useMemo, useState, type ReactNode } from "react";
import {
  Alert,
  Autocomplete,
  Box,
  Chip,
  Divider,
  LinearProgress,
  Stack,
  TextField,
  Typography,
} from "@mui/material";
import { MagnifyingGlass } from "@phosphor-icons/react";
import { DataFactory } from "../../api/factories";
import type { UniverseInstrument } from "../../api/types";
import { useZenPrimaryLayoutContext } from "../../zen_components/layout/ZenPrimaryLayoutProvider";
import { DEFAULT_SMALL_ICON_SIZE } from "../../constants";
import { AnalyzeStockApi } from "./api";
import { buildNextMoveSections } from "./nextMove";
import {
  buildFutureOutlookSections,
  readUserView,
  type OutlookSection,
} from "./outlook";
import {
  fmt,
  sourceLabel,
  verdictColor,
  type AnalyzeStockResult,
} from "./types";

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <Box>
      <Typography variant="caption" color="text.secondary" display="block">
        {label}
      </Typography>
      <Typography variant="body1" fontWeight={600}>
        {value}
      </Typography>
    </Box>
  );
}

const BIAS_LABEL = {
  bullish: "Bullish",
  bearish: "Bearish",
  neutral: "Neutral / sideways",
  unclear: "No clear direction",
} as const;

const BIAS_COLOR = {
  bullish: "success",
  bearish: "error",
  neutral: "warning",
  unclear: "default",
} as const;

const HORIZON_LABEL = {
  intraday: "Intraday",
  swing: "Swing (days)",
  positional: "Positional (weeks+)",
} as const;

const LEAN_LABEL = {
  favoured: "Favoured",
  possible: "Possible",
  unlikely: "Less likely",
} as const;

const LEAN_COLOR = {
  favoured: "primary",
  possible: "default",
  unlikely: "default",
} as const;

function OutlookBlock({ section }: { section: OutlookSection }) {
  return (
    <Box>
      <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap sx={{ mb: 0.5 }}>
        <Typography variant="subtitle2" fontWeight={700}>
          {section.title}
        </Typography>
        {section.lean && (
          <Chip size="small" label={LEAN_LABEL[section.lean]} color={LEAN_COLOR[section.lean]} variant="outlined" />
        )}
        {section.matchesUserView === true && (
          <Chip size="small" label="Matches your view" color="info" variant="outlined" />
        )}
        {section.matchesUserView === false && (
          <Chip size="small" label="Against your view" variant="outlined" />
        )}
      </Stack>
      <Typography variant="body2" color="text.primary" sx={{ lineHeight: 1.6 }}>
        {section.summary}
      </Typography>
      {section.bullets.length > 0 && (
        <Box component="ul" sx={{ m: 0, mt: 0.75, pl: 2.5 }}>
          {section.bullets.map((b) => (
            <Typography
              key={b}
              component="li"
              variant="body2"
              color="text.secondary"
              sx={{ lineHeight: 1.6, mb: 0.4 }}
            >
              {b}
            </Typography>
          ))}
        </Box>
      )}
    </Box>
  );
}

function Section({
  title,
  children,
}: {
  title: string;
  children: ReactNode;
}) {
  return (
    <Box sx={{ py: 2 }}>
      <Typography variant="subtitle1" fontWeight={700} gutterBottom>
        {title}
      </Typography>
      {children}
    </Box>
  );
}

export default function AnalyzeStockPage() {
  const { setTitle, setBreadcrumbs, setPageActions, setIsSyncing } =
    useZenPrimaryLayoutContext();
  const [universe, setUniverse] = useState<UniverseInstrument[]>([]);
  const [selected, setSelected] = useState<UniverseInstrument | null>(null);
  const [result, setResult] = useState<AnalyzeStockResult | null>(null);
  const [userView, setUserView] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const outlookSections = useMemo(
    () => (result ? buildFutureOutlookSections(result, userView) : []),
    [result, userView],
  );
  const nextMoveSections = useMemo(
    () => (result ? buildNextMoveSections(result) : []),
    [result],
  );
  const view = useMemo(() => readUserView(userView), [userView]);

  useEffect(() => {
    setTitle("Analyze Stock");
    setBreadcrumbs([{ label: "Home" }, { label: "Analyze Stock" }]);
    setPageActions(null);
    void DataFactory.universes()
      .then(setUniverse)
      .catch((e) => setError(e instanceof Error ? e.message : String(e)));
    return () => setPageActions(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function load(instrument: UniverseInstrument | null) {
    setSelected(instrument);
    setResult(null);
    setUserView("");
    if (!instrument) return;
    setLoading(true);
    setError(null);
    setIsSyncing(true);
    try {
      setResult(await AnalyzeStockApi.analyze(instrument.id));
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setLoading(false);
      setIsSyncing(false);
    }
  }

  const lvl = result?.levels;
  const setup = result?.primarySetup;

  return (
    <Stack spacing={2} sx={{ p: 2, maxWidth: 1100 }}>
      <Autocomplete
        sx={{ maxWidth: 420 }}
        size="small"
        options={universe}
        getOptionLabel={(o) => `${o.symbol} — ${o.name}`}
        value={selected}
        onChange={(_, v) => void load(v)}
        renderInput={(params) => (
          <TextField
            {...params}
            label="Search stock"
            placeholder="Type symbol or name"
            InputProps={{
              ...params.InputProps,
              startAdornment: (
                <>
                  <MagnifyingGlass size={DEFAULT_SMALL_ICON_SIZE} style={{ marginRight: 6 }} />
                  {params.InputProps.startAdornment}
                </>
              ),
            }}
          />
        )}
        isOptionEqualToValue={(a, b) => a.id === b.id}
      />

      {loading && <LinearProgress />}
      {error && <Alert severity="error">{error}</Alert>}

      {!selected && !loading && (
        <Typography color="text.secondary">
          Pick a stock to see a future outlook, pivots, trade score, entry / SL / T1, liquidity zones,
          and a verdict from your engines. Optionally write how you feel about the stock — the outlook
          will compare your view with the system.
        </Typography>
      )}

      {result && lvl && (
        <Box>
          <Stack
            direction={{ xs: "column", sm: "row" }}
            spacing={2}
            alignItems={{ sm: "center" }}
            justifyContent="space-between"
          >
            <Box>
              <Typography variant="h5" fontWeight={800}>
                {result.symbol}
              </Typography>
              <Typography color="text.secondary">{result.name}</Typography>
            </Box>
            <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
              <Chip
                label={result.verdictLabel}
                color={verdictColor(result.verdict)}
                sx={{ fontWeight: 700 }}
              />
              {result.spotLtp != null && (
                <Chip label={`LTP ${fmt(result.spotLtp)}`} variant="outlined" />
              )}
              {result.sectorSymbol && (
                <Chip
                  label={`Sector ${result.sectorSymbol}${
                    result.sectorConfirmed == null
                      ? ""
                      : result.sectorConfirmed
                        ? " ✓"
                        : " ✗"
                  }`}
                  variant="outlined"
                  color={
                    result.sectorConfirmed === true
                      ? "success"
                      : result.sectorConfirmed === false
                        ? "warning"
                        : "default"
                  }
                />
              )}
            </Stack>
          </Stack>

          {result.verdictReasons.length > 0 && (
            <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap sx={{ mt: 1.5 }}>
              {result.verdictReasons.map((r) => (
                <Chip key={r} size="small" label={r} />
              ))}
            </Stack>
          )}

          <Box sx={{ mt: 2.5 }}>
            <TextField
              label="Your view about this stock"
              placeholder="Write anything — e.g. I feel this will bounce, too extended might fall, confused / sideways…"
              value={userView}
              onChange={(e) => setUserView(e.target.value)}
              fullWidth
              multiline
              minRows={2}
              maxRows={6}
              size="small"
            />
            <Stack direction="row" spacing={1} alignItems="center" sx={{ mt: 1 }} flexWrap="wrap" useFlexGap>
              <Typography variant="caption" color="text.secondary">
                Read from your note:
              </Typography>
              <Chip size="small" label={BIAS_LABEL[view.bias]} color={BIAS_COLOR[view.bias]} variant="outlined" />
              {view.strength !== "none" && (
                <Chip size="small" label={`${view.strength} conviction`} variant="outlined" />
              )}
              {view.horizon && <Chip size="small" label={HORIZON_LABEL[view.horizon]} variant="outlined" />}
              <Typography variant="caption" color="text.secondary">
                Outlook updates as you type — no re-fetch needed.
              </Typography>
            </Stack>
          </Box>

          <Box
            sx={{
              mt: 2.5,
              p: 2,
              borderRadius: 1,
              border: "1px solid",
              borderColor: "primary.main",
              bgcolor: "background.default",
            }}
          >
            <Typography variant="subtitle1" fontWeight={800} gutterBottom>
              Future outlook
            </Typography>
            <Typography variant="caption" color="text.secondary" display="block" mb={1.5}>
              Scenario paths from live levels, engines and recent bars — not a guaranteed prediction.
              Your note changes emphasis and alignment only, never the price map.
            </Typography>
            <Stack spacing={2.5}>
              {outlookSections.map((s) => (
                <OutlookBlock key={s.title} section={s} />
              ))}
            </Stack>
          </Box>

          <Box
            sx={{
              mt: 2.5,
              p: 2,
              borderRadius: 1,
              border: "1px solid",
              borderColor: "divider",
              bgcolor: "background.default",
            }}
          >
            <Typography variant="subtitle1" fontWeight={800} gutterBottom>
              What can be the next move
            </Typography>
            <Typography variant="caption" color="text.secondary" display="block" mb={1.5}>
              Plain-language reading of this stock’s live engines — not a buy/sell order.
            </Typography>
            <Stack spacing={2}>
              {nextMoveSections.map((s) => (
                <Box key={s.title}>
                  <Typography variant="subtitle2" fontWeight={700} gutterBottom>
                    {s.title}
                  </Typography>
                  <Typography variant="body2" color="text.primary" sx={{ lineHeight: 1.6 }}>
                    {s.body}
                  </Typography>
                </Box>
              ))}
            </Stack>
          </Box>

          <Divider sx={{ my: 2 }} />

          <Section title="Primary setup (entry / SL / targets)">
            {setup ? (
              <Stack direction="row" spacing={3} flexWrap="wrap" useFlexGap>
                <Metric label="Source" value={sourceLabel(setup.source)} />
                <Metric label="Side" value={setup.side.toUpperCase()} />
                <Metric label="As of" value={setup.asOfDate} />
                <Metric label="Entry" value={fmt(setup.entry)} />
                <Metric label="Stop loss" value={fmt(setup.stopLoss)} />
                <Metric label="T1" value={fmt(setup.targetT1)} />
                <Metric label="T2" value={fmt(setup.targetT2)} />
                <Metric label="T3" value={fmt(setup.targetT3)} />
                <Metric label="Planned R:R" value={fmt(setup.plannedRiskReward)} />
              </Stack>
            ) : (
              <Typography color="text.secondary">
                No active setup. Run Signals / Liquidity Fresh / Trade Score first.
              </Typography>
            )}
          </Section>

          <Divider />

          <Section title="Pivot levels (classic floor from last daily bar)">
            {lvl ? (
              <Stack direction="row" spacing={3} flexWrap="wrap" useFlexGap>
                <Metric label="Pivot (PP)" value={fmt(lvl.pivot)} />
                <Metric label="R1" value={fmt(lvl.resistance1)} />
                <Metric label="R2" value={fmt(lvl.resistance2)} />
                <Metric label="R3" value={fmt(lvl.resistance3)} />
                <Metric label="S1" value={fmt(lvl.support1)} />
                <Metric label="S2" value={fmt(lvl.support2)} />
                <Metric label="S3" value={fmt(lvl.support3)} />
                <Metric label="Prior high" value={fmt(lvl.priorDayHigh)} />
                <Metric label="Prior low" value={fmt(lvl.priorDayLow)} />
                <Metric label="MA 2D" value={fmt(lvl.ma2d)} />
                <Metric label="MA 3D" value={fmt(lvl.ma3d)} />
                <Metric label="MA 5D" value={fmt(lvl.ma5d)} />
                <Metric label="Last 2D H" value={fmt(lvl.last2dHigh)} />
                <Metric label="Last 2D L" value={fmt(lvl.last2dLow)} />
              </Stack>
            ) : (
              <Typography color="text.secondary">No daily bars.</Typography>
            )}
          </Section>

          <Divider />

          <Section title="Liquidity zones (live for this stock)">
            {lvl?.liquidityEvalDetail && (
              <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
                {lvl.liquidityEvalDetail}
                {lvl.liquidityEvalStatus ? ` · status: ${lvl.liquidityEvalStatus}` : ""}
              </Typography>
            )}
            {lvl?.sweptZoneType ||
            lvl?.nearestZoneType ||
            (lvl?.liquidityZones?.length ?? 0) > 0 ||
            (lvl?.zoneTags?.length ?? 0) > 0 ? (
              <Stack spacing={1.5}>
                <Stack direction="row" spacing={3} flexWrap="wrap" useFlexGap>
                  <Metric
                    label="Swept zone"
                    value={
                      lvl.sweptZoneType
                        ? `${lvl.sweepSide ?? ""} ${lvl.sweptZoneType} @ ${fmt(lvl.sweptZonePrice)}`
                        : "—"
                    }
                  />
                  <Metric
                    label="Nearest zone"
                    value={
                      lvl.nearestZoneType
                        ? `${lvl.nearestZoneType} @ ${fmt(lvl.nearestZonePrice)} (${fmt(
                            lvl.distancePct != null ? lvl.distancePct * 100 : null,
                          )}%)`
                        : "—"
                    }
                  />
                  <Metric label="Context" value={lvl.liquidityContext ?? "—"} />
                </Stack>
                {(lvl.liquidityZones?.length ?? 0) > 0 && (
                  <Box
                    component="table"
                    sx={{
                      width: "100%",
                      maxWidth: 480,
                      borderCollapse: "collapse",
                      fontSize: 13,
                      "& th, & td": { textAlign: "right", py: 0.4, px: 1 },
                      "& th:first-of-type, & td:first-of-type": { textAlign: "left" },
                    }}
                  >
                    <thead>
                      <tr>
                        <th>Zone</th>
                        <th>Kind</th>
                        <th>Price</th>
                      </tr>
                    </thead>
                    <tbody>
                      {lvl.liquidityZones.map((z) => (
                        <tr key={`${z.type}-${z.price}`}>
                          <td>{z.type}</td>
                          <td>{z.kind}</td>
                          <td>{fmt(z.price)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </Box>
                )}
                {result.liquidityFresh && (
                  <Typography variant="body2" color="text.secondary">
                    Fresh setup: {result.liquidityFresh.side.toUpperCase()} · entry{" "}
                    {fmt(result.liquidityFresh.entryPrice)} · SL{" "}
                    {fmt(result.liquidityFresh.initialStopLoss)} · T1{" "}
                    {fmt(result.liquidityFresh.targetT1)} · RVOL{" "}
                    {fmt(result.liquidityFresh.relativeVolume)}
                  </Typography>
                )}
                {!result.liquidityFresh && result.liquidityClassic && (
                  <Typography variant="body2" color="text.secondary">
                    Classic setup: {result.liquidityClassic.side.toUpperCase()} · entry{" "}
                    {fmt(result.liquidityClassic.entryPrice)} · SL{" "}
                    {fmt(result.liquidityClassic.initialStopLoss)} · T1{" "}
                    {fmt(result.liquidityClassic.targetT1)}
                  </Typography>
                )}
              </Stack>
            ) : (
              <Typography color="text.secondary">
                {lvl?.liquidityEvalDetail ??
                  "No liquidity zones yet — need hourly bars for this stock."}
              </Typography>
            )}
          </Section>

          <Divider />

          <Section title="Trade score (calculated live for this stock)">
            {result.tradeScore ? (
              <Stack spacing={1.5}>
                <Stack direction="row" spacing={3} flexWrap="wrap" useFlexGap>
                  <Metric label="Score" value={`${result.tradeScore.confidenceScore}/100`} />
                  <Metric label="Rating" value={result.tradeScore.rating} />
                  <Metric label="Signals" value={`${result.tradeScore.signalsScore}/20`} />
                  <Metric label="Liquidity" value={`${result.tradeScore.liquidityScore}/20`} />
                  <Metric label="Breakout" value={`${result.tradeScore.breakoutScore}/30`} />
                  <Metric label="Futures" value={`${result.tradeScore.futuresScore}/15`} />
                  <Metric label="Options" value={`${result.tradeScore.optionsScore}/15`} />
                </Stack>
                <Typography variant="body2" color="text.secondary">
                  Why this score:
                </Typography>
                <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
                  {result.tradeScore.reasons.map((r) => (
                    <Chip key={r} size="small" label={r} />
                  ))}
                </Stack>
              </Stack>
            ) : (
              <Typography color="text.secondary">No trade score row yet.</Typography>
            )}
          </Section>

          <Divider />

          <Section title="Engines snapshot">
            <Stack direction="row" spacing={3} flexWrap="wrap" useFlexGap>
              <Metric
                label="Signals"
                value={
                  result.signal
                    ? `${result.signal.side.toUpperCase()} · ${result.signal.asOfDate}`
                    : "—"
                }
              />
              <Metric
                label="Liquidity Fresh"
                value={
                  result.liquidityFresh
                    ? `${result.liquidityFresh.side.toUpperCase()} · ${result.liquidityFresh.asOfDate}`
                    : "—"
                }
              />
              <Metric
                label="Confluence"
                value={
                  result.confluence
                    ? `${result.confluence.side.toUpperCase()} · ${result.confluence.asOfDate}`
                    : "—"
                }
              />
              <Metric
                label="Breakout"
                value={
                  result.breakout?.confirmed
                    ? `${result.breakout.patternType ?? "yes"} @ ${fmt(result.breakout.level20d)}`
                    : "—"
                }
              />
              <Metric
                label="Options Intraday"
                value={
                  result.optionsIntraday
                    ? `${result.optionsIntraday.contractOptionType ?? "?"} ${fmt(result.optionsIntraday.contractStrike)} · conf ${result.optionsIntraday.confidenceScore}`
                    : "—"
                }
              />
            </Stack>
          </Section>

          <Divider />

          <Section title="Sector">
            <Stack direction="row" spacing={3} flexWrap="wrap" useFlexGap>
              <Metric label="Index" value={result.sectorSymbol ?? "—"} />
              <Metric label="Name" value={result.sectorName ?? "—"} />
              <Metric
                label="Confirmed"
                value={
                  result.sectorConfirmed == null
                    ? "—"
                    : result.sectorConfirmed
                      ? "Yes"
                      : "No"
                }
              />
            </Stack>
          </Section>

          {result.backtestSummary && result.backtestSummary.timesInStrategy > 0 && (
            <>
              <Divider />
              <Section title="Backtest (journal)">
                <Stack direction="row" spacing={3} flexWrap="wrap" useFlexGap>
                  <Metric label="Setups" value={String(result.backtestSummary.timesInStrategy)} />
                  <Metric label="Targets" value={String(result.backtestSummary.targetHits)} />
                  <Metric label="SL hits" value={String(result.backtestSummary.slHits)} />
                  <Metric
                    label="Hit rate %"
                    value={fmt(result.backtestSummary.targetHitRatePct)}
                  />
                  <Metric label="Avg R:R" value={fmt(result.backtestSummary.avgRiskReward)} />
                  <Metric label="Avg R" value={fmt(result.backtestSummary.avgRMultiple)} />
                </Stack>
              </Section>
            </>
          )}

          {result.recentBars.length > 0 && (
            <>
              <Divider />
              <Section title="Recent daily bars">
                <Box
                  component="table"
                  sx={{
                    width: "100%",
                    borderCollapse: "collapse",
                    fontSize: 13,
                    "& th, & td": { textAlign: "right", py: 0.5, px: 1 },
                    "& th:first-of-type, & td:first-of-type": { textAlign: "left" },
                  }}
                >
                  <thead>
                    <tr>
                      <th>Date</th>
                      <th>O</th>
                      <th>H</th>
                      <th>L</th>
                      <th>C</th>
                      <th>Vol</th>
                    </tr>
                  </thead>
                  <tbody>
                    {result.recentBars.slice(0, 8).map((b) => (
                      <tr key={b.tradeDate}>
                        <td>{b.tradeDate}</td>
                        <td>{fmt(b.open)}</td>
                        <td>{fmt(b.high)}</td>
                        <td>{fmt(b.low)}</td>
                        <td>{fmt(b.close)}</td>
                        <td>{b.volume.toLocaleString()}</td>
                      </tr>
                    ))}
                  </tbody>
                </Box>
              </Section>
            </>
          )}
        </Box>
      )}
    </Stack>
  );
}
