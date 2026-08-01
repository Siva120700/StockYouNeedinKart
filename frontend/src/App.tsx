import { CssBaseline, ThemeProvider, createTheme } from "@mui/material";
import { BrowserRouter, Navigate, Route, Routes } from "react-router-dom";
import ZenPrimaryLayout from "./zen_components/layout/ZenPrimaryLayout";
import { ZenPrimaryLayoutProvider } from "./zen_components/layout/ZenPrimaryLayoutProvider";
import AnalyzeStockPage from "./modules/analyze-stock/AnalyzeStockPage";
import BreakoutPage from "./modules/breakout/BreakoutPage";
import ConfluencePage from "./modules/confluence/ConfluencePage";
import OptionsIntradayPage from "./modules/options-intraday/OptionsIntradayPage";
import OutcomesPage from "./modules/outcomes/OutcomesPage";
import TradeScorePage from "./modules/trade-score/TradeScorePage";
import BacktestPage from "./pages/BacktestPage";
import LiquiditySignalsPage from "./pages/LiquiditySignalsPage";
import LtpPage from "./pages/LtpPage";
import PositionsPage from "./pages/PositionsPage";
import SignalsPage from "./pages/SignalsPage";

const theme = createTheme({
  palette: {
    mode: "light",
    primary: { main: "#1b5e40" },
    secondary: { main: "#d4a017" },
    background: { default: "#f5f7f6", paper: "#ffffff" },
  },
  typography: {
    fontFamily: '"DM Sans", "Segoe UI", system-ui, sans-serif',
  },
  shape: { borderRadius: 8 },
});

export default function App() {
  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <BrowserRouter>
        <ZenPrimaryLayoutProvider>
          <ZenPrimaryLayout>
            <Routes>
              <Route path="/" element={<Navigate to="/ltp" replace />} />
              <Route path="/ltp" element={<LtpPage />} />
              <Route path="/analyze" element={<AnalyzeStockPage />} />
              <Route path="/signals" element={<SignalsPage />} />
              <Route path="/liquidity" element={<LiquiditySignalsPage ruleset="classic" />} />
              <Route path="/liquidity-fresh" element={<LiquiditySignalsPage ruleset="fresh" />} />
              <Route path="/liquidity-v2" element={<LiquiditySignalsPage ruleset="v2" />} />
              <Route path="/confluence" element={<ConfluencePage />} />
              <Route path="/breakout" element={<BreakoutPage />} />
              <Route path="/trade-score" element={<TradeScorePage />} />
              <Route path="/accuracy" element={<OutcomesPage />} />
              <Route path="/options-intraday" element={<OptionsIntradayPage />} />
              <Route path="/backtest" element={<BacktestPage />} />
              <Route path="/positions" element={<PositionsPage />} />
            </Routes>
          </ZenPrimaryLayout>
        </ZenPrimaryLayoutProvider>
      </BrowserRouter>
    </ThemeProvider>
  );
}
