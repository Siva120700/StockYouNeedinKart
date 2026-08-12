import { Chip } from "@mui/material";
import { columnFactories } from "../zen_components/table/columnFactories";
import type { TextColumnConfig } from "../zen_components/table/columnTypes";

export type SectorRelativeStrength = {
  symbol: string | null;
  name: string | null;
  medianChangePct: number | null;
  rank: number | null;
  lagging: boolean;
  downranked: boolean;
};

export const SECTOR_RS_GQL = `
  sectorRs { symbol name medianChangePct rank lagging downranked }
`;

export function formatSectorRs(
  rs: SectorRelativeStrength | null | undefined,
): string {
  if (rs?.medianChangePct == null) return "—";
  const name = rs.name || rs.symbol || "";
  const sign = rs.medianChangePct >= 0 ? "+" : "";
  return `${name} ${sign}${rs.medianChangePct.toFixed(2)}%`;
}

export function createSectorRsColumn<T>(
  getRs: (row: T) => SectorRelativeStrength | null | undefined,
): TextColumnConfig<T> {
  return columnFactories.createTextColumn<T>({
    field: "sectorRs",
    headerName: "Sector RS",
    width: 160,
    getValue: (r) => formatSectorRs(getRs(r)),
    displayRenderer: (_v, row) => {
      const rs = getRs(row);
      const label = formatSectorRs(rs);
      if (label === "—") return label;
      const color = rs?.downranked ? "error" : rs?.lagging ? "warning" : "success";
      return <Chip size="small" color={color} variant="outlined" label={label} />;
    },
  });
}
