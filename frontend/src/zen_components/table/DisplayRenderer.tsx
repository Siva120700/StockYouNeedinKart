import React from "react";
import { Box, Chip, IconButton, Link, Typography, type SxProps, type Theme } from "@mui/material";
import { Stack } from "@mui/material";
import dayjs from "dayjs";
import { CheckSquare, Square } from "@phosphor-icons/react";
import { DEFAULT_ICON_SIZE } from "../../constants";
import {
  formatCurrency,
  formatNumberWithCommas,
} from "../../utilities/numberUtilities";
import type { DisplayRenderer } from "./columnTypes";

interface LinkOptions<T> {
  onClick: (row: T) => void;
  disabled?: (row: T) => boolean;
  buttonSx?: SxProps<Theme>;
  variant?: "text" | "contained" | "outlined";
  size?: "small" | "medium" | "large";
  showIcon?: boolean;
  startIcon?: (row: T) => React.ReactNode;
  endIcon?: (row: T) => React.ReactNode;
  cellAlignment?: "left" | "center" | "right";
  color?: (row: T) => string | undefined;
}

interface NumberOptions<T> {
  startIcon?: (row: T) => React.ReactNode;
  endIcon?: (row: T) => React.ReactNode;
  decimalPlaces?: number;
  suppressZero?: boolean;
  textAlignment?: "flex-start" | "center" | "flex-end" | "left" | "right";
}

interface CurrencyOptions<T> {
  startIcon?: (row: T) => React.ReactNode;
  endIcon?: (row: T) => React.ReactNode;
  minimumFractionDigits?: number;
  maximumFractionDigits?: number;
  currency?: string;
  suppressZero?: boolean;
  textAlignment?: "flex-start" | "center" | "flex-end" | "left" | "right";
  prefix?: string;
  hintText?: string;
}

