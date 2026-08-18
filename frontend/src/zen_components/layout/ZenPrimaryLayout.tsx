import React, { useCallback, useEffect, useState } from "react";
import { styled, useTheme } from "@mui/material/styles";
import useMediaQuery from "@mui/material/useMediaQuery";
import {
  Box,
  AppBar,
  Toolbar,
  Drawer,
  IconButton,
  ClickAwayListener,
  Stack,
  Typography,
} from "@mui/material";
import MenuIcon from "@mui/icons-material/Menu";
import { CaretDoubleRight } from "@phosphor-icons/react";
import { useZenPrimaryLayoutContext } from "./ZenPrimaryLayoutProvider";
import LeftPanel from "./LeftPanel";
import ZenBreadcrumbs from "../shared/ZenBreadcrumbs";
import ZenSyncStatusIcon from "../shared/ZenSyncStatusIcon";
import IstLiveClock from "../shared/IstLiveClock";
import PremiumCalculatorPopover from "../shared/PremiumCalculatorPopover";
import IndexOptionNotificationBell from "./IndexOptionNotificationBell";
import {
  DEFAULT_ICON_SIZE,
  DEFAULT_LEFT_DRAWER_WIDTH,
  MAX_DRAWER_WIDTH,
  MIN_DRAWER_WIDTH,
  TRIGGER_AREA_WIDTH,
} from "../../constants";

const Root = styled("div")({
  display: "flex",
  height: "100vh",
  width: "100%",
  overflow: "hidden",
  position: "relative",
});

interface MainProps {
  open?: boolean;
  leftWidth: number;
}

const Main = styled(Stack, {
  shouldForwardProp: (prop) => !["open", "leftWidth"].includes(prop as string),
})<MainProps>(({ theme, open }) => ({
  flexGrow: 1,
  transition: theme.transitions.create("margin", {
    easing: theme.transitions.easing.sharp,
    duration: theme.transitions.duration.leavingScreen,
  }),
  marginLeft: 0,
  height: "100%",
  overflow: "hidden",
  position: "relative",
  ...(open && {
    transition: theme.transitions.create("margin", {
      easing: theme.transitions.easing.easeOut,
      duration: theme.transitions.duration.enteringScreen,
    }),
  }),
}));

const AppBarStyled = styled(AppBar, {
  shouldForwardProp: (prop) => !["open", "leftWidth"].includes(prop as string),
})<{ open?: boolean; leftWidth: number }>(({ theme, open }) => ({
  transition: theme.transitions.create(["margin", "width"], {
    easing: theme.transitions.easing.sharp,
    duration: theme.transitions.duration.leavingScreen,
  }),
  ...(open && {
    transition: theme.transitions.create(["margin", "width"], {
      easing: theme.transitions.easing.easeOut,
      duration: theme.transitions.duration.enteringScreen,
    }),
  }),
  elevation: 0,
  boxShadow: "none",
  pointerEvents: "auto",
  zIndex: theme.zIndex.appBar,
  flexShrink: 0,
}));

const ResizeHandle = styled("div")(({ theme }) => ({
  position: "absolute",
  top: 0,
  bottom: 0,
  width: "10px",
  cursor: "col-resize",
  transition: "background-color 0.3s ease",
  "&:hover, &.resizing": {
    backgroundColor: theme.palette.primary.main,
  },
}));

const StyledToolbar = styled(Toolbar)(({ theme }) => ({
  padding: "0 !important",
  paddingLeft: `${theme.spacing(0.5)} !important`,
  margin: 0,
  minHeight: "64px !important",
}));

type ZenPrimaryLayoutProps = {
  children: React.ReactNode;
};

