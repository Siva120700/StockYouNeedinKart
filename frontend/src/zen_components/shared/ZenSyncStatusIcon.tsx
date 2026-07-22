import { Box, CircularProgress } from "@mui/material";
import { ArrowsClockwise } from "@phosphor-icons/react";
import { DEFAULT_ICON_SIZE } from "../../constants";

export default function ZenSyncStatusIcon({ isSyncing }: { isSyncing: boolean }) {
  if (!isSyncing) return null;
  return (
    <Box display="flex" alignItems="center" color="text.secondary">
      <CircularProgress size={DEFAULT_ICON_SIZE} thickness={5} />
      <Box component="span" sx={{ ml: 0.5, display: "none" }}>
        <ArrowsClockwise size={DEFAULT_ICON_SIZE} />
      </Box>
    </Box>
  );
}
