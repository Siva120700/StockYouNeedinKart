import { Button, Stack } from "@mui/material";
import { Trash } from "@phosphor-icons/react";
import { DEFAULT_SMALL_ICON_SIZE } from "../../constants";

/** Toolbar shown above Traded tables for multi-select delete. */
export default function TradedDeleteBar({
  selectedCount,
  onDelete,
}: {
  selectedCount: number;
  onDelete: () => void;
}) {
  return (
    <Stack direction="row" spacing={1} alignItems="center" sx={{ mb: 1 }}>
      <Button
        variant="outlined"
        color="error"
        size="small"
        disabled={selectedCount === 0}
        startIcon={<Trash size={DEFAULT_SMALL_ICON_SIZE} />}
        onClick={onDelete}
      >
        {selectedCount > 0 ? `Delete selected (${selectedCount})` : "Delete selected"}
      </Button>
    </Stack>
  );
}
