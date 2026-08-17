import { gql } from "../../api/client";
import type { AnalyzeStockResult } from "./types";

export const AnalyzeStockApi = {
  async analyze(instrumentId: string): Promise<AnalyzeStockResult> {
    const data = await gql<{ analyzeStock: AnalyzeStockResult }>(
      `query ($instrumentId: UUID!) {
        analyzeStock(instrumentId: $instrumentId) {
          instrumentId symbol name spotLtp ltpFetchedAt
          sectorInstrumentId sectorSymbol sectorName sectorConfirmed
          verdict verdictLabel verdictReasons
          primarySetup {
            source side asOfDate entry stopLoss
            targetT1 targetT2 targetT3 plannedRiskReward
          }
          levels {
            pivot resistance1 resistance2 resistance3
            support1 support2 support3
            priorDayHigh priorDayLow
            ma2d ma3d ma5d last2dHigh last2dLow
            sweptZoneType sweptZonePrice sweepSide
            nearestZoneType nearestZonePrice distancePct
            zoneTags liquidityContext
            liquidityEvalStatus liquidityEvalDetail liquidityLive
            liquidityZones { type price kind }
            breakoutLevel breakoutPattern
          }
          signal {
            side asOfDate entryPrice initialStopLoss
            targetT1 targetT2 targetT3
            volumeOk sectorConfirmed freshCross
            ma2d ma3d ma5d
          }
          liquidityFresh {
            side asOfDate entryPrice initialStopLoss
            targetT1 targetT2 targetT3
            relativeVolume rvolOk strongClose sectorConfirmed
            sweepSide sweptZoneType sweptZonePrice
            nearestZoneType nearestZonePrice distancePct
            zoneTags timeframeContext
          }
          liquidityClassic {
            side asOfDate entryPrice initialStopLoss targetT1
            zoneTags timeframeContext
          }
          confluence {
            side asOfDate entryPrice initialStopLoss
            targetT1 targetT2 targetT3
            sectorConfirmed freshCross
          }
          tradeScore {
            side asOfDate confidenceScore rating
            signalsScore liquidityScore breakoutScore futuresScore optionsScore
            reasons entryPrice initialStopLoss
            targetT1 targetT2 targetT3 breakoutConfirmed
          }
          momentumV2 {
            side asOfDate entryPrice initialStopLoss
            targetT1 targetT2 targetT3
            volumeOk sectorConfirmed freshCross momentumScore
          }
          momentumV3 {
            side asOfDate entryPrice initialStopLoss
            targetT1 targetT2 targetT3
            volumeOk sectorConfirmed freshCross momentumScore
          }
          breakout {
            side asOfDate confirmed closePrice level20d
            volumeRatio adx rsi patternType
          }
          optionsIntraday {
            side signalSource confidenceScore reasons
            contractTradingSymbol contractOptionType contractStrike
            premiumLtp delta impliedVolatility flatByIst
          }
          backtestSummary {
            timesInStrategy targetHits slHits
            targetHitRatePct avgRiskReward avgRMultiple
          }
          recentBars {
            tradeDate open high low close volume
          }
        }
      }`,
      { instrumentId },
    );
    return data.analyzeStock;
  },
};
