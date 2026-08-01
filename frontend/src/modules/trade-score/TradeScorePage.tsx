import { useEffect, useMemo, useState } from "react";
import {
  Alert,
  Box,
  Button,
  Chip,
  FormControlLabel,
  LinearProgress,
  Stack,
  Switch,
  Typography,
} from "@mui/material";
import { ArrowSquareOut, Play } from "@phosphor-icons/react";
import { columnFactories } from "../../zen_components/table/columnFactories";
import ZenTable from "../../zen_components/table/ZenTable";
import { useZenPrimaryLayoutContext } from "../../zen_components/layout/ZenPrimaryLayoutProvider";
import { DEFAULT_SMALL_ICON_SIZE } from "../../constants";
import { TradeScoreApi } from "./api";
import type { TradeConfidenceScore } from "./types";
import { ratingLabel } from "./types";
import {
  createHistoricalHitRateColumn,
  loadHistoricalHitRates,
  type HitRateByInstrument,
} from "../../utils/historicalHitRate";

function riskReward(row: TradeConfidenceScore): number | null {
  const entry = Number(row.entryPrice);
  const sl = Number(row.initialStopLoss);
  const t1 = Number(row.targetT1);
  if (![entry, sl, t1].every(Number.isFinite) || entry === 0) return null;
  const risk = row.side === "sell" ? sl - entry : entry - sl;
  const reward = row.side === "sell" ? entry - t1 : t1 - entry;
  if (risk <= 0 || reward <= 0) return null;
  return reward / risk;
}

