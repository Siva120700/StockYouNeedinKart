import { useMemo, useState } from "react";
import {
  Box,
  Button,
  Divider,
  IconButton,
  Popover,
  Stack,
  TextField,
  ToggleButton,
  ToggleButtonGroup,
  Typography,
} from "@mui/material";
import { Calculator, X } from "@phosphor-icons/react";
import { DEFAULT_SMALL_ICON_SIZE } from "../../constants";
import {
  estimateOptionPremiums,
  type PremiumSide,
} from "../../utils/premiumCalculator";

function parseNum(raw: string): number | null {
  const n = Number(raw.trim());
  return Number.isFinite(n) ? n : null;
}

function fmt(n: number | null | undefined): string {
  if (n == null || !Number.isFinite(n)) return "—";
  return n.toFixed(2);
}

/** Header Δ premium calculator (Index Options style). */
export default function PremiumCalculatorPopover() {
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const [side, setSide] = useState<PremiumSide>("long");
  const [spotEntry, setSpotEntry] = useState("");
  const [entryPremium, setEntryPremium] = useState("");
  const [delta, setDelta] = useState("");
  const [targetSpot, setTargetSpot] = useState("");
  const [exitSpot, setExitSpot] = useState("");

  const result = useMemo(() => {
    const spot = parseNum(spotEntry);
    const prem = parseNum(entryPremium);
    const d = parseNum(delta);
    const target = parseNum(targetSpot);
    const exit = parseNum(exitSpot);
    if (spot == null || prem == null || d == null || target == null || exit == null) return null;
    return estimateOptionPremiums(spot, prem, d, target, exit, side);
  }, [spotEntry, entryPremium, delta, targetSpot, exitSpot, side]);

  const isShort = side === "short";

  return (
    <>
      <Button
        size="small"
        variant="outlined"
        onClick={(e) => setAnchor(e.currentTarget)}
        startIcon={<Calculator size={DEFAULT_SMALL_ICON_SIZE} />}
        sx={{
          textTransform: "none",
          borderColor: "divider",
          color: "text.primary",
          px: 1.5,
          py: 0.75,
          minWidth: 0,
          whiteSpace: "nowrap",
        }}
      >
        Premium
      </Button>
      <Popover
        open={Boolean(anchor)}
        anchorEl={anchor}
        onClose={() => setAnchor(null)}
        anchorOrigin={{ vertical: "bottom", horizontal: "right" }}
        transformOrigin={{ vertical: "top", horizontal: "right" }}
        slotProps={{ paper: { sx: { p: 2, width: 320, maxWidth: "95vw" } } }}
      >
        <Stack spacing={1.5}>
          <Stack direction="row" alignItems="center" justifyContent="space-between">
            <Typography variant="subtitle2">Premium calculator</Typography>
            <IconButton size="small" onClick={() => setAnchor(null)} aria-label="Close">
              <X size={DEFAULT_SMALL_ICON_SIZE} />
            </IconButton>
          </Stack>
          <ToggleButtonGroup
            exclusive
            fullWidth
            size="small"
            value={side}
            onChange={(_, v: PremiumSide | null) => {
              if (v) setSide(v);
            }}
          >
            <ToggleButton value="long">Long (CE)</ToggleButton>
            <ToggleButton value="short">Short (PE)</ToggleButton>
          </ToggleButtonGroup>
          <Typography variant="caption" color="text.secondary">
            {isShort
              ? "PE: target below spot, SL above. Premium rises as spot falls."
              : "CE: target above spot, SL below. Premium rises as spot rises."}{" "}
            Δ × |spot move|.
          </Typography>
          <TextField
            size="small"
            label="Spot (current price)"
            value={spotEntry}
            onChange={(e) => setSpotEntry(e.target.value)}
            inputMode="decimal"
          />
          <TextField
            size="small"
            label="Entry premium"
            value={entryPremium}
            onChange={(e) => setEntryPremium(e.target.value)}
            inputMode="decimal"
          />
          <TextField
            size="small"
            label="Delta (abs)"
            value={delta}
            onChange={(e) => setDelta(e.target.value)}
            inputMode="decimal"
          />
          <TextField
            size="small"
            label={isShort ? "Target price (spot down)" : "Target price (spot up)"}
            value={targetSpot}
            onChange={(e) => setTargetSpot(e.target.value)}
            inputMode="decimal"
          />
          <TextField
            size="small"
            label={isShort ? "Exit price (spot SL up)" : "Exit price (spot SL down)"}
            value={exitSpot}
            onChange={(e) => setExitSpot(e.target.value)}
            inputMode="decimal"
          />
          <Divider />
          <Box
            sx={{
              bgcolor: "grey.50",
              borderRadius: 1,
              p: 1.5,
              border: "1px solid",
              borderColor: "divider",
            }}
          >
            <Stack spacing={0.75}>
              <Stack direction="row" justifyContent="space-between">
                <Typography variant="body2" color="text.secondary">
                  Entry premium
                </Typography>
                <Typography variant="body2" fontWeight={600}>
                  {result ? fmt(result.entryPremium) : "—"}
                </Typography>
              </Stack>
              <Stack direction="row" justifyContent="space-between">
                <Typography variant="body2" color="text.secondary">
                  Target premium
                </Typography>
                <Typography variant="body2" fontWeight={600} color="success.main">
                  {result ? fmt(result.targetPremium) : "—"}
                </Typography>
              </Stack>
              <Stack direction="row" justifyContent="space-between">
                <Typography variant="body2" color="text.secondary">
                  Exit premium
                </Typography>
                <Typography variant="body2" fontWeight={600} color="error.main">
                  {result ? fmt(result.exitPremium) : "—"}
                </Typography>
              </Stack>
            </Stack>
          </Box>
        </Stack>
      </Popover>
    </>
  );
}
