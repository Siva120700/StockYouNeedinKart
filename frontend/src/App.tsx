import { CssBaseline, ThemeProvider, createTheme } from "@mui/material";
import { BrowserRouter, Navigate, Route, Routes } from "react-router-dom";
import ZenPrimaryLayout from "./zen_components/layout/ZenPrimaryLayout";
import { ZenPrimaryLayoutProvider } from "./zen_components/layout/ZenPrimaryLayoutProvider";
import LtpPage from "./pages/LtpPage";
import SignalsPage from "./pages/SignalsPage";
import LiquiditySignalsPage from "./pages/LiquiditySignalsPage";
import TradeScorePage from "./modules/trade-score/TradeScorePage";
import ConfluencePage from "./modules/confluence/ConfluencePage";
import BreakoutPage from "./modules/breakout/BreakoutPage";
import OutcomesPage from "./modules/outcomes/OutcomesPage";
import OptionsIntradayPage from "./modules/options-intraday/OptionsIntradayPage";
import BacktestPage from "./pages/BacktestPage";
import PositionsPage from "./pages/PositionsPage";

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
              <Route path="/signals" element={<SignalsPage />} />
              <Route path="/liquidity" element={<LiquiditySignalsPage ruleset="classic" />} />
              <Route path="/liquidity-fresh" element={<LiquiditySignalsPage ruleset="fresh" />} />
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
