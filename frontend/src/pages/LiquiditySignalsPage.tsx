import { useEffect, useMemo, useState } from "react";
import { Alert, Button, FormControlLabel, Switch, Stack as MuiStack } from "@mui/material";
import { Play, ArrowSquareOut, FilePdf, FileXls } from "@phosphor-icons/react";
import { ActionFactory, DataFactory } from "../api/factories";
import type { LiquiditySignal } from "../api/types";
import { columnFactories } from "../zen_components/table/columnFactories";
import ZenTable from "../zen_components/table/ZenTable";
import { useZenPrimaryLayoutContext } from "../zen_components/layout/ZenPrimaryLayoutProvider";
import { DEFAULT_SMALL_ICON_SIZE } from "../constants";
import {
  downloadExcelTable,
  downloadPdfTable,
  exportStamp,
  type ExportColumn,
} from "../utils/exportTable";
import {
  createHistoricalHitRateColumn,
  formatHitRatePct,
  loadHistoricalHitRates,
  type HitRateByInstrument,
} from "../utils/historicalHitRate";

type ScoredLiquiditySignal = LiquiditySignal & { score: number };

function formatTarget(row: LiquiditySignal, target: number | null | undefined) {
  if (target == null || !Number.isFinite(Number(target)) || !row.entryPrice) return "";
  const t = Number(target);
  const entry = Number(row.entryPrice);
  if (entry === 0) return t.toFixed(2);
  const pct =
    row.side === "sell" ? ((entry - t) / entry) * 100 : ((t - entry) / entry) * 100;
  return `${t.toLocaleString("en-IN", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })} (${pct >= 0 ? "+" : ""}${pct.toFixed(2)}%)`;
}

function formatSl(row: LiquiditySignal) {
  const sl = row.initialStopLoss;
  if (sl == null || !Number.isFinite(Number(sl)) || !row.entryPrice) return "";
  const s = Number(sl);
  const entry = Number(row.entryPrice);
  if (entry === 0) return s.toFixed(2);
  const pct = ((s - entry) / entry) * 100;
  return `${s.toLocaleString("en-IN", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })} (${pct >= 0 ? "+" : ""}${pct.toFixed(2)}%)`;
}

const liquidityExportBase: ExportColumn<ScoredLiquiditySignal>[] = [
  { header: "Score", value: (r) => r.score },
  { header: "Symbol", value: (r) => r.appSymbol },
  { header: "Side", value: (r) => (r.side === "sell" ? "SELL" : "BUY") },
  { header: "Entry", value: (r) => r.entryPrice },
  { header: "SL", value: (r) => formatSl(r) },
  { header: "T1", value: (r) => formatTarget(r, r.targetT1) },
  { header: "T2", value: (r) => formatTarget(r, r.targetT2) },
  { header: "T3", value: (r) => formatTarget(r, r.targetT3) },
  {
    header: "RVOL",
    value: (r) =>
      `${Number(r.relativeVolume).toFixed(2)} (${Math.round(Number(r.rvolPercentile) * 100)}%)`,
  },
  {
    header: "Sweep",
    value: (r) =>
      r.sweptZoneType
        ? `${r.sweptZoneType}${r.sweptZonePrice != null ? ` @ ${Number(r.sweptZonePrice).toFixed(1)}` : ""}`
        : "",
  },
  {
    header: "Near zone",
    value: (r) => {
      if (!r.nearestZoneType) return "";
      const dist =
        r.distancePct != null ? ` ${(Number(r.distancePct) * 100).toFixed(2)}%` : "";
      return `${r.nearestZoneType}${dist}`;
    },
  },
  { header: "Strong", value: (r) => (r.strongClose ? "Yes" : "No") },
  { header: "Sector", value: (r) => (r.sectorConfirmed ? "Yes" : "No") },
];

export type LiquidityRuleset = "classic" | "fresh" | "v2";

