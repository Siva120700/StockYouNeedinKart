import { useEffect, useState } from "react";
import { Box, Divider, Typography } from "@mui/material";
import useMediaQuery from "@mui/material/useMediaQuery";
import { useTheme } from "@mui/material/styles";
import { Clock } from "@phosphor-icons/react";
import { DEFAULT_SMALL_ICON_SIZE } from "../../constants";

const IST = "Asia/Kolkata";

function formatIstDate(now: Date): string {
  const parts = Object.fromEntries(
    new Intl.DateTimeFormat("en-GB", {
      timeZone: IST,
      weekday: "short",
      day: "numeric",
      month: "short",
    })
      .formatToParts(now)
      .map((p) => [p.type, p.value]),
  );
  return `${parts.weekday}, ${parts.day} ${parts.month}`;
}

function formatIstTime(now: Date): string {
  return new Intl.DateTimeFormat("en-IN", {
    timeZone: IST,
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
    hour12: true,
  })
    .format(now)
    .toLowerCase();
}

/** Live IST date/time chip — StepOne TradeGen style header clock. */
export default function IstLiveClock() {
  const theme = useTheme();
  const compact = useMediaQuery(theme.breakpoints.down("md"));
  const [now, setNow] = useState(() => new Date());

  useEffect(() => {
    const id = window.setInterval(() => setNow(new Date()), 1000);
    return () => window.clearInterval(id);
  }, []);

  return (
    <Box
      aria-live="polite"
      aria-label={`Indian Standard Time: ${formatIstDate(now)} ${formatIstTime(now)}`}
      sx={{
        display: "flex",
        alignItems: "center",
        gap: 1,
        px: compact ? 1 : 1.5,
        py: 0.75,
        borderRadius: 2,
        border: "1px solid",
        borderColor: "divider",
        bgcolor: "background.paper",
        flexShrink: 0,
      }}
    >
      <Clock size={DEFAULT_SMALL_ICON_SIZE} weight="regular" color={theme.palette.text.secondary} />
      {!compact && (
        <>
          <Typography
            variant="body2"
            sx={{ color: "text.secondary", fontSize: 13, whiteSpace: "nowrap" }}
          >
            {formatIstDate(now)}
          </Typography>
          <Divider orientation="vertical" flexItem sx={{ borderColor: "divider", my: 0.25 }} />
        </>
      )}
      <Typography
        variant="body2"
        sx={{
          fontSize: 13,
          fontWeight: 500,
          fontVariantNumeric: "tabular-nums",
          whiteSpace: "nowrap",
        }}
      >
        {formatIstTime(now)}
      </Typography>
      <Typography variant="caption" sx={{ color: "text.secondary", fontSize: 11, ml: -0.25 }}>
        IST
      </Typography>
    </Box>
  );
}