export const displayRenderers = {
  link:
    <T,>(options: LinkOptions<T>): DisplayRenderer<T> =>
    (value, row) => {
      const {
        onClick,
        buttonSx,
        startIcon,
        endIcon,
        color = () => undefined,
      } = options;

      return (
        <Stack
          direction="row"
          alignItems="center"
          justifyContent="space-between"
          width="100%"
        >
          <Stack direction="row" alignItems="center" width="100%">
            {startIcon && <Box sx={{ marginRight: 1 }}>{startIcon(row)}</Box>}
            <Link
              variant="body2"
              onClick={(e) => {
                e.stopPropagation();
                onClick(row);
              }}
              sx={{
                cursor: "pointer",
                textOverflow: "ellipsis",
                textWrap: "nowrap",
                display: "flex",
                alignItems: "center",
                ...buttonSx,
              }}
              color={color(row) ?? "text.primary"}
            >
              <Typography variant="body2" noWrap sx={{ textOverflow: "ellipsis" }}>
                {String(value ?? "")}
              </Typography>
            </Link>
          </Stack>
          {endIcon && <Box sx={{ marginLeft: 1 }}>{endIcon(row)}</Box>}
        </Stack>
      );
    },

  text:
    <T,>(options: {
      startIcon?: (row: T) => React.ReactNode;
      endIcon?: (row: T) => React.ReactNode;
      multiline?: boolean;
      hintText?: string;
    }): DisplayRenderer<T> =>
    (value, row) => {
      const textContent = String(value ?? "");
      const isTextEmpty = !textContent;
      const multilineStyles: SxProps<Theme> = {
        whiteSpace: "pre-wrap",
        wordBreak: "break-word",
      };
      const singleLineStyles: SxProps<Theme> = {
        textOverflow: "ellipsis",
        overflow: "hidden",
      };

      return (
        <Box
          display="flex"
          alignItems={options?.multiline ? "flex-start" : "center"}
        >
          {options?.startIcon?.(row)}
          <Typography
            variant="body2"
            noWrap={!options?.multiline}
            color={isTextEmpty ? "text.disabled" : "text.primary"}
            sx={options?.multiline ? multilineStyles : singleLineStyles}
          >
            {isTextEmpty ? (options?.hintText ?? "") : textContent}
          </Typography>
          {options?.endIcon?.(row)}
        </Box>
      );
    },

  number:
    <T,>(options: NumberOptions<T>): DisplayRenderer<T> =>
    (value, row) => {
      if (value === null || value === undefined || value === "") return "";
      const num = Number(value);
      if (Number.isNaN(num)) return "";

      return (
        <Box display="flex" alignItems="center" width="100%" sx={{ minWidth: 0 }}>
          <Box flexShrink={0}>{options?.startIcon?.(row)}</Box>
          <Box
            flex={1}
            display="flex"
            justifyContent={options?.textAlignment ?? "flex-end"}
            sx={{ minWidth: 0 }}
          >
            <Typography noWrap textOverflow="ellipsis" variant="body2">
              {formatNumberWithCommas(
                num,
                options?.decimalPlaces ?? 0,
                options?.suppressZero,
              )}
            </Typography>
          </Box>
          <Box flexShrink={0} marginLeft={1}>
            {options?.endIcon?.(row)}
          </Box>
        </Box>
      );
    },

  currency:
    <T,>(options: CurrencyOptions<T>): DisplayRenderer<T> =>
    (value, row) => {
      const {
        startIcon,
        endIcon,
        textAlignment,
        minimumFractionDigits,
        maximumFractionDigits,
        suppressZero,
        currency = "INR",
        hintText,
      } = options;

      const isEmpty = value === null || value === undefined || value === "";
      const num = Number(value);
      const isInvalid = Number.isNaN(num);

      if ((isEmpty || isInvalid) && hintText) {
        return (
          <Typography variant="body2" color="text.disabled">
            {hintText}
          </Typography>
        );
      }
      if (isEmpty || isInvalid) return "";

      return (
        <Box display="flex" alignItems="center" width="100%" sx={{ minWidth: 0 }}>
          <Box flexShrink={0}>{startIcon?.(row)}</Box>
          <Box
            flex={1}
            display="flex"
            justifyContent={textAlignment ?? "flex-end"}
            sx={{ minWidth: 0 }}
          >
            <Typography noWrap textOverflow="ellipsis" variant="body2">
              {formatCurrency(
                num,
                minimumFractionDigits,
                maximumFractionDigits,
                suppressZero,
                currency,
              )}
            </Typography>
          </Box>
          <Box flexShrink={0} marginLeft={1}>
            {endIcon?.(row)}
          </Box>
        </Box>
      );
    },

  percentage:
    <T,>(
      startIcon?: (row: T) => React.ReactNode,
      endIcon?: (row: T) => React.ReactNode,
      decimalPlaces = 1,
    ): DisplayRenderer<T> =>
    (value, row) => {
      if (value === null || value === undefined || value === "") return "";
      const num = Number(value);
      if (Number.isNaN(num)) return "";
      return (
        <Box display="flex" alignItems="center" width="100%">
          {startIcon?.(row)}
          <Box flex={1} display="flex" justifyContent="flex-end">
            <Typography variant="body2">{`${num.toFixed(decimalPlaces)}%`}</Typography>
          </Box>
          {endIcon?.(row)}
        </Box>
      );
    },

  date:
    <T,>(format = "MM-DD-YYYY", hintText?: string): DisplayRenderer<T> =>
    (value) => (
      <>
        <Typography noWrap textOverflow="ellipsis" variant="body2">
          {value ? dayjs(value as string | Date).format(format) : ""}
        </Typography>
        {hintText && !value ? (
          <Typography variant="body2" color="text.disabled">
            {hintText}
          </Typography>
        ) : null}
      </>
    ),

  dateTime:
    <T,>(format = "MM-DD-YYYY hh:mm A"): DisplayRenderer<T> =>
    (value) => (
      <Typography noWrap textOverflow="ellipsis" variant="body2">
        {value ? dayjs(value as string | Date).format(format) : ""}
      </Typography>
    ),

  status:
    <T,>(
      statuses: Record<
        string,
        { label: string; color: string; icon?: React.ReactElement }
      >,
    ): DisplayRenderer<T> =>
    (value) => {
      const key = String(value ?? "");
      const status = key ? statuses[key] : null;
      if (!status) return String(value ?? "");
      return (
        <Chip
          icon={status.icon}
          label={status.label}
          size="small"
          sx={{
            backgroundColor: `${status.color}20`,
            color: status.color,
            "& .MuiChip-icon": { color: status.color },
          }}
        />
      );
    },

  boolean:
    <T,>(
      _trueLabel = "Yes",
      _falseLabel = "No",
      trueIcon: React.ReactNode = <CheckSquare size={DEFAULT_ICON_SIZE} />,
      falseIcon: React.ReactNode = <Square size={DEFAULT_ICON_SIZE} />,
    ): DisplayRenderer<T> =>
    (value) => (
      <Box display="flex" justifyContent="center" alignItems="center">
        {value ? trueIcon : falseIcon}
      </Box>
    ),

  actions:
    <T,>(
      actions: Array<{
        icon: React.ReactNode;
        onClick: (row: T) => void;
        disabled?: (row: T) => boolean;
        hide?: (row: T) => boolean;
        color?: string;
        tooltip?: string;
      }>,
    ): DisplayRenderer<T> =>
    (_, row) => (
      <Box sx={{ display: "flex", gap: 0.5 }}>
        {actions.map((action, index) => {
          if (action.hide?.(row)) return null;
          return (
            <IconButton
              key={index}
              size="small"
              onClick={(e) => {
                e.stopPropagation();
                action.onClick(row);
              }}
              disabled={action.disabled?.(row)}
              sx={{ color: action.color }}
              title={action.tooltip}
            >
              {action.icon}
            </IconButton>
          );
        })}
      </Box>
    ),
};
