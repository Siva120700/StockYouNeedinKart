import { Chip } from "@mui/material";
import { columnFactories } from "../zen_components/table/columnFactories";
import type { TextColumnConfig } from "../zen_components/table/columnTypes";

export type SectorRotationOverlay = {
  sectorSymbol: string | null;
  sectorName: string | null;
  sectorScore: number;
  stockMomentumScore: number;
  alignment: string;
  bucket: string;
  blendedScore: number | null;
  downranked: boolean;
};

export const SECTOR_ROTATION_GQL = `
  sectorRotation {
    sectorSymbol sectorName sectorScore stockMomentumScore
    alignment bucket blendedScore downranked
  }
`;

export function alignmentLabel(a: string | null | undefined): string {
  if (a === "a_plus") return "A+";
  if (a === "stock_only") return "Stock only";
  if (a === "watch") return "Watch";
  if (a === "avoid") return "Avoid";
  return "Neutral";
}

export function formatSectorRotation(
  rot: SectorRotationOverlay | null | undefined,
): string {
  if (!rot?.sectorSymbol) return "—";
  return `${rot.sectorName || rot.sectorSymbol} ${rot.sectorScore} · ${alignmentLabel(rot.alignment)}`;
}

export function createSectorRotationColumn<T>(
  getRot: (row: T) => SectorRotationOverlay | null | undefined,
): TextColumnConfig<T> {
  return columnFactories.createTextColumn<T>({
    field: "sectorRotation",
    headerName: "Sector rot.",
    width: 170,
    getValue: (r) => formatSectorRotation(getRot(r)),
    displayRenderer: (_v, row) => {
      const rot = getRot(row);
      const label = formatSectorRotation(rot);
      if (label === "—") return label;
      const color =
        rot?.alignment === "a_plus"
          ? "success"
          : rot?.downranked || rot?.alignment === "avoid"
            ? "error"
            : rot?.alignment === "stock_only"
              ? "warning"
              : "default";
      return <Chip size="small" color={color} variant="outlined" label={label} />;
    },
  });
}