const ZenPrimaryLayout: React.FC<ZenPrimaryLayoutProps> = ({ children }) => {
  const [leftOpenOnMobile, setLeftOpenOnMobile] = useState(true);
  const [leftDocked, setLeftDocked] = useState(true);
  const [leftWidth, setLeftWidth] = useState(DEFAULT_LEFT_DRAWER_WIDTH);
  const [isResizingLeft, setIsResizingLeft] = useState(false);
  const [showUndockedDrawer, setShowUndockedDrawer] = useState(false);
  const [isMenuIconHovered, setIsMenuIconHovered] = useState(false);

  const leftPanelRef = React.useRef<HTMLDivElement>(null);
  const triggerAreaRef = React.useRef<HTMLDivElement>(null);
  const hoverTimeoutRef = React.useRef<ReturnType<typeof setTimeout> | null>(
    null,
  );

  const theme = useTheme();
  const {
    breadcrumbs,
    breadcrumbActions,
    isSyncing,
    title,
    pageActions,
  } = useZenPrimaryLayoutContext();

  const isMobile = useMediaQuery(theme.breakpoints.down("sm"));

  const handleMobileLeftDrawerOpen = useCallback(() => {
    setLeftOpenOnMobile(true);
  }, []);

  const handleMobileLeftDrawerClose = useCallback(() => {
    setLeftOpenOnMobile(false);
  }, []);

  const handleLeftDrawerDock = useCallback(() => {
    setLeftDocked((prev) => {
      const next = !prev;
      if (next) {
        setLeftOpenOnMobile(true);
        setShowUndockedDrawer(false);
        setLeftWidth(DEFAULT_LEFT_DRAWER_WIDTH);
      } else {
        setLeftOpenOnMobile(false);
        setShowUndockedDrawer(true);
      }
      return next;
    });
  }, []);

  const handleLeftMouseDown = useCallback((e: React.MouseEvent) => {
    e.preventDefault();
    setIsResizingLeft(true);
    document.body.style.cursor = "col-resize";
  }, []);

  const handleLeftMouseMove = useCallback(
    (e: MouseEvent) => {
      if (!isResizingLeft) return;
      const newWidth = e.clientX;
      if (newWidth >= MIN_DRAWER_WIDTH && newWidth <= MAX_DRAWER_WIDTH) {
        setLeftWidth(newWidth);
      }
    },
    [isResizingLeft],
  );

  const handleLeftMouseUp = useCallback(() => {
    setIsResizingLeft(false);
    document.body.style.cursor = "default";
  }, []);

  const handleMouseEnter = () => {
    if (hoverTimeoutRef.current) clearTimeout(hoverTimeoutRef.current);
    setShowUndockedDrawer(true);
    setIsMenuIconHovered(true);
  };

  const handleMouseLeave = () => {
    if (isResizingLeft) return;
    if (hoverTimeoutRef.current) clearTimeout(hoverTimeoutRef.current);
    hoverTimeoutRef.current = setTimeout(() => setShowUndockedDrawer(false), 300);
    setIsMenuIconHovered(false);
  };

  const handleClickAway = (event: MouseEvent | TouchEvent) => {
    if (isResizingLeft) return;
    if (
      leftPanelRef.current &&
      !leftPanelRef.current.contains(event.target as Node) &&
      triggerAreaRef.current &&
      !triggerAreaRef.current.contains(event.target as Node)
    ) {
      setShowUndockedDrawer(false);
    }
  };

  useEffect(() => {
    if (isMobile) {
      setLeftDocked(false);
      setLeftOpenOnMobile(false);
    }
  }, [isMobile]);

  useEffect(() => {
    if (isResizingLeft) {
      document.addEventListener("mousemove", handleLeftMouseMove);
      document.addEventListener("mouseup", handleLeftMouseUp);
    }
    return () => {
      document.removeEventListener("mousemove", handleLeftMouseMove);
      document.removeEventListener("mouseup", handleLeftMouseUp);
    };
  }, [isResizingLeft, handleLeftMouseMove, handleLeftMouseUp]);

  return (
    <Root>
      {isMobile ? (
        <Drawer
          variant="temporary"
          anchor="left"
          open={leftOpenOnMobile}
          onClose={handleMobileLeftDrawerClose}
          ModalProps={{ keepMounted: true }}
        >
          <Box sx={{ width: { xs: "75vw", sm: 280 } }}>
            <LeftPanel
              isMobile={isMobile}
              isDocked={leftDocked}
              handleMobileLeftDrawerClose={handleMobileLeftDrawerClose}
              handleLeftDrawerDock={handleLeftDrawerDock}
            />
          </Box>
        </Drawer>
      ) : (
        <>
          {leftDocked ? (
            <Drawer
              sx={{
                width: leftWidth,
                flexShrink: 0,
                "& .MuiDrawer-paper": {
                  width: leftWidth,
                  boxSizing: "border-box",
                  position: "relative",
                  height: "100%",
                  overflowX: "hidden",
                },
              }}
              variant="persistent"
              anchor="left"
              open={leftOpenOnMobile}
            >
              <LeftPanel
                isMobile={isMobile}
                isDocked={leftDocked}
                handleMobileLeftDrawerClose={handleMobileLeftDrawerClose}
                handleLeftDrawerDock={handleLeftDrawerDock}
              />
              <ResizeHandle
                className={isResizingLeft ? "resizing" : ""}
                style={{ right: "-5px" }}
                onMouseDown={handleLeftMouseDown}
              />
            </Drawer>
          ) : (
            <ClickAwayListener onClickAway={handleClickAway}>
              <Box>
                <Box
                  ref={triggerAreaRef}
                  onMouseEnter={handleMouseEnter}
                  sx={{
                    position: "fixed",
                    left: 0,
                    top: 0,
                    bottom: 0,
                    width: TRIGGER_AREA_WIDTH,
                    zIndex: theme.zIndex.drawer + 1,
                  }}
                />
                <Box
                  ref={leftPanelRef}
                  onMouseEnter={handleMouseEnter}
                  onMouseLeave={handleMouseLeave}
                  sx={{
                    position: "fixed",
                    left: showUndockedDrawer || isResizingLeft ? 0 : -leftWidth,
                    top: 0,
                    bottom: 0,
                    width: leftWidth,
                    transition: isResizingLeft
                      ? "none"
                      : theme.transitions.create("left"),
                    zIndex: theme.zIndex.drawer,
                    bgcolor: "background.paper",
                    boxShadow: 4,
                    borderTopRightRadius: "12px",
                    borderBottomRightRadius: "12px",
                    height: "calc(100% - 48px)",
                    marginTop: "auto",
                    marginBottom: "auto",
                    overflow: "hidden",
                  }}
                >
                  <LeftPanel
                    isMobile={isMobile}
                    isDocked={leftDocked}
                    handleMobileLeftDrawerClose={handleMobileLeftDrawerClose}
                    handleLeftDrawerDock={handleLeftDrawerDock}
                  />
                  <ResizeHandle
                    className={isResizingLeft ? "resizing" : ""}
                    style={{ right: "-5px" }}
                    onMouseDown={handleLeftMouseDown}
                  />
                </Box>
              </Box>
            </ClickAwayListener>
          )}
        </>
      )}

      <Main open={leftDocked && leftOpenOnMobile} leftWidth={leftWidth}>
        <AppBarStyled
          position="relative"
          open={leftDocked}
          leftWidth={leftWidth}
          color="default"
          sx={{ width: "100%", bgcolor: "background.paper" }}
        >
          <StyledToolbar>
            <Stack direction="column" spacing={1} width="100%">
              <Stack
                direction="row"
                spacing={1}
                alignItems="center"
                justifyContent="space-between"
                sx={{ px: 2.5, pr: 4 }}
                width="100%"
              >
                <Stack direction="row" spacing={1} alignItems="center">
                  {!leftDocked && (
                    <IconButton
                      aria-label="open drawer"
                      onClick={
                        isMobile
                          ? handleMobileLeftDrawerOpen
                          : handleLeftDrawerDock
                      }
                      onMouseEnter={handleMouseEnter}
                      onMouseLeave={handleMouseLeave}
                      edge="start"
                      sx={{
                        borderRadius: 1,
                        width: 2 * DEFAULT_ICON_SIZE,
                        height: 2 * DEFAULT_ICON_SIZE,
                      }}
                    >
                      {isMenuIconHovered ? (
                        <CaretDoubleRight size={DEFAULT_ICON_SIZE} />
                      ) : (
                        <MenuIcon
                          sx={{
                            width: DEFAULT_ICON_SIZE,
                            height: DEFAULT_ICON_SIZE,
                          }}
                        />
                      )}
                    </IconButton>
                  )}
                  <ZenBreadcrumbs items={breadcrumbs ?? []} />
                </Stack>
                <Stack direction="row" spacing={1} alignItems="center">
                  {breadcrumbActions}
                  <IstLiveClock />
                  <PremiumCalculatorPopover />
                  <IndexOptionNotificationBell />
                  <ZenSyncStatusIcon isSyncing={isSyncing} />
                </Stack>
              </Stack>

              <Stack
                direction={isMobile ? "column" : "row"}
                alignItems={isMobile ? "flex-start" : "center"}
                justifyContent="space-between"
                width="100%"
                px={isMobile ? 3 : 3.5}
                py={0.75}
                sx={{
                  color: "text.primary",
                  borderTop: "1px solid rgba(0, 0, 0, 0.04)",
                }}
              >
                <Box sx={{ flex: 1, textAlign: "left", width: "100%" }}>
                  {typeof title === "string" ? (
                    <Typography variant="h6" sx={{ fontSize: "18px" }}>
                      {title}
                    </Typography>
                  ) : (
                    title
                  )}
                </Box>
                <Box
                  sx={{
                    display: "flex",
                    justifyContent: isMobile ? "right" : "flex-end",
                    width: isMobile ? "100%" : "auto",
                    gap: 1,
                  }}
                >
                  {pageActions}
                </Box>
              </Stack>
            </Stack>
          </StyledToolbar>
        </AppBarStyled>

        <Box
          sx={{
            flex: 1,
            minHeight: 0,
            overflow: "hidden",
            display: "flex",
            flexDirection: "column",
            px: { xs: 2, sm: 3 },
            py: 2,
            bgcolor: "grey.50",
            "& > *": {
              flex: 1,
              minHeight: 0,
              height: "100%",
            },
          }}
        >
          {children}
        </Box>
      </Main>
    </Root>
  );
};

export default ZenPrimaryLayout;
