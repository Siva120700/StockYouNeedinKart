import { Fragment, useEffect, useMemo, useState, type ReactNode } from "react";
import {
  Box,
  Checkbox,
  CircularProgress,
  IconButton,
  InputAdornment,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  TableSortLabel,
  TextField,
  Typography,
} from "@mui/material";
import { MagnifyingGlass, X } from "@phosphor-icons/react";
import type { ColumnConfig } from "./columnTypes";
import ZenPagination from "../shared/ZenPagination";
import { DEFAULT_SMALL_ICON_SIZE } from "../../constants";

export type ZenTableProps<T> = {
  columns: ColumnConfig<T>[];
  rows: T[];
  getRowId: (row: T) => string;
  loading?: boolean;
  emptyMessage?: string;
  onRowClick?: (row: T) => void;
  /** When set with renderExpandedRow, a detail panel opens under that row. */
  expandedRowId?: string | null;
  renderExpandedRow?: (row: T) => ReactNode;
  dense?: boolean;
  /** Client-side pagination (default true when rows.length > pageSize). */
  enablePagination?: boolean;
  defaultPageSize?: number;
  /** Fill parent height and scroll rows inside the table body. */
  fillHeight?: boolean;
  /** Show a search box that filters rows by any column value. */
  enableSearch?: boolean;
  searchPlaceholder?: string;
  /** Controlled search (optional). */
  search?: string;
  onSearchChange?: (value: string) => void;
  /** Notify parent of filtered/sorted rows (e.g. for aggregate stats). */
  onVisibleRowsChange?: (rows: T[]) => void;
  /** Row checkboxes for multi-select (e.g. Traded tab delete). */
  enableSelection?: boolean;
  selectedRowIds?: string[];
  onSelectedRowIdsChange?: (ids: string[]) => void;
};

type SortDir = "asc" | "desc";

function compareValues(a: unknown, b: unknown): number {
  if (a == null && b == null) return 0;
  if (a == null) return -1;
  if (b == null) return 1;

  if (typeof a === "number" && typeof b === "number") {
    if (Number.isNaN(a) && Number.isNaN(b)) return 0;
    if (Number.isNaN(a)) return -1;
    if (Number.isNaN(b)) return 1;
    return a - b;
  }

  const an = typeof a === "string" && a.trim() !== "" && !Number.isNaN(Number(a)) ? Number(a) : null;
  const bn = typeof b === "string" && b.trim() !== "" && !Number.isNaN(Number(b)) ? Number(b) : null;
  if (an != null && bn != null) return an - bn;

  return String(a).localeCompare(String(b), undefined, { numeric: true, sensitivity: "base" });
}

function rowMatchesSearch<T>(row: T, columns: ColumnConfig<T>[], query: string): boolean {
  const q = query.trim().toLowerCase();
  if (!q) return true;
  return columns.some((col) => {
    const value = col.getValue(row);
    if (value == null) return false;
    return String(value).toLowerCase().includes(q);
  });
}

