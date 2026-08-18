import { useEffect, useMemo, useState } from "react";
import {
  Alert,
  Box,
  Button,
  Divider,
  Link,
  Stack,
  TextField,
  Typography,
} from "@mui/material";
import { ArrowClockwise, ArrowSquareOut } from "@phosphor-icons/react";
import { useZenPrimaryLayoutContext } from "../../zen_components/layout/ZenPrimaryLayoutProvider";
import PageFrame from "../../zen_components/layout/PageFrame";
import { DEFAULT_SMALL_ICON_SIZE } from "../../constants";
import { NewsApi } from "./api";
import { formatRelativeTime, type MarketNewsItem } from "./types";

export default function NewsPage() {
  const { setTitle, setBreadcrumbs, setPageActions, setIsSyncing } =
    useZenPrimaryLayoutContext();
  const [rows, setRows] = useState<MarketNewsItem[]>([]);
  const [filter, setFilter] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function refresh() {
    setError(null);
    setIsSyncing(true);
    try {
      const items = await NewsApi.fetchNews(50);
      setRows(items);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setLoading(false);
      setIsSyncing(false);
    }
  }

  const visible = useMemo(() => {
    const q = filter.trim().toLowerCase();
    if (!q) return rows;
    return rows.filter(
      (r) =>
        r.title.toLowerCase().includes(q) ||
        r.summary.toLowerCase().includes(q) ||
        r.source.toLowerCase().includes(q),
    );
  }, [rows, filter]);

  useEffect(() => {
    setTitle("News");
    setBreadcrumbs([{ label: "Home" }, { label: "News" }]);
    void refresh();
    return () => setPageActions(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    setPageActions(
      <Stack direction="row" spacing={1} alignItems="center">
        <TextField
          size="small"
          label="Filter"
          placeholder="Nifty, Sensex…"
          value={filter}
          onChange={(e) => setFilter(e.target.value)}
          sx={{ minWidth: 180 }}
        />
        <Button
          size="small"
          variant="contained"
          startIcon={<ArrowClockwise size={DEFAULT_SMALL_ICON_SIZE} />}
          onClick={() => void refresh()}
          disabled={loading}
        >
          Refresh
        </Button>
      </Stack>,
    );
  }, [filter, loading]);

  return (
    <PageFrame scroll>
      {error && <Alert severity="error">{error}</Alert>}
      {!error && !loading && visible.length === 0 && (
        <Alert severity="info">No headlines matched.</Alert>
      )}
      {visible.map((item, index) => (
        <Box key={item.id}>
          {index > 0 && <Divider sx={{ mb: 1.5 }} />}
          <Stack spacing={0.5}>
            <Stack direction="row" spacing={1} alignItems="baseline" flexWrap="wrap">
              <Typography variant="caption" color="text.secondary">
                {item.source}
              </Typography>
              <Typography variant="caption" color="text.secondary">
                ·
              </Typography>
              <Typography variant="caption" color="text.secondary">
                {formatRelativeTime(item.publishedAt)}
              </Typography>
            </Stack>
            <Link
              href={item.url}
              target="_blank"
              rel="noopener noreferrer"
              underline="hover"
              color="inherit"
              sx={{ display: "inline-flex", alignItems: "center", gap: 0.75 }}
            >
              <Typography variant="subtitle1" fontWeight={600} component="span">
                {item.title}
              </Typography>
              <ArrowSquareOut size={DEFAULT_SMALL_ICON_SIZE} />
            </Link>
            {item.summary && (
              <Typography variant="body2" color="text.secondary">
                {item.summary}
              </Typography>
            )}
          </Stack>
        </Box>
      ))}
    </PageFrame>
  );
}
