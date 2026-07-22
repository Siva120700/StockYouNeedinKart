import { Breadcrumbs, Link, Typography } from "@mui/material";
import type { BreadcrumbItem } from "../layout/ZenPrimaryLayoutProvider";

export default function ZenBreadcrumbs({ items }: { items: BreadcrumbItem[] }) {
  if (!items.length) return null;
  return (
    <Breadcrumbs aria-label="breadcrumb" sx={{ fontSize: 14 }}>
      {items.map((item, index) => {
        const isLast = index === items.length - 1;
        if (isLast) {
          return (
            <Typography key={`${item.label}-${index}`} color="text.primary" fontSize={14}>
              {item.label}
            </Typography>
          );
        }
        return (
          <Link
            key={`${item.label}-${index}`}
            component="button"
            underline="hover"
            color="inherit"
            onClick={item.onClick}
            sx={{ cursor: item.onClick ? "pointer" : "default", border: 0, background: "none", p: 0, font: "inherit" }}
          >
            {item.label}
          </Link>
        );
      })}
    </Breadcrumbs>
  );
}
