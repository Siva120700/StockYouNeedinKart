import {
  Box,
  Divider,
  List,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  Stack,
  Typography,
  IconButton,
} from "@mui/material";
import {
  ChartLine,
  Drop,
  ListChecks,
  Notebook,
  Pulse,
  SidebarSimple,
} from "@phosphor-icons/react";
import { NavLink } from "react-router-dom";
import { DEFAULT_ICON_SIZE } from "../../constants";

const navItems = [
  { to: "/ltp", label: "Live LTP", icon: Pulse },
  { to: "/signals", label: "Signals", icon: ChartLine },
  { to: "/liquidity", label: "Liquidity", icon: Drop },
  { to: "/backtest", label: "Backtest", icon: Notebook },
  { to: "/positions", label: "Positions", icon: ListChecks },
];

type LeftPanelProps = {
  isMobile: boolean;
  isDocked: boolean;
  handleMobileLeftDrawerClose: () => void;
  handleLeftDrawerDock: () => void;
};

export default function LeftPanel({
  isMobile,
  handleLeftDrawerDock,
  handleMobileLeftDrawerClose,
}: LeftPanelProps) {
  return (
    <Stack height="100%" sx={{ bgcolor: "background.paper" }}>
      <Stack
        direction="row"
        alignItems="center"
        justifyContent="space-between"
        px={2}
        py={1.5}
      >
        <Typography
          variant="h6"
          sx={{ fontFamily: '"Instrument Serif", Georgia, serif', fontWeight: 400 }}
        >
          StockYouNeed
        </Typography>
        <IconButton size="small" onClick={handleLeftDrawerDock} title="Dock / undock">
          <SidebarSimple size={DEFAULT_ICON_SIZE} />
        </IconButton>
      </Stack>
      <Divider />
      <List sx={{ flex: 1, px: 1 }}>
        {navItems.map((item) => {
          const Icon = item.icon;
          return (
            <ListItemButton
              key={item.to}
              component={NavLink}
              to={item.to}
              onClick={isMobile ? handleMobileLeftDrawerClose : undefined}
              sx={{
                borderRadius: 1,
                mb: 0.5,
                "&.active": {
                  bgcolor: "action.selected",
                },
              }}
            >
              <ListItemIcon sx={{ minWidth: 36 }}>
                <Icon size={DEFAULT_ICON_SIZE} />
              </ListItemIcon>
              <ListItemText primary={item.label} />
            </ListItemButton>
          );
        })}
      </List>
      <Box px={2} py={1.5}>
        <Typography variant="caption" color="text.secondary">
          Market data via Angel · thin UI
        </Typography>
      </Box>
    </Stack>
  );
}
