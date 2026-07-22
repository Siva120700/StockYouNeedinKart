export function formatNumberWithCommas(
  value: number,
  decimalPlaces = 0,
  suppressZero = false,
): string {
  if (suppressZero && value === 0) return "";
  return value.toLocaleString(undefined, {
    minimumFractionDigits: decimalPlaces,
    maximumFractionDigits: decimalPlaces,
  });
}

export function formatCurrency(
  value: number | string,
  minimumFractionDigits = 2,
  maximumFractionDigits = 2,
  suppressZero = false,
  currency = "INR",
): string {
  const num = Number(value);
  if (Number.isNaN(num)) return "";
  if (suppressZero && num === 0) return "";
  return num.toLocaleString(undefined, {
    style: "currency",
    currency,
    minimumFractionDigits,
    maximumFractionDigits,
  });
}
