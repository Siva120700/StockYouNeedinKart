import type { ReactNode } from "react";
import type { SxProps, Theme } from "@mui/material";

export type CellAlignment = "left" | "center" | "right";

export type DisplayRenderer<T> = (
  value: unknown,
  row: T,
  extra?: { theme?: Theme; sx?: SxProps<Theme>; width?: number },
) => ReactNode;

export type BaseColumnConfig<T> = {
  field: string;
  headerName: string;
  width?: number;
  sortable?: boolean;
  editable?: (row: T) => boolean;
  cellAlignment?: CellAlignment;
  disableHide?: boolean;
  getValue: (row: T) => unknown;
  setValue?: (row: T, value: unknown) => void;
  displayRenderer?: DisplayRenderer<T>;
  validation?: (value: unknown) => string | null;
};

export type TextColumnConfig<T> = BaseColumnConfig<T> & {
  type: "text";
  textType?: "text" | "number" | "currency" | "percentage" | "multiline";
  startIcon?: (row: T) => ReactNode;
  endIcon?: (row: T) => ReactNode;
  minDecimalPlaces?: number;
  maxDecimalPlaces?: number;
  minValue?: number;
  maxValue?: number;
  allowNegative?: boolean;
  suppressZero?: boolean;
  prefix?: string;
  textAlignment?: "flex-start" | "center" | "flex-end" | "left" | "right";
  required?: boolean;
};

export type DateColumnConfig<T> = BaseColumnConfig<T> & {
  type: "date";
  dateConfig?: { format?: string };
};

export type BooleanColumnConfig<T> = BaseColumnConfig<T> & {
  type: "boolean";
  trueLabel?: string;
  falseLabel?: string;
  trueIcon?: ReactNode;
  falseIcon?: ReactNode;
};

export type StatusColumnConfig<T> = BaseColumnConfig<T> & {
  type: "status";
  statuses: Record<
    string,
    { label: string; color: string; icon?: React.ReactElement }
  >;
};

export type LinkColumnConfig<T> = BaseColumnConfig<T> & {
  type: "link";
  onClick: (row: T) => void;
  variant?: "text" | "contained" | "outlined";
  size?: "small" | "medium" | "large";
  showIcon?: boolean;
  disabled?: (row: T) => boolean;
  buttonSx?: SxProps<Theme>;
  color?: (row: T) => string | undefined;
};

export type ActionColumnConfig<T> = BaseColumnConfig<T> & {
  type: "action";
  actions: (row: T) => Array<{
    icon: ReactNode;
    onClick: (row: T) => void;
    disabled?: (row: T) => boolean;
    hide?: (row: T) => boolean;
    color?: string;
    tooltip?: string;
  }> | null;
};

export type ColumnConfig<T> =
  | TextColumnConfig<T>
  | DateColumnConfig<T>
  | BooleanColumnConfig<T>
  | StatusColumnConfig<T>
  | LinkColumnConfig<T>
  | ActionColumnConfig<T>;