export default function LiquiditySignalsPage({
  ruleset = "classic",
}: {
  ruleset?: LiquidityRuleset;
}) {
  const { setTitle, setBreadcrumbs, setPageActions, setIsSyncing } =
    useZenPrimaryLayoutContext();
  const [rows, setRows] = useState<LiquiditySignal[]>([]);
  const [hitRates, setHitRates] = useState<HitRateByInstrument>(() => new Map());
  const [loading, setLoading] = useState(true);
  const [running, setRunning] = useState(false);
  const [sectorCheck, setSectorCheck] = useState(false);
  const [riskRewardCheck, setRiskRewardCheck] = useState(false);
  const [requireRetest, setRequireRetest] = useState(false);
  const [requireRelativeStrength, setRequireRelativeStrength] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const isV2 = ruleset === "v2";
  const pageTitle =
    ruleset === "fresh" ? "Liquidity Fresh" : ruleset === "v2" ? "Liquidity V2" : "Liquidity";
  const exportBase =
    ruleset === "fresh" ? "liquidity-fresh" : ruleset === "v2" ? "liquidity-v2" : "liquidity";
  const strategyKey =
    ruleset === "fresh" ? "liquidity_fresh" : ruleset === "v2" ? "liquidity_v2" : "liquidity";

  const liquidityExportColumns: ExportColumn<ScoredLiquiditySignal>[] = useMemo(
    () => [
      liquidityExportBase[0]!,
      liquidityExportBase[1]!,
      {
        header: "Hit %",
        value: (r) => formatHitRatePct(hitRates.get(r.instrumentId)),
      },
      ...(isV2
        ? ([
            { header: "Confidence", value: (r: ScoredLiquiditySignal) => r.confidenceRating ?? "" },
            { header: "Sweep str", value: (r: ScoredLiquiditySignal) => r.sweepStrength ?? "" },
          ] as ExportColumn<ScoredLiquiditySignal>[])
        : []),
      ...liquidityExportBase.slice(2),
    ],
    [hitRates, isV2],
  );

  async function refresh() {
    setError(null);
    setIsSyncing(true);
    try {
      const [signals, rates] = await Promise.all([
        DataFactory.liquiditySignals(undefined, ruleset),
        loadHistoricalHitRates(strategyKey),
      ]);
      setRows(signals);
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
      await ActionFactory.runLiquidityAnalysis(
        ruleset,
        isV2
          ? { requireRetest, requireRelativeStrength }
          : undefined,
      );
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setRunning(false);
    }
  }

  async function onOpen(signalId: string) {
    try {
      await ActionFactory.openPositionFromLiquiditySignal(signalId);
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  }

  /** Risk-reward vs T1: reward/risk. Buy (T1-entry)/(entry-SL); sell (entry-T1)/(SL-entry). */
  function riskRewardRatio(row: LiquiditySignal): number | null {
    const entry = Number(row.entryPrice);
    const sl = Number(row.initialStopLoss);
    const target = Number(row.targetT1 ?? row.targetT2 ?? row.targetT3);
    if (![entry, sl, target].every((n) => Number.isFinite(n)) || entry === 0) return null;
    const risk = row.side === "sell" ? sl - entry : entry - sl;
    const reward = row.side === "sell" ? entry - target : target - entry;
    if (risk <= 0 || reward <= 0) return null;
    return reward / risk;
  }

  /**
   * Quality score 0–100: higher = stronger liquidity setup.
   * V2 uses server-side qualityScore; classic/fresh keep client heuristic.
   */
  function liquidityScore(row: LiquiditySignal): number {
    if (isV2 && row.qualityScore != null && Number.isFinite(Number(row.qualityScore))) {
      return Math.round(Number(row.qualityScore));
    }

    let score = 0;

    const pctile = Number(row.rvolPercentile);
    if (Number.isFinite(pctile)) {
      score += Math.min(1, Math.max(0, pctile)) * 25; // top of own history
    }

    const rvol = Number(row.relativeVolume);
    if (Number.isFinite(rvol) && rvol > 0) {
      score += Math.min(rvol / 3, 1) * 15; // caps at ~3×
    }

    const zone = (row.sweptZoneType ?? "").toLowerCase();
    if (zone.startsWith("equal")) score += 20;
    else if (zone.startsWith("swing")) score += 15;
    else if (zone === "pdh" || zone === "pdl") score += 12;
    else if (zone === "pwh" || zone === "pwl") score += 10;
    else if (zone === "round") score += 6;

    if (row.strongClose) score += 15;

    const rr = riskRewardRatio(row);
    if (rr != null) {
      score += Math.min(rr / 2, 1) * 20; // full points at R:R ≥ 2
    }

    const dist = Number(row.distancePct);
    if (Number.isFinite(dist)) {
      if (dist <= 0.005) score += 5;
      else if (dist <= 0.01) score += 3;
      else if (dist <= 0.02) score += 1;
    }

    return Math.round(Math.min(100, Math.max(0, score)));
  }

  const visibleRows = useMemo(() => {
    let list: ScoredLiquiditySignal[] = rows.map((r) => ({
      ...r,
      score: liquidityScore(r),
    }));
    if (sectorCheck) list = list.filter((r) => r.sectorConfirmed);
    if (riskRewardCheck) {
      list = list.filter((r) => {
        const rr = riskRewardRatio(r);
        return rr != null && rr >= 1;
      });
    }
    return list.sort((a, b) => b.score - a.score);
  }, [rows, sectorCheck, riskRewardCheck]);

  function onExportPdf() {
    downloadPdfTable({
      title: `${pageTitle} Signals`,
      fileName: exportStamp(exportBase, "pdf"),
      columns: liquidityExportColumns,
      rows: visibleRows,
    });
  }

  function onExportExcel() {
    downloadExcelTable({
      sheetName: pageTitle,
      fileName: exportStamp(exportBase, "xlsx"),
      columns: liquidityExportColumns,
      rows: visibleRows,
    });
  }

  useEffect(() => {
    setTitle(pageTitle);
    setBreadcrumbs([{ label: "Home" }, { label: pageTitle }]);
    setLoading(true);
    void refresh();
    return () => setPageActions(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ruleset]);

  useEffect(() => {
    const exportDisabled = loading || visibleRows.length === 0;
    setPageActions(
      <MuiStack direction="row" spacing={1} alignItems="center" flexWrap="wrap">
        <FormControlLabel
          control={
            <Switch
              size="small"
              checked={sectorCheck}
              onChange={(e) => setSectorCheck(e.target.checked)}
            />
          }
          label="Sector check"
          sx={{ mr: 1 }}
        />
        <FormControlLabel
          control={
            <Switch
              size="small"
              checked={riskRewardCheck}
              onChange={(e) => setRiskRewardCheck(e.target.checked)}
            />
          }
          label="R:R ≥ 1"
          sx={{ mr: 1 }}
        />
        {isV2 ? (
          <>
            <FormControlLabel
              control={
                <Switch
                  size="small"
                  checked={requireRetest}
                  onChange={(e) => setRequireRetest(e.target.checked)}
                />
              }
              label="Require retest"
              sx={{ mr: 1 }}
            />
            <FormControlLabel
              control={
                <Switch
                  size="small"
                  checked={requireRelativeStrength}
                  onChange={(e) => setRequireRelativeStrength(e.target.checked)}
                />
              }
              label="Nifty RS"
              sx={{ mr: 1 }}
            />
          </>
        ) : null}
        <Button
          variant="outlined"
          size="small"
          disabled={exportDisabled}
          startIcon={<FileXls size={DEFAULT_SMALL_ICON_SIZE} />}
          onClick={onExportExcel}
        >
          Excel
        </Button>
        <Button
          variant="outlined"
          size="small"
          disabled={exportDisabled}
          startIcon={<FilePdf size={DEFAULT_SMALL_ICON_SIZE} />}
          onClick={onExportPdf}
        >
          PDF
        </Button>
        <Button
          variant="contained"
          size="small"
          disabled={running}
          startIcon={<Play size={DEFAULT_SMALL_ICON_SIZE} />}
          onClick={() => void onRun()}
        >
          {running ? "Running…" : `Run ${pageTitle.toLowerCase()}`}
        </Button>
      </MuiStack>,
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [
    running,
    loading,
    sectorCheck,
    riskRewardCheck,
    requireRetest,
    requireRelativeStrength,
    visibleRows,
    pageTitle,
    isV2,
  ]);

  const columns = useMemo(() => {
    type Scored = LiquiditySignal & { score: number };

    return [
      columnFactories.createNumberColumn<Scored>({
        field: "score",
        headerName: "Score",
        width: 80,
        minDecimalPlaces: 0,
        getValue: (r) => r.score,
      }),
      ...(isV2
        ? [
            columnFactories.createTextColumn<Scored>({
              field: "confidenceRating",
              headerName: "Conf",
              width: 70,
              getValue: (r) => r.confidenceRating ?? "",
            }),
            columnFactories.createTextColumn<Scored>({
              field: "sweepStrength",
              headerName: "Sweep str",
              width: 90,
              getValue: (r) => r.sweepStrength ?? "",
            }),
          ]
        : []),
      columnFactories.createTextColumn<Scored>({
        field: "appSymbol",
        headerName: "Symbol",
        width: 110,
        getValue: (r) => r.appSymbol,
      }),
      createHistoricalHitRateColumn<Scored>(hitRates, (r) => r.instrumentId),
      columnFactories.createStatusColumn<Scored>(
        {
          buy: { label: "BUY", color: "#2e7d32" },
          sell: { label: "SELL", color: "#c62828" },
        },
        {
          field: "side",
          headerName: "Side",
          width: 90,
          getValue: (r) => r.side,
        },
      ),
      columnFactories.createNumberColumn<Scored>({
        field: "entryPrice",
        headerName: "Entry",
        width: 100,
        minDecimalPlaces: 2,
        getValue: (r) => r.entryPrice,
      }),
      columnFactories.createTextColumn<Scored>({
        field: "initialStopLoss",
        headerName: "SL",
        width: 130,
        getValue: (r) => formatSl(r),
      }),
      columnFactories.createTextColumn<Scored>({
        field: "targetT1",
        headerName: "T1",
        width: 130,
        getValue: (r) => formatTarget(r, r.targetT1),
      }),
      columnFactories.createTextColumn<Scored>({
        field: "targetT2",
        headerName: "T2",
        width: 130,
        getValue: (r) => formatTarget(r, r.targetT2),
      }),
      columnFactories.createTextColumn<Scored>({
        field: "targetT3",
        headerName: "T3",
        width: 130,
        getValue: (r) => formatTarget(r, r.targetT3),
      }),
      columnFactories.createTextColumn<Scored>({
        field: "relativeVolume",
        headerName: "RVOL",
        width: 90,
        getValue: (r) =>
          `${Number(r.relativeVolume).toFixed(2)} (${Math.round(Number(r.rvolPercentile) * 100)}%)`,
      }),
      columnFactories.createTextColumn<Scored>({
        field: "sweptZoneType",
        headerName: "Sweep",
        width: 120,
        getValue: (r) =>
          r.sweptZoneType
            ? `${r.sweptZoneType}${r.sweptZonePrice != null ? ` @ ${Number(r.sweptZonePrice).toFixed(1)}` : ""}`
            : "",
      }),
      columnFactories.createTextColumn<Scored>({
        field: "nearestZoneType",
        headerName: "Near zone",
        width: 120,
        getValue: (r) => {
          if (!r.nearestZoneType) return "";
          const dist =
            r.distancePct != null ? ` ${((Number(r.distancePct) * 100).toFixed(2))}%` : "";
          return `${r.nearestZoneType}${dist}`;
        },
      }),
      columnFactories.createBooleanColumn<Scored>({
        field: "strongClose",
        headerName: "Strong",
        width: 80,
        getValue: (r) => r.strongClose,
      }),
      columnFactories.createActionColumn<Scored>(
        () => [
          {
            icon: <ArrowSquareOut size={DEFAULT_SMALL_ICON_SIZE} />,
            tooltip: "Open position",
            onClick: (r) => void onOpen(r.id),
          },
        ],
        { field: "actions", headerName: "", width: 72 },
      ),
    ];
  }, [hitRates, isV2]);

  return (
    <>
      {error ? (
        <Alert severity="error" sx={{ mb: 2 }}>
          {error}
        </Alert>
      ) : null}
      <ZenTable
        columns={columns}
        rows={visibleRows}
        getRowId={(r) => r.id}
        loading={loading}
        enableSearch
        searchPlaceholder="Search symbol or name…"
        emptyMessage={
          sectorCheck || riskRewardCheck
            ? `No ${pageTitle.toLowerCase()} signals match the active filters. Turn filters off, or Run again.`
            : ruleset === "fresh"
              ? "No liquidity fresh signals. Click Run (stricter confirm window + skip spent T1)."
              : ruleset === "v2"
                ? "No liquidity V2 signals. Click Run (ATR/HTF/quality filters)."
                : "No liquidity signals. Click Run liquidity (needs 1H bars + 4H sweep + 1H confirm)."
        }
      />
    </>
  );
}
