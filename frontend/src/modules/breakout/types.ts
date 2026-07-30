export type BreakoutConfirmation = {
  id: string;
  runId: string;
  instrumentId: string;
  appSymbol: string;
  instrumentName: string;
  side: string;
  asOfDate: string;
  confirmed: boolean;
  closePrice?: number | null;
  level20d?: number | null;
  volumeRatio?: number | null;
  patternType?: string | null;
};

export function patternLabel(patternType: string | null | undefined): string {
  switch (patternType) {
    case "range_breakout":
      return "Range breakout";
    case "ascending_triangle":
      return "Ascending triangle";
    case "descending_triangle":
      return "Descending triangle";
    case "double_bottom":
      return "Double bottom";
    case "double_top":
      return "Double top";
    default:
      return "—";
  }
}
