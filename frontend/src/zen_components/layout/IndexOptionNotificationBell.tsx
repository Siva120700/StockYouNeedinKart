import { useCallback, useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import {
  Badge,
  Box,
  IconButton,
  List,
  ListItem,
  ListItemButton,
  ListItemText,
  Menu,
  MenuItem,
  Snackbar,
  Typography,
} from "@mui/material";
import { Bell } from "@phosphor-icons/react";
import { DEFAULT_SMALL_ICON_SIZE } from "../../constants";
import {
  IndexOptionNotificationsApi,
  type IndexOptionNotification,
} from "../../modules/index-options/notificationsApi";

const POLL_MS = 45_000;
const SEEN_KEY = "syn_index_notif_seen";

function loadSeen(): Set<string> {
  try {
    const raw = sessionStorage.getItem(SEEN_KEY);
    if (!raw) return new Set();
    return new Set(JSON.parse(raw) as string[]);
  } catch {
    return new Set();
  }
}

function saveSeen(seen: Set<string>) {
  sessionStorage.setItem(SEEN_KEY, JSON.stringify([...seen].slice(-200)));
}

function isMarketHoursIst(): boolean {
  const ist = new Date(
    new Date().toLocaleString("en-US", { timeZone: "Asia/Kolkata" }),
  );
  const day = ist.getDay();
  if (day === 0 || day === 6) return false;
  const mins = ist.getHours() * 60 + ist.getMinutes();
  return mins >= 9 * 60 + 10 && mins <= 15 * 60 + 35;
}

async function ensureBrowserPermission(): Promise<boolean> {
  if (typeof Notification === "undefined") return false;
  if (Notification.permission === "granted") return true;
  if (Notification.permission === "denied") return false;
  const p = await Notification.requestPermission();
  return p === "granted";
}

function pushBrowserNotification(n: IndexOptionNotification) {
  if (typeof Notification === "undefined" || Notification.permission !== "granted")
    return;
  try {
    new Notification(n.title, {
      body: n.body,
      tag: n.id,
    });
  } catch {
    /* ignore */
  }
}

export default function IndexOptionNotificationBell() {
  const navigate = useNavigate();
  const [items, setItems] = useState<IndexOptionNotification[]>([]);
  const [unreadCount, setUnreadCount] = useState(0);
  const [anchor, setAnchor] = useState<null | HTMLElement>(null);
  const [toast, setToast] = useState<IndexOptionNotification | null>(null);
  const seenRef = useRef(loadSeen());
  const pollingRef = useRef(false);

  const poll = useCallback(async () => {
    if (pollingRef.current) return;
    pollingRef.current = true;
    try {
      const unread = await IndexOptionNotificationsApi.fetch(true, 30);
      setItems(unread);
      setUnreadCount(unread.length);

      for (const n of unread) {
        if (seenRef.current.has(n.id)) continue;
        seenRef.current.add(n.id);
        saveSeen(seenRef.current);
        setToast(n);
        pushBrowserNotification(n);
      }
    } catch {
      /* silent — API may be down */
    } finally {
      pollingRef.current = false;
    }
  }, []);

  useEffect(() => {
    void ensureBrowserPermission();
    void poll();
    const id = window.setInterval(() => {
      if (isMarketHoursIst() || document.visibilityState === "visible") void poll();
    }, POLL_MS);
    return () => window.clearInterval(id);
  }, [poll]);

  async function markAllRead() {
    const ids = items.map((x) => x.id);
    await IndexOptionNotificationsApi.markRead(ids);
    setItems([]);
    setUnreadCount(0);
    setAnchor(null);
  }

  async function openItem(n: IndexOptionNotification) {
    await IndexOptionNotificationsApi.markRead([n.id]);
    setItems((prev) => prev.filter((x) => x.id !== n.id));
    setUnreadCount((c) => Math.max(0, c - 1));
    setAnchor(null);
    navigate("/index-options");
  }

  return (
    <>
      <IconButton
        size="small"
        aria-label="Index option alerts"
        onClick={(e) => setAnchor(e.currentTarget)}
        sx={{ borderRadius: 1 }}
      >
        <Badge color="error" badgeContent={unreadCount} max={9}>
          <Bell size={DEFAULT_SMALL_ICON_SIZE} />
        </Badge>
      </IconButton>

      <Menu
        anchorEl={anchor}
        open={Boolean(anchor)}
        onClose={() => setAnchor(null)}
        PaperProps={{ sx: { width: 360, maxHeight: 420 } }}
      >
        <Box px={2} py={1} display="flex" justifyContent="space-between" alignItems="center">
          <Typography variant="subtitle2">Index option alerts</Typography>
          {items.length > 0 && (
            <MenuItem dense onClick={() => void markAllRead()} sx={{ minHeight: 0, py: 0.5 }}>
              Mark all read
            </MenuItem>
          )}
        </Box>
        {items.length === 0 ? (
          <MenuItem disabled>
            <ListItemText primary="No high-probability strikes yet" />
          </MenuItem>
        ) : (
          <List dense disablePadding>
            {items.map((n) => (
              <ListItem key={n.id} disablePadding>
                <ListItemButton onClick={() => void openItem(n)}>
                  <ListItemText
                    primary={n.title}
                    secondary={n.body}
                    primaryTypographyProps={{ variant: "body2", fontWeight: 600 }}
                    secondaryTypographyProps={{ variant: "caption" }}
                  />
                </ListItemButton>
              </ListItem>
            ))}
          </List>
        )}
      </Menu>

      <Snackbar
        open={!!toast}
        autoHideDuration={8000}
        onClose={() => setToast(null)}
        message={toast ? `${toast.title} — ${toast.body}` : ""}
        anchorOrigin={{ vertical: "top", horizontal: "right" }}
        action={
          toast ? (
            <Typography
              component="button"
              variant="caption"
              sx={{ color: "inherit", cursor: "pointer", border: 0, bgcolor: "transparent" }}
              onClick={() => {
                if (toast) void openItem(toast);
                setToast(null);
              }}
            >
              View
            </Typography>
          ) : undefined
        }
      />
    </>
  );
}