export function ZenTable<T>({
  columns,
  rows,
  getRowId,
  loading = false,
  emptyMessage = "No rows",
  onRowClick,
  expandedRowId = null,
  renderExpandedRow,
  dense = true,
  enablePagination = true,
  defaultPageSize = 25,
  fillHeight = false,
  enableSearch = false,
  searchPlaceholder = "Search…",
  search: controlledSearch,
  onSearchChange,
  onVisibleRowsChange,
  enableSelection = false,
  selectedRowIds,
  onSelectedRowIdsChange,
}: ZenTableProps<T>) {
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = useState(defaultPageSize);
  const [internalSearch, setInternalSearch] = useState("");
  const [sortField, setSortField] = useState<string | null>(null);
  const [sortDir, setSortDir] = useState<SortDir>("asc");
  const [internalSelected, setInternalSelected] = useState<string[]>([]);

  const search = controlledSearch ?? internalSearch;
  const selectedIds = selectedRowIds ?? internalSelected;

  function setSelectedIds(ids: string[]) {
    if (onSelectedRowIdsChange) onSelectedRowIdsChange(ids);
    else setInternalSelected(ids);
  }

  function setSearch(value: string) {
    if (onSearchChange) onSearchChange(value);
    else setInternalSearch(value);
    setPage(0);
  }

  const processedRows = useMemo(() => {
    let list = rows;
    if (enableSearch && search.trim()) {
      list = list.filter((row) => rowMatchesSearch(row, columns, search));
    }

    if (sortField) {
      const col = columns.find((c) => c.field === sortField);
      if (col) {
        const dir = sortDir === "asc" ? 1 : -1;
        list = [...list].sort((a, b) => dir * compareValues(col.getValue(a), col.getValue(b)));
      }
    }

    return list;
  }, [rows, columns, enableSearch, search, sortField, sortDir]);

  useEffect(() => {
    onVisibleRowsChange?.(processedRows);
  }, [processedRows, onVisibleRowsChange]);

  // Drop selections that no longer exist in the current row set.
  useEffect(() => {
    if (!enableSelection) return;
    const valid = new Set(rows.map((r) => getRowId(r)));
    const next = selectedIds.filter((id) => valid.has(id));
    if (next.length !== selectedIds.length) setSelectedIds(next);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [rows, enableSelection]);

  const pagedRows = useMemo(() => {
    if (!enablePagination) return processedRows;
    const start = page * pageSize;
    return processedRows.slice(start, start + pageSize);
  }, [processedRows, page, pageSize, enablePagination]);

  const pageIds = pagedRows.map((r) => getRowId(r));
  const allPageSelected =
    enableSelection && pageIds.length > 0 && pageIds.every((id) => selectedIds.includes(id));
  const somePageSelected =
    enableSelection && pageIds.some((id) => selectedIds.includes(id)) && !allPageSelected;

  function toggleAllPage() {
    if (allPageSelected) {
      setSelectedIds(selectedIds.filter((id) => !pageIds.includes(id)));
    } else {
      setSelectedIds([...new Set([...selectedIds, ...pageIds])]);
    }
  }

  function toggleOne(id: string) {
    if (selectedIds.includes(id)) {
      setSelectedIds(selectedIds.filter((x) => x !== id));
    } else {
      setSelectedIds([...selectedIds, id]);
    }
  }

  function toggleSort(field: string, sortable?: boolean) {
    if (sortable === false) return;
    if (sortField !== field) {
      setSortField(field);
      setSortDir("asc");
    } else if (sortDir === "asc") {
      setSortDir("desc");
    } else {
      setSortField(null);
      setSortDir("asc");
    }
    setPage(0);
  }

  const colSpan = columns.length + (enableSelection ? 1 : 0);

  if (loading) {
    return (
      <Box
        display="flex"
        justifyContent="center"
        alignItems="center"
        py={6}
        sx={fillHeight ? { flex: 1, minHeight: 0 } : undefined}
      >
        <CircularProgress size={28} />
      </Box>
    );
  }

  return (
    <Paper
      elevation={0}
      sx={{
        border: "1px solid",
        borderColor: "divider",
        borderRadius: 2,
        overflow: "hidden",
        display: "flex",
        flexDirection: "column",
        maxHeight: "100%",
        ...(fillHeight ? { height: "100%", minHeight: 0, flex: 1 } : {}),
      }}
    >
      {enableSearch ? (
        <Box sx={{ px: 1.5, py: 1, borderBottom: "1px solid", borderColor: "divider" }}>
          <TextField
            size="small"
            fullWidth
            placeholder={searchPlaceholder}
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            InputProps={{
              startAdornment: (
                <InputAdornment position="start">
                  <MagnifyingGlass size={DEFAULT_SMALL_ICON_SIZE} />
                </InputAdornment>
              ),
              endAdornment: search ? (
                <InputAdornment position="end">
                  <IconButton size="small" aria-label="Clear search" onClick={() => setSearch("")}>
                    <X size={DEFAULT_SMALL_ICON_SIZE} />
                  </IconButton>
                </InputAdornment>
              ) : undefined,
            }}
          />
        </Box>
      ) : null}

      <TableContainer sx={{ flex: 1, minHeight: 0, overflow: "auto" }}>
        <Table stickyHeader size={dense ? "small" : "medium"}>
          <TableHead>
            <TableRow>
              {enableSelection ? (
                <TableCell padding="checkbox" sx={{ bgcolor: "background.paper", width: 48 }}>
                  <Checkbox
                    size="small"
                    indeterminate={somePageSelected}
                    checked={allPageSelected}
                    onChange={toggleAllPage}
                    inputProps={{ "aria-label": "Select all on page" }}
                  />
                </TableCell>
              ) : null}
              {columns.map((col) => {
                const canSort = col.sortable !== false && col.type !== "action";
                return (
                  <TableCell
                    key={col.field}
                    align={col.cellAlignment ?? "left"}
                    sortDirection={sortField === col.field ? sortDir : false}
                    sx={{
                      width: col.width,
                      minWidth: col.width,
                      fontWeight: 600,
                      bgcolor: "background.paper",
                      whiteSpace: "nowrap",
                    }}
                  >
                    {canSort ? (
                      <TableSortLabel
                        active={sortField === col.field}
                        direction={sortField === col.field ? sortDir : "asc"}
                        onClick={() => toggleSort(col.field, col.sortable)}
                      >
                        {col.headerName}
                      </TableSortLabel>
                    ) : (
                      col.headerName
                    )}
                  </TableCell>
                );
              })}
            </TableRow>
          </TableHead>
          <TableBody>
            {pagedRows.length === 0 ? (
              <TableRow>
                <TableCell colSpan={colSpan}>
                  <Typography variant="body2" color="text.secondary" py={2}>
                    {emptyMessage}
                  </Typography>
                </TableCell>
              </TableRow>
            ) : (
              pagedRows.map((row) => {
                const id = getRowId(row);
                const checked = selectedIds.includes(id);
                const expanded = expandedRowId === id && !!renderExpandedRow;
                return (
                  <Fragment key={id}>
                    <TableRow
                      hover
                      selected={checked || expanded}
                      onClick={onRowClick ? () => onRowClick(row) : undefined}
                      sx={{ cursor: onRowClick ? "pointer" : "default" }}
                    >
                      {enableSelection ? (
                        <TableCell padding="checkbox">
                          <Checkbox
                            size="small"
                            checked={checked}
                            onClick={(e) => e.stopPropagation()}
                            onChange={() => toggleOne(id)}
                            inputProps={{ "aria-label": `Select row ${id}` }}
                          />
                        </TableCell>
                      ) : null}
                      {columns.map((col) => {
                        const value = col.getValue(row);
                        const content = col.displayRenderer
                          ? col.displayRenderer(value, row)
                          : String(value ?? "");
                        return (
                          <TableCell
                            key={col.field}
                            align={col.cellAlignment ?? "left"}
                            sx={{ width: col.width, minWidth: col.width }}
                          >
                            {content}
                          </TableCell>
                        );
                      })}
                    </TableRow>
                    {expanded ? (
                      <TableRow>
                        <TableCell colSpan={colSpan} sx={{ py: 0, px: 0, bgcolor: "grey.50" }}>
                          <Box sx={{ px: 2, pr: 4, py: 1.5 }}>
                            {renderExpandedRow(row)}
                          </Box>
                        </TableCell>
                      </TableRow>
                    ) : null}
                  </Fragment>
                );
              })
            )}
          </TableBody>
        </Table>
      </TableContainer>

      {enablePagination && processedRows.length > 0 ? (
        <Stack
          direction="row"
          justifyContent="flex-end"
          alignItems="center"
          sx={{ borderTop: "1px solid", borderColor: "divider", px: 1 }}
        >
          <ZenPagination
            pageSize={pageSize}
            currentPage={page}
            totalCount={processedRows.length}
            onPageChange={setPage}
            onPageSizeChange={setPageSize}
          />
        </Stack>
      ) : null}
    </Paper>
  );
}

export default ZenTable;
