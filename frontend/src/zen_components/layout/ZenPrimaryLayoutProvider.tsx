import {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useState,
  type ReactNode,
} from "react";

export type BreadcrumbItem = {
  label: string;
  href?: string;
  onClick?: () => void;
};

type ZenPrimaryLayoutContextValue = {
  title: ReactNode;
  setTitle: (title: ReactNode) => void;
  breadcrumbs: BreadcrumbItem[];
  setBreadcrumbs: (items: BreadcrumbItem[]) => void;
  pageActions: ReactNode;
  setPageActions: (actions: ReactNode) => void;
  breadcrumbActions: ReactNode;
  setBreadcrumbActions: (actions: ReactNode) => void;
  isSyncing: boolean;
  setIsSyncing: (v: boolean) => void;
};

const ZenPrimaryLayoutContext =
  createContext<ZenPrimaryLayoutContextValue | null>(null);

export function ZenPrimaryLayoutProvider({
  children,
}: {
  children: ReactNode;
}) {
  const [title, setTitle] = useState<ReactNode>("StockYouNeed");
  const [breadcrumbs, setBreadcrumbs] = useState<BreadcrumbItem[]>([
    { label: "Home" },
  ]);
  const [pageActions, setPageActions] = useState<ReactNode>(null);
  const [breadcrumbActions, setBreadcrumbActions] = useState<ReactNode>(null);
  const [isSyncing, setIsSyncing] = useState(false);

  const value = useMemo(
    () => ({
      title,
      setTitle,
      breadcrumbs,
      setBreadcrumbs,
      pageActions,
      setPageActions,
      breadcrumbActions,
      setBreadcrumbActions,
      isSyncing,
      setIsSyncing,
    }),
    [title, breadcrumbs, pageActions, breadcrumbActions, isSyncing],
  );

  return (
    <ZenPrimaryLayoutContext.Provider value={value}>
      {children}
    </ZenPrimaryLayoutContext.Provider>
  );
}

export function useZenPrimaryLayoutContext() {
  const ctx = useContext(ZenPrimaryLayoutContext);
  if (!ctx) {
    throw new Error(
      "useZenPrimaryLayoutContext must be used within ZenPrimaryLayoutProvider",
    );
  }
  return ctx;
}

/** Helper hook for pages to set layout chrome on mount. */
export function useZenPageChrome(options: {
  title: ReactNode;
  breadcrumbs?: BreadcrumbItem[];
  pageActions?: ReactNode;
}) {
  const {
    setTitle,
    setBreadcrumbs,
    setPageActions,
  } = useZenPrimaryLayoutContext();

  const apply = useCallback(() => {
    setTitle(options.title);
    setBreadcrumbs(options.breadcrumbs ?? [{ label: "Home" }]);
    setPageActions(options.pageActions ?? null);
  }, [
    options.title,
    options.breadcrumbs,
    options.pageActions,
    setTitle,
    setBreadcrumbs,
    setPageActions,
  ]);

  return { apply, setPageActions, setIsSyncing: useZenPrimaryLayoutContext().setIsSyncing };
}
