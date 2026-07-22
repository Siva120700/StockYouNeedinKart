import type { ReactNode } from "react";
import { displayRenderers } from "../table/DisplayRenderer";

const generateHintText = (label: string, customHintText?: string): string => {
  if (customHintText !== undefined) return customHintText;
  return label ? `Set ${label}...` : "";
};

export type TextFieldConfig<T> = {
  type: "text";
  name: string;
  label: string;
  hintText?: string;
  getValue: (row: T) => unknown;
  setValue?: (row: T, value: unknown) => void;
  editable?: boolean;
  required?: boolean;
  width?: string | number;
  textType?: "text" | "number" | "currency" | "percentage" | "multiline";
  displayRenderer?: (value: unknown, row: T) => ReactNode;
  minDecimalPlaces?: number;
  maxDecimalPlaces?: number;
  prefix?: string;
  textAlignment?: "flex-start" | "center" | "flex-end";
};

export type BooleanFieldConfig<T> = {
  type: "boolean";
  name: string;
  label: string;
  hintText?: string;
  getValue: (row: T) => unknown;
  setValue?: (row: T, value: unknown) => void;
  editable?: boolean;
  required?: boolean;
  width?: string | number;
  trueLabel?: string;
  falseLabel?: string;
  displayRenderer?: (value: unknown, row: T) => ReactNode;
};

export type DateFieldConfig<T> = {
  type: "date";
  name: string;
  label: string;
  hintText?: string;
  getValue: (row: T) => unknown;
  setValue?: (row: T, value: unknown) => void;
  editable?: boolean;
  required?: boolean;
  width?: string | number;
  format?: string;
  displayRenderer?: (value: unknown, row: T) => ReactNode;
};

export type FieldConfig<T> =
  | TextFieldConfig<T>
  | BooleanFieldConfig<T>
  | DateFieldConfig<T>;

export const fieldFactories = {
  createTextField: <T,>(
    options: Partial<TextFieldConfig<T>> &
      Pick<TextFieldConfig<T>, "name" | "label" | "getValue">,
  ): TextFieldConfig<T> => {
    const label = options.label ?? "";
    const hintText = generateHintText(label, options.hintText);
    return {
      type: "text",
      hintText,
      editable: true,
      required: false,
      width: "100%",
      textType: "text",
      displayRenderer: displayRenderers.text({
        multiline: options.textType === "multiline",
        hintText,
      }),
      ...options,
    };
  },

  createNumberField: <T,>(
    options: Partial<TextFieldConfig<T>> &
      Pick<TextFieldConfig<T>, "name" | "label" | "getValue">,
  ): TextFieldConfig<T> => {
    const label = options.label ?? "";
    const hintText = generateHintText(label, options.hintText);
    return {
      type: "text",
      textType: "number",
      hintText,
      editable: true,
      width: "100%",
      displayRenderer: displayRenderers.number({
        decimalPlaces: options.minDecimalPlaces ?? 0,
        textAlignment: options.textAlignment ?? "flex-start",
      }),
      ...options,
    };
  },

  createCurrencyField: <T,>(
    options: Partial<TextFieldConfig<T>> &
      Pick<TextFieldConfig<T>, "name" | "label" | "getValue">,
  ): TextFieldConfig<T> => {
    const label = options.label ?? "";
    const hintText = generateHintText(label, options.hintText);
    return {
      type: "text",
      textType: "currency",
      hintText,
      editable: true,
      width: "100%",
      displayRenderer: displayRenderers.currency({
        minimumFractionDigits: options.minDecimalPlaces ?? 2,
        maximumFractionDigits: options.maxDecimalPlaces ?? 2,
        textAlignment: options.textAlignment ?? "flex-start",
        currency: options.prefix === "USD" ? "USD" : "INR",
        hintText,
      }),
      ...options,
    };
  },

  createDateField: <T,>(
    options: Partial<DateFieldConfig<T>> &
      Pick<DateFieldConfig<T>, "name" | "label" | "getValue">,
  ): DateFieldConfig<T> => {
    const label = options.label ?? "";
    const hintText = generateHintText(label, options.hintText);
    const format = options.format ?? "DD-MMM-YYYY";
    return {
      type: "date",
      hintText,
      editable: true,
      width: "100%",
      format,
      displayRenderer: displayRenderers.date(format, hintText),
      ...options,
    };
  },

  createBooleanField: <T,>(
    options: Partial<BooleanFieldConfig<T>> &
      Pick<BooleanFieldConfig<T>, "name" | "label" | "getValue">,
  ): BooleanFieldConfig<T> => {
    const label = options.label ?? "";
    const hintText = generateHintText(label, options.hintText);
    return {
      type: "boolean",
      hintText,
      editable: true,
      width: "100%",
      displayRenderer: displayRenderers.boolean(
        options.trueLabel,
        options.falseLabel,
      ),
      ...options,
    };
  },
};
