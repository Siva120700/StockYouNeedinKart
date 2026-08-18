import type { ReactNode } from "react";
import { Box } from "@mui/material";

type PageFrameProps = {
  children: ReactNode;
  /** Scroll the page body (Analyze, News, multi-section pages). Table pages leave this off. */
  scroll?: boolean;
};

/** Fills the layout content area. Header/title stay fixed; children layout in a column. */
export default function PageFrame({ children, scroll = false }: PageFrameProps) {
  return (
    <Box
      sx={{
        height: "100%",
        minHeight: 0,
        display: "flex",
        flexDirection: "column",
        gap: 2,
        overflow: scroll ? "auto" : "hidden",
      }}
    >
      {children}
    </Box>
  );
}

/** Remaining height under alerts/tabs — put ZenTable with fillHeight inside. */
export function TablePane({ children }: { children: ReactNode }) {
  return (
    <Box
      sx={{
        flex: 1,
        minHeight: 0,
        display: "flex",
        flexDirection: "column",
        overflow: "hidden",
      }}
    >
      {children}
    </Box>
  );
}

/** Remaining height that scrolls (multi-section pages). */
export function ScrollPane({ children }: { children: ReactNode }) {
  return (
    <Box sx={{ flex: 1, minHeight: 0, overflow: "auto" }}>
      {children}
    </Box>
  );
}
