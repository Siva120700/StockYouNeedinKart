import { useCallback, useEffect, useMemo, useState } from "react";
import {
  Alert,
  Button,
  FormControlLabel,
  IconButton,
  Switch,
  Stack as MuiStack,
  Tab,
  Tabs,
} from "@mui/material";
import { CaretDown, CaretRight, Play, Handshake, FilePdf, FileXls } from "@phosphor-icons/react";
import { ActionFactory, DataFactory } from "../api/factories";
import type { MomentumFuturesSuggestion, MomentumSignal } from "../api/types";
import { columnFactories } from "../zen_components/table/columnFactories";
import type { ColumnConfig } from "../zen_components/table/columnTypes";
import ZenTable from "../zen_components/table/ZenTable";
import { useZenPrimaryLayoutContext } from "../zen_components/layout/ZenPrimaryLayoutProvider";
import PageFrame, { TablePane } from "../zen_components/layout/PageFrame";
import { DEFAULT_SMALL_ICON_SIZE } from "../constants";
import {
  downloadExcelTable,
  downloadPdfTable,
  exportStamp,
  type ExportColumn,
} from "../utils/exportTable";
import { createSectorRsColumn } from "../utils/sectorRelativeStrength.tsx";
import {
  isSignalDayTraded,
  markSignalDayTraded,
  syncSignalDayHistory,
  unmarkSignalDayTraded,
  type SignalDayEntry,
} from "../utils/signalDayHistory";
import TradedDeleteBar from "../zen_components/shared/TradedDeleteBar";
import MomentumFuturesDetailPanel from "../components/MomentumFuturesDetailPanel";
import { formatMomentumScore, MIN_MOMENTUM_AVERAGE_SCORE, momentumTierLabel } from "../utils/momentumScore";

export type MomentumRuleset = "v2" | "v3";
type MomentumTab = "active" | "history" | "traded";

