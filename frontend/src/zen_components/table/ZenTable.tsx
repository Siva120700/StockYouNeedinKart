import { useMemo, useState } from "react";
import {
  Box,
  CircularProgress,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from "@mui/material";
import type { ColumnConfig } from "./columnTypes";
import ZenPagination from "../shared/ZenPagination";

export type ZenTableProps<T> = {
  columns: ColumnConfig<T>[];
  rows: T[];
  getRowId: (row: T) => string;
  loading?: boolean;
  emptyMessage?: string;
  onRowClick?: (row: T) => void;
  dense?: boolean;
  /** Client-side pagination (default true when rows.length > pageSize). */
  enablePagination?: boolean;
  defaultPageSize?: number;
  /** Fill parent height and scroll rows inside the table body. */
  fillHeight?: boolean;
};

export function ZenTable<T>({
  columns,
  rows,
  getRowId,
  loading = false,
  emptyMessage = "No rows",
  onRowClick,
  dense = true,
  enablePagination = true,
  defaultPageSize = 25,
  fillHeight = false,
}: ZenTableProps<T>) {
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = useState(defaultPageSize);

  const pagedRows = useMemo(() => {
    if (!enablePagination) return rows;
    const start = page * pageSize;
    return rows.slice(start, start + pageSize);
  }, [rows, page, pageSize, enablePagination]);

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
        ...(fillHeight ? { height: "100%", minHeight: 0 } : {}),
      }}
    >
      <TableContainer sx={{ flex: 1, minHeight: 0, overflow: "auto" }}>
        <Table stickyHeader size={dense ? "small" : "medium"}>
          <TableHead>
            <TableRow>
              {columns.map((col) => (
                <TableCell
                  key={col.field}
                  align={col.cellAlignment ?? "left"}
                  sx={{
                    width: col.width,
                    minWidth: col.width,
                    fontWeight: 600,
                    bgcolor: "background.paper",
                    whiteSpace: "nowrap",
                  }}
                >
                  {col.headerName}
                </TableCell>
              ))}
            </TableRow>
          </TableHead>
          <TableBody>
            {pagedRows.length === 0 ? (
              <TableRow>
                <TableCell colSpan={columns.length}>
                  <Typography variant="body2" color="text.secondary" py={2}>
                    {emptyMessage}
                  </Typography>
                </TableCell>
              </TableRow>
            ) : (
              pagedRows.map((row) => (
                <TableRow
                  key={getRowId(row)}
                  hover
                  onClick={onRowClick ? () => onRowClick(row) : undefined}
                  sx={{ cursor: onRowClick ? "pointer" : "default" }}
                >
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
              ))
            )}
          </TableBody>
        </Table>
      </TableContainer>

      {enablePagination && rows.length > 0 ? (
        <Stack
          direction="row"
          justifyContent="flex-end"
          alignItems="center"
          sx={{ borderTop: "1px solid", borderColor: "divider", px: 1 }}
        >
          <ZenPagination
            pageSize={pageSize}
            currentPage={page}
            totalCount={rows.length}
            onPageChange={setPage}
            onPageSizeChange={setPageSize}
          />
        </Stack>
      ) : null}
    </Paper>
  );
}

export default ZenTable;
