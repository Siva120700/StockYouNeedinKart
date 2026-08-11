import { gql } from "../../api/client";
import type { MarketNewsItem } from "./types";

export const NewsApi = {
  async fetchNews(limit = 40): Promise<MarketNewsItem[]> {
    const data = await gql<{ marketNews: MarketNewsItem[] }>(
      `
      query MarketNews($limit: Int) {
        marketNews(limit: $limit) {
          id
          title
          summary
          url
          source
          publishedAt
        }
      }
    `,
      { limit },
    );
    return data.marketNews ?? [];
  },
};