export default function TradeScorePage() {
  const { setTitle, setBreadcrumbs, setPageActions, setIsSyncing } =
    useZenPrimaryLayoutContext();
  const [rows, setRows] = useState<TradeConfidenceScore[]>([]);
  const [hitRates, setHitRates] = useState<HitRateByInstrument>(() => new Map());
  const [loading, setLoading] = useState(true);
  const [running, setRunning] = useState(false);
  const [minScore, setMinScore] = useState(60);
  const [error, setError] = useState<string | null>(null);

  async function refresh() {
    setError(null);
    setIsSyncing(true);
    try {
      const [scores, rates] = await Promise.all([
        TradeScoreApi.fetchScores(),
        loadHistoricalHitRates("trade_score"),
      ]);
      setRows(scores);
      setHitRates(rates);
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
      await TradeScoreApi.runAnalysis(true, true);
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setRunning(false);
    }
  }

  const visibleRows = useMemo(
    () => rows.filter((r) => r.confidenceScore >= minScore),
    [rows, minScore],
  );

  useEffect(() => {
    setTitle("Trade Score");
    setBreadcrumbs([{ label: "Home" }, { label: "Trade Score" }]);
    void refresh();
    return () => setPageActions(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    setPageActions(
      <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap">
        <FormControlLabel
          control={
            <Switch
              size="small"
              checked={minScore >= 75}
              onChange={(e) => setMinScore(e.target.checked ? 75 : 60)}
            />
          }
          label="Min ★★★★ (75+)"
        />
        <Button
          variant="contained"
          size="small"
          disabled={running}
          startIcon={<Play size={DEFAULT_SMALL_ICON_SIZE} />}
          onClick={() => void onRun()}
        >
          {running ? "Scoring…" : "Run trade score"}
        </Button>
      </Stack>,
    );
  }, [running, minScore, setPageActions]);

  const columns = useMemo(
    () => [
      columnFactories.createNumberColumn<TradeConfidenceScore>({
        field: "confidenceScore",
        headerName: "Score",
        width: 80,
        minDecimalPlaces: 0,
        getValue: (r) => r.confidenceScore,
      }),
      columnFactories.createTextColumn<TradeConfidenceScore>({
        field: "rating",
        headerName: "Rating",
        width: 150,
        getValue: (r) => ratingLabel(r.rating),
      }),
      columnFactories.createTextColumn<TradeConfidenceScore>({
        field: "appSymbol",
        headerName: "Symbol",
        width: 110,
        getValue: (r) => r.appSymbol,
      }),
      createHistoricalHitRateColumn<TradeConfidenceScore>(hitRates, (r) => r.instrumentId),
      columnFactories.createStatusColumn<TradeConfidenceScore>(
        {
          buy: { label: "BUY", color: "#2e7d32" },
          sell: { label: "SELL", color: "#c62828" },
        },
        { field: "side", headerName: "Side", width: 90, getValue: (r) => r.side },
      ),
      columnFactories.createNumberColumn<TradeConfidenceScore>({
        field: "entryPrice",
        headerName: "Entry",
        width: 100,
        minDecimalPlaces: 2,
        getValue: (r) => r.entryPrice,
      }),
      columnFactories.createNumberColumn<TradeConfidenceScore>({
        field: "initialStopLoss",
        headerName: "SL",
        width: 100,
        minDecimalPlaces: 2,
        getValue: (r) => r.initialStopLoss,
      }),
      columnFactories.createTextColumn<TradeConfidenceScore>({
        field: "targetT1",
        headerName: "T1",
        width: 100,
        getValue: (r) => (r.targetT1 != null ? Number(r.targetT1).toFixed(2) : ""),
      }),
      columnFactories.createTextColumn<TradeConfidenceScore>({
        field: "rr",
        headerName: "R:R",
        width: 70,
        getValue: (r) => {
          const rr = riskReward(r);
          return rr != null ? rr.toFixed(2) : "";
        },
      }),
      columnFactories.createTextColumn<TradeConfidenceScore>({
        field: "layers",
        headerName: "Layers",
        width: 200,
        getValue: (r) =>
          `S${r.signalsScore} L${r.liquidityScore} B${r.breakoutScore} F${r.futuresScore} O${r.optionsScore}`,
      }),
      columnFactories.createActionColumn<TradeConfidenceScore>(
        () => [
          {
            icon: <ArrowSquareOut size={DEFAULT_SMALL_ICON_SIZE} />,
            tooltip: "Open position",
            onClick: (r) => void TradeScoreApi.openPosition(r.id),
          },
        ],
        { field: "actions", headerName: "", width: 72 },
      ),
    ],
    [hitRates],
  );

  return (
    <>
      {error ? (
        <Alert severity="error" sx={{ mb: 2 }}>
          {error}
        </Alert>
      ) : null}
      <Alert severity="info" sx={{ mb: 2 }}>
        Separate high-probability engine. Primary <strong>Signals</strong> (40%) +{" "}
        <strong>Liquidity Fresh</strong> (20%) + <strong>Quality Breakout</strong> (20%).
        F&amp;O layers (20%) — Phase 3–4. SL = tighter stop, entries within 0.2%.
        Existing Signals / Liquidity pages are unchanged.
      </Alert>
      {running ? <LinearProgress sx={{ mb: 2 }} /> : null}
      {visibleRows.length > 0 ? (
        <Box sx={{ mb: 2 }}>
          <Typography variant="subtitle2" sx={{ mb: 1 }}>
            Top pick
          </Typography>
          <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
            {visibleRows.slice(0, 3).map((r) => (
              <Chip
                key={r.id}
                label={`${r.appSymbol} ${r.side.toUpperCase()} · ${r.confidenceScore}% · ${ratingLabel(r.rating)}`}
                color={r.confidenceScore >= 90 ? "success" : "default"}
                variant="outlined"
              />
            ))}
          </Stack>
          {visibleRows[0]?.reasons?.length ? (
            <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
              {visibleRows[0].reasons.map((x) => `✓ ${x}`).join(" · ")}
            </Typography>
          ) : null}
        </Box>
      ) : null}
      <ZenTable
        columns={columns}
        rows={visibleRows}
        getRowId={(r) => r.id}
        loading={loading}
        enableSearch
        searchPlaceholder="Search symbol…"
        emptyMessage="No trade scores yet. Run trade score (refreshes Signals + Liquidity Fresh, then scores)."
      />
    </>
  );
}
