import {
  type ActionColumnConfig,
  type BooleanColumnConfig,
  type DateColumnConfig,
  type LinkColumnConfig,
  type StatusColumnConfig,
  type TextColumnConfig,
} from "./columnTypes";
import { displayRenderers } from "./DisplayRenderer";

export const columnFactories = {
  createLinkColumn: <T,>(
    onClick: (row: T) => void,
    options: Partial<LinkColumnConfig<T>> &
      Pick<LinkColumnConfig<T>, "field" | "headerName" | "getValue">,
  ): LinkColumnConfig<T> => ({
    type: "link",
    width: options.width ?? 150,
    onClick,
    displayRenderer: displayRenderers.link({
      onClick,
      disabled: options.disabled,
      buttonSx: options.buttonSx,
      variant: options.variant ?? "text",
      size: options.size ?? "small",
      showIcon: options.showIcon ?? false,
      cellAlignment: options.cellAlignment ?? "left",
      color: options.color,
    }),
    variant: options.variant ?? "text",
    size: options.size ?? "small",
    showIcon: options.showIcon ?? false,
    editable: () => false,
    sortable: false,
    cellAlignment: "left",
    ...options,
  }),

  createTextColumn: <T,>(
    options: Partial<TextColumnConfig<T>> &
      Pick<TextColumnConfig<T>, "field" | "headerName" | "getValue">,
  ): TextColumnConfig<T> => ({
    type: "text",
    width: options.width ?? 150,
    displayRenderer: displayRenderers.text({
      startIcon: options.startIcon,
      endIcon: options.endIcon,
    }),
    editable: () => false,
    required: options.required ?? false,
    sortable: true,
    cellAlignment: "left",
    ...options,
  }),

  createNumberColumn: <T,>(
    options: Partial<TextColumnConfig<T>> &
      Pick<TextColumnConfig<T>, "field" | "headerName" | "getValue">,
  ): TextColumnConfig<T> => ({
    type: "text",
    width: options.width ?? 120,
    textType: "number",
    displayRenderer: displayRenderers.number({
      decimalPlaces: options.minDecimalPlaces,
      suppressZero: options.suppressZero,
      textAlignment: options.textAlignment ?? "flex-end",
      startIcon: options.startIcon,
      endIcon: options.endIcon,
    }),
    cellAlignment: "right",
    editable: () => false,
    sortable: true,
    validation: (value) => {
      if (value === null || value === "") return null;
      if (Number.isNaN(Number(value))) return "Must be a number";
      if (options.minValue !== undefined && Number(value) < options.minValue)
        return `Must be at least ${options.minValue}`;
      if (options.maxValue !== undefined && Number(value) > options.maxValue)
        return `Must be at most ${options.maxValue}`;
      if (!options.allowNegative && Number(value) < 0)
        return "Negative values not allowed";
      return null;
    },
    ...options,
  }),

  createCurrencyColumn: <T,>(
    options: Partial<TextColumnConfig<T>> &
      Pick<TextColumnConfig<T>, "field" | "headerName" | "getValue">,
  ): TextColumnConfig<T> =>
    ({
      type: "text",
      width: options.width ?? 120,
      textType: "currency",
      displayRenderer: displayRenderers.currency({
        startIcon: options.startIcon,
        endIcon: options.endIcon,
        minimumFractionDigits: options.minDecimalPlaces ?? 2,
        maximumFractionDigits: options.maxDecimalPlaces ?? 2,
        textAlignment: options.textAlignment ?? "flex-end",
        suppressZero: options.suppressZero ?? false,
        currency: options.prefix === "USD" ? "USD" : "INR",
        prefix: options.prefix ?? "INR",
      }),
      cellAlignment: "right",
      editable: () => false,
      sortable: true,
      ...options,
    }) as TextColumnConfig<T>,

  createDateColumn: <T,>(
    options: Partial<DateColumnConfig<T>> &
      Pick<DateColumnConfig<T>, "field" | "headerName" | "getValue">,
  ): DateColumnConfig<T> => ({
    type: "date",
    width: options.width ?? 150,
    displayRenderer: displayRenderers.date(options.dateConfig?.format),
    dateConfig: { format: "DD-MMM-YYYY", ...options.dateConfig },
    editable: () => false,
    sortable: true,
    ...options,
  }),

  createDateTimeColumn: <T,>(
    options: Partial<DateColumnConfig<T>> &
      Pick<DateColumnConfig<T>, "field" | "headerName" | "getValue">,
  ): DateColumnConfig<T> => ({
    type: "date",
    width: options.width ?? 180,
    displayRenderer: displayRenderers.dateTime(options.dateConfig?.format),
    dateConfig: {
      format: "DD-MMM-YYYY hh:mm A",
      ...options.dateConfig,
    },
    editable: () => false,
    sortable: true,
    ...options,
  }),

  createStatusColumn: <T,>(
    statuses: StatusColumnConfig<T>["statuses"],
    options: Partial<StatusColumnConfig<T>> &
      Pick<StatusColumnConfig<T>, "field" | "headerName" | "getValue">,
  ): StatusColumnConfig<T> => ({
    type: "status",
    width: options.width ?? 150,
    displayRenderer: displayRenderers.status(statuses),
    statuses,
    editable: () => false,
    sortable: true,
    ...options,
  }),

  createBooleanColumn: <T,>(
    options: Partial<BooleanColumnConfig<T>> &
      Pick<BooleanColumnConfig<T>, "field" | "headerName" | "getValue">,
  ): BooleanColumnConfig<T> => ({
    type: "boolean",
    width: options.width ?? 100,
    displayRenderer: displayRenderers.boolean(
      options.trueLabel,
      options.falseLabel,
      options.trueIcon,
      options.falseIcon,
    ),
    editable: () => false,
    sortable: true,
    ...options,
  }),

  createActionColumn: <T,>(
    actions: ActionColumnConfig<T>["actions"],
    options: Partial<ActionColumnConfig<T>> &
      Pick<ActionColumnConfig<T>, "field" | "headerName">,
  ): ActionColumnConfig<T> => ({
    type: "action",
    width: options.width ?? 80,
    getValue: (row) => row,
    displayRenderer: (value, row) => {
      const actionList = actions?.(row);
      if (!actionList) return null;
      return displayRenderers.actions<T>(actionList)(value, row);
    },
    actions,
    editable: () => false,
    sortable: false,
    cellAlignment: "center",
    ...options,
  }),
};
