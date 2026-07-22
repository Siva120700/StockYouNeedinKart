import {
  TablePagination,
  Menu,
  MenuItem,
  Typography,
  Box,
  alpha,
  ClickAwayListener,
} from "@mui/material";
import React, { useEffect, useState } from "react";
import { DEFAULT_ICON_SIZE } from "../../constants";

export interface ZenPaginationProps {
  pageSize?: number;
  currentPage?: number;
  totalCount?: number;
  onPageChange?: (page: number) => void;
  onPageSizeChange?: (newPageSize: number) => void;
}

const ZenPagination: React.FC<ZenPaginationProps> = ({
  pageSize = 10,
  currentPage = 0,
  totalCount = 0,
  onPageChange,
  onPageSizeChange,
}) => {
  const [anchorEl, setAnchorEl] = useState<null | HTMLElement>(null);

  useEffect(() => {
    if (totalCount === 0) {
      return;
    }
    const maxPageIndex = Math.max(Math.ceil(totalCount / pageSize) - 1, 0);
    if (currentPage > maxPageIndex) {
      onPageChange?.(maxPageIndex);
    }
  }, [currentPage, pageSize, totalCount, onPageChange]);

  const handleMenuOpen = (event: React.MouseEvent<HTMLElement>) => {
    setAnchorEl(event.currentTarget);
  };

  const handleMenuClose = () => {
    setAnchorEl(null);
  };

  const handleFirstPageClick = () => {
    onPageChange?.(0);
    handleMenuClose();
  };

  const handleLastPageClick = () => {
    const lastPageIndex = Math.max(Math.ceil(totalCount / pageSize) - 1, 0);
    onPageChange?.(lastPageIndex);
    handleMenuClose();
  };

  const handlePageSizeChange = (newPageSize: number) => {
    if (pageSize === newPageSize) {
      handleMenuClose();
      return;
    }
    const currentFirstItemIndex = currentPage * pageSize;
    const newPageIndex = Math.floor(currentFirstItemIndex / newPageSize);
    const maxPageIndex = Math.max(Math.ceil(totalCount / newPageSize) - 1, 0);

    onPageSizeChange?.(newPageSize);
    onPageChange?.(Math.min(newPageIndex, maxPageIndex));
    handleMenuClose();
  };

  const handleMenuItemClick = (event: React.MouseEvent<HTMLElement>) => {
    event.stopPropagation();
  };

  const getPageSizeOptions = () => [10, 25, 50, 100];

  const getSelectedPage = () => {
    let selectedPage = Math.min(
      currentPage,
      Math.ceil(totalCount / pageSize) - 1,
    );

    if (selectedPage < 0) {
      selectedPage = 0;
    }

    return selectedPage;
  };

  if (totalCount === 0) {
    return null;
  }

  return (
    <Box>
      <TablePagination
        component="div"
        count={totalCount}
        page={getSelectedPage()}
        onPageChange={(_, page) => onPageChange?.(page)}
        rowsPerPage={pageSize}
        rowsPerPageOptions={[]}
        sx={{
          "& .MuiTablePagination-actions": {
            marginLeft: "0px",
            fontSize: DEFAULT_ICON_SIZE,
          },
          "& .MuiSvgIcon-root": {
            fontSize: DEFAULT_ICON_SIZE,
          },
          "& .MuiTablePagination-toolbar": {
            minHeight: "36px",
            minWidth: "200px",
            height: "36px",
            padding: "0 4px",
          },
        }}
        labelDisplayedRows={({ from, to, count }) => (
          <ClickAwayListener onClickAway={handleMenuClose}>
            <Box
              component="span"
              onClick={handleMenuOpen}
              sx={{
                position: "relative",
                display: "inline-flex",
                alignItems: "center",
                cursor: "pointer",
                padding: "5px",
                borderRadius: "4px",
                ":hover": {
                  backgroundColor: (theme) =>
                    alpha(theme.palette.action.hover, 1),
                },
              }}
            >
              <Typography component="span" variant="body2" sx={{ mr: 1 }}>
                {count > 0
                  ? `${Math.min(from, count)}-${Math.min(to, count)} of ${count}`
                  : "No results"}
              </Typography>
              <Menu
                anchorEl={anchorEl}
                open={Boolean(anchorEl)}
                onClose={handleMenuClose}
                onClick={handleMenuItemClick}
                sx={{
                  "& .MuiPaper-root": {
                    width: 200,
                    mt: 1,
                    position: "relative",
                    overflow: "visible",
                    bgcolor: (theme) => theme.palette.background.paper,
                    backgroundImage: "none !important",
                    "&::before": {
                      content: '""',
                      display: "block",
                      position: "absolute",
                      top: 0,
                      left: 14,
                      width: 10,
                      height: 10,
                      bgcolor: (theme) => theme.palette.background.paper,
                      transform: "translateY(-50%) rotate(45deg)",
                      zIndex: 0,
                    },
                    "& .MuiMenuItem-root": {
                      "&.Mui-selected": {
                        color: "primary.main",
                      },
                    },
                  },
                }}
              >
                <MenuItem
                  onClick={handleFirstPageClick}
                  disabled={currentPage === 0}
                >
                  First
                </MenuItem>
                <MenuItem
                  onClick={handleLastPageClick}
                  disabled={
                    currentPage === Math.ceil(totalCount / pageSize) - 1
                  }
                >
                  Last
                </MenuItem>
                <Box component="hr" sx={{ my: 1, borderColor: "divider" }} />
                {getPageSizeOptions().map((size) => (
                  <MenuItem
                    key={size}
                    onClick={() => handlePageSizeChange(size)}
                    selected={pageSize === size}
                  >
                    {size} rows
                  </MenuItem>
                ))}
              </Menu>
            </Box>
          </ClickAwayListener>
        )}
      />
    </Box>
  );
};

export default ZenPagination;