function formatTarget(row: MomentumSignal, target: number | null | undefined) {
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

function formatSl(row: MomentumSignal) {
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

function formatTime(iso: string | null | undefined) {
  if (!iso) return "";
  try {
    return new Date(iso).toLocaleTimeString("en-IN", {
      timeZone: "Asia/Kolkata",
      hour: "2-digit",
      minute: "2-digit",
    });
  } catch {
    return "";
  }
}

export default function MomentumSignalsPage({
  ruleset = "v2",
}: {
  ruleset?: MomentumRuleset;
}) {
  const { setTitle, setBreadcrumbs, setPageActions, setIsSyncing } =
    useZenPrimaryLayoutContext();
  const [rows, setRows] = useState<MomentumSignal[]>([]);
  const [historyRows, setHistoryRows] = useState<SignalDayEntry<MomentumSignal>[]>([]);
  const [tradedRows, setTradedRows] = useState<SignalDayEntry<MomentumSignal>[]>([]);
  const [tab, setTab] = useState<MomentumTab>("active");
  const [selectedTradedIds, setSelectedTradedIds] = useState<string[]>([]);
  const [loading, setLoading] = useState(true);
  const [running, setRunning] = useState(false);
  const [sectorCheck, setSectorCheck] = useState(false);
  const [freshCrossCheck, setFreshCrossCheck] = useState(false);
  const [hideLaggingRs, setHideLaggingRs] = useState(false);
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [futuresCache, setFuturesCache] = useState<Record<string, MomentumFuturesSuggestion>>({});
  const [futuresLoadingId, setFuturesLoadingId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);

  const pageTitle = ruleset === "v3" ? "Momentum V3" : "Momentum V2";
  const exportBase = ruleset === "v3" ? "momentum-v3" : "momentum-v2";
  const historyScope = `momentum.${ruleset}`;
  const scoreHeader = ruleset === "v3" ? "Mom V3" : "Mom V2";

  const exportColumns: ExportColumn<MomentumSignal>[] = useMemo(
    () => [
      { header: "Symbol", value: (r) => r.appSymbol },
      { header: scoreHeader, value: (r) => `${r.momentumScore.toFixed(1)} ${momentumTierLabel(r.momentumScore)}` },
      { header: "Side", value: (r) => (r.side === "sell" ? "SELL" : "BUY") },
      { header: "Entry", value: (r) => r.entryPrice },
      { header: "SL", value: (r) => formatSl(r) },
      { header: "T1", value: (r) => formatTarget(r, r.targetT1) },
      { header: "T2", value: (r) => formatTarget(r, r.targetT2) },
      { header: "T3", value: (r) => formatTarget(r, r.targetT3) },
      { header: "Sector", value: (r) => (r.sectorConfirmed ? "Yes" : "No") },
      { header: "Fresh", value: (r) => (r.freshCross ? "Yes" : "No") },
    ],
    [scoreHeader],
  );

  async function refresh() {
    setError(null);
    setIsSyncing(true);
    try {
      const list = await DataFactory.momentumSignals(undefined, ruleset);
      setRows(list);
      const synced = syncSignalDayHistory(historyScope, list);
      setHistoryRows(synced.history);
      setTradedRows(synced.traded);
      setFuturesCache({});
      setExpandedId(null);
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
    setInfo(null);
    try {
      await ActionFactory.runMomentumAnalysis(ruleset);
      await refresh();
      setInfo(`Momentum ${ruleset.toUpperCase()} run complete.`);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setRunning(false);
    }
  }

  async function onTrade(row: MomentumSignal) {
    setError(null);
    setInfo(null);
    try {
      await ActionFactory.openPositionFromMomentumSignal(row.id);
      markSignalDayTraded(historyScope, row);
      setInfo(`${row.appSymbol} moved to Positions (Traded).`);
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  }

  function tradedRowId(r: MomentumSignal) {
    return `${r.instrumentId}:${r.side}:${r.id}`;
  }

  function onDeleteSelectedTraded() {
    const selected = tradedRows.filter((r) => selectedTradedIds.includes(tradedRowId(r)));
    if (selected.length === 0) return;
    unmarkSignalDayTraded(historyScope, selected);
    setSelectedTradedIds([]);
    const synced = syncSignalDayHistory(historyScope, rows);
    setHistoryRows(synced.history);
    setTradedRows(synced.traded);
    setInfo(`Removed ${selected.length} from Traded.`);
  }

  const filteredActive = useMemo(() => {
    let list = rows;
    if (ruleset === "v2") {
      list = list.filter((r) => r.momentumScore >= MIN_MOMENTUM_AVERAGE_SCORE);
    }
    if (sectorCheck) list = list.filter((r) => r.sectorConfirmed);
    if (freshCrossCheck) list = list.filter((r) => r.freshCross);
    if (hideLaggingRs) list = list.filter((r) => !r.sectorRs?.downranked);
    return [...list].sort((a, b) => b.momentumScore - a.momentumScore);
  }, [rows, ruleset, sectorCheck, freshCrossCheck, hideLaggingRs]);

  const loadFutures = useCallback(async (row: MomentumSignal) => {
    setFuturesLoadingId(row.id);
    try {
      const suggestion = await DataFactory.momentumFuturesSuggestion(row);
      setFuturesCache((prev) => ({ ...prev, [row.id]: suggestion }));
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setFuturesLoadingId(null);
    }
  }, []);

  const toggleExpand = useCallback(
    (row: MomentumSignal) => {
      setExpandedId((prev) => {
        const next = prev === row.id ? null : row.id;
        if (next && !futuresCache[row.id]) void loadFutures(row);
        return next;
      });
    },
    [loadFutures, futuresCache],
  );

  const tableRows: MomentumSignal[] = useMemo(() => {
    if (tab === "history") return historyRows;
    if (tab === "traded") return tradedRows;
    return filteredActive;
  }, [tab, filteredActive, historyRows, tradedRows]);

  useEffect(() => {
    setTitle(pageTitle);
    setBreadcrumbs([{ label: "Home" }, { label: pageTitle }]);
    void refresh();
    return () => setPageActions(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ruleset]);

  useEffect(() => {
    const exportDisabled = loading || tableRows.length === 0;
    setPageActions(
      <MuiStack direction="row" spacing={1} alignItems="center" flexWrap="wrap">
        {tab === "active" ? (
          <>
            <FormControlLabel
              control={
                <Switch size="small" checked={sectorCheck} onChange={(e) => setSectorCheck(e.target.checked)} />
              }
              label="Sector check"
              sx={{ mr: 1 }}
            />
            <FormControlLabel
              control={
                <Switch
                  size="small"
                  checked={freshCrossCheck}
                  onChange={(e) => setFreshCrossCheck(e.target.checked)}
                />
              }
              label="Fresh cross"
              sx={{ mr: 1 }}
            />
            <FormControlLabel
              control={
                <Switch
                  size="small"
                  checked={hideLaggingRs}
                  onChange={(e) => setHideLaggingRs(e.target.checked)}
                />
              }
              label="Hide lagging RS"
              sx={{ mr: 1 }}
            />
          </>
        ) : null}
        <Button
          variant="outlined"
          size="small"
          disabled={exportDisabled}
          startIcon={<FileXls size={DEFAULT_SMALL_ICON_SIZE} />}
          onClick={() =>
            downloadExcelTable({
              sheetName: pageTitle,
              fileName: exportStamp(exportBase, "xlsx"),
              columns: exportColumns,
              rows: tableRows,
            })
          }
        >
          Excel
        </Button>
        <Button
          variant="outlined"
          size="small"
          disabled={exportDisabled}
          startIcon={<FilePdf size={DEFAULT_SMALL_ICON_SIZE} />}
          onClick={() =>
            downloadPdfTable({
              title: pageTitle,
              fileName: exportStamp(exportBase, "pdf"),
              columns: exportColumns,
              rows: tableRows,
            })
          }
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
          {running ? "Running…" : "Run analysis"}
        </Button>
      </MuiStack>,
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ruleset, tab, loading, running, sectorCheck, freshCrossCheck, hideLaggingRs, tableRows, pageTitle]);

  const columns = useMemo(() => {
    const base: ColumnConfig<MomentumSignal>[] = [];

    if (tab === "active") {
      base.push(
        columnFactories.createTextColumn<MomentumSignal>({
          field: "_expand",
          headerName: "",
          width: 44,
          sortable: false,
          getValue: () => "",
          displayRenderer: (_v, row) => (
            <IconButton
              size="small"
              aria-label={expandedId === row.id ? "Collapse futures" : "Expand futures"}
              onClick={(e) => {
                e.stopPropagation();
                toggleExpand(row);
              }}
            >
              {expandedId === row.id ? (
                <CaretDown size={DEFAULT_SMALL_ICON_SIZE} />
              ) : (
                <CaretRight size={DEFAULT_SMALL_ICON_SIZE} />
              )}
            </IconButton>
          ),
        }),
      );
    }

    base.push(
      columnFactories.createTextColumn<MomentumSignal>({
        field: "appSymbol",
        headerName: "Symbol",
        width: 120,
        getValue: (r) => r.appSymbol,
      }),
      columnFactories.createTextColumn<MomentumSignal>({
        field: "momentumScore",
        headerName: scoreHeader,
        width: 130,
        getValue: (r) => r.momentumScore,
        displayRenderer: (v) =>
          v != null && Number.isFinite(Number(v)) ? formatMomentumScore(Number(v)) : "—",
      }),
      createSectorRsColumn<MomentumSignal>((r) => r.sectorRs),
      columnFactories.createStatusColumn<MomentumSignal>(
        {
          buy: { label: "BUY", color: "#2e7d32" },
          sell: { label: "SELL", color: "#c62828" },
        },
        {
          field: "side",
          headerName: "Side",
          width: 100,
          getValue: (r) => r.side,
        },
      ),
      columnFactories.createNumberColumn<MomentumSignal>({
        field: "entryPrice",
        headerName: "Entry",
        width: 110,
        minDecimalPlaces: 2,
        getValue: (r) => r.entryPrice,
      }),
      columnFactories.createTextColumn<MomentumSignal>({
        field: "initialStopLoss",
        headerName: "SL",
        width: 150,
        getValue: (r) => formatSl(r),
      }),
      columnFactories.createTextColumn<MomentumSignal>({
        field: "targetT1",
        headerName: "T1",
        width: 150,
        getValue: (r) => formatTarget(r, r.targetT1),
      }),
      columnFactories.createTextColumn<MomentumSignal>({
        field: "targetT2",
        headerName: "T2",
        width: 150,
        getValue: (r) => formatTarget(r, r.targetT2),
      }),
      columnFactories.createTextColumn<MomentumSignal>({
        field: "targetT3",
        headerName: "T3",
        width: 150,
        getValue: (r) => formatTarget(r, r.targetT3),
      }),
      columnFactories.createBooleanColumn<MomentumSignal>({
        field: "sectorConfirmed",
        headerName: "Sector",
        width: 80,
        getValue: (r) => r.sectorConfirmed,
      }),
      columnFactories.createBooleanColumn<MomentumSignal>({
        field: "freshCross",
        headerName: "Fresh",
        width: 80,
        getValue: (r) => r.freshCross,
      }),
      columnFactories.createBooleanColumn<MomentumSignal>({
        field: "volumeOk",
        headerName: "Vol OK",
        width: 90,
        getValue: (r) => r.volumeOk,
      }),
    );

    if (tab === "history") {
      base.push(
        columnFactories.createTextColumn<MomentumSignal>({
          field: "disappearedAt",
          headerName: "Left",
          width: 90,
          getValue: (r) => formatTime((r as SignalDayEntry<MomentumSignal>).disappearedAt),
        }),
      );
    }

    if (tab === "traded") {
      base.push(
        columnFactories.createTextColumn<MomentumSignal>({
          field: "tradedAt",
          headerName: "Traded",
          width: 90,
          getValue: (r) => formatTime((r as SignalDayEntry<MomentumSignal>).tradedAt),
        }),
      );
    }

    if (tab !== "traded") {
      base.push(
        columnFactories.createActionColumn<MomentumSignal>(
          (row) => [
            {
              icon: <Handshake size={DEFAULT_SMALL_ICON_SIZE} />,
              tooltip: isSignalDayTraded(historyScope, row)
                ? "Already traded"
                : "Trade — open in Positions",
              disabled: () => isSignalDayTraded(historyScope, row),
              onClick: (r) => void onTrade(r),
            },
          ],
          { headerName: "", width: 72 },
        ),
      );
    }

    return base;
  }, [tab, historyScope, scoreHeader, expandedId, toggleExpand]);

  return (
    <PageFrame>
      {error ? <Alert severity="error">{error}</Alert> : null}
      {info ? <Alert severity="info">{info}</Alert> : null}
      <Alert severity="info" variant="outlined">
        {ruleset === "v3"
          ? "Jegadeesh–Titman multi-horizon momentum (12–1 / 6–1 / 3–1 + RS vs Nifty). All scored breakouts are ranked /10. Click ▸ for nearest FUTSTK entry / exit / targets."
          : "StepOne-style composite (trend, returns, RVOL, RSI, ATR breakout, candle strength). Average tier and above (score ≥ 4). Click ▸ for futures contract details."}
      </Alert>
      <Tabs value={tab} onChange={(_, v) => setTab(v as MomentumTab)}>
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
        <ZenTable
          fillHeight
          rows={tableRows}
          columns={columns}
          loading={loading}
          enableSearch
          searchPlaceholder="Filter symbols…"
          getRowId={(r) => (tab === "traded" ? tradedRowId(r) : r.id)}
          enableSelection={tab === "traded"}
          selectedRowIds={tab === "traded" ? selectedTradedIds : undefined}
          onSelectedRowIdsChange={tab === "traded" ? setSelectedTradedIds : undefined}
          onRowClick={tab === "active" ? (r) => toggleExpand(r) : undefined}
          expandedRowId={tab === "active" ? expandedId : null}
          renderExpandedRow={
            tab === "active"
              ? (row) => (
                  <MomentumFuturesDetailPanel
                    signal={row}
                    futures={futuresCache[row.id] ?? null}
                    loading={futuresLoadingId === row.id}
                  />
                )
              : undefined
          }
        />
      </TablePane>
    </PageFrame>
  );
}
