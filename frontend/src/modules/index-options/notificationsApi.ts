import { gql } from "../../api/client";

export type IndexOptionNotification = {
  id: string;
  signalSource: string;
  side: string;
  asOfDate: string;
  contractStrike: number;
  contractOptionType: string;
  premiumLtp: number;
  premiumStopLoss: number | null;
  premiumTargetT1: number | null;
  confidenceScore: number;
  title: string;
  body: string;
  readAt: string | null;
  createdAt: string;
};

const FIELDS = `
  id signalSource side asOfDate contractStrike contractOptionType
  premiumLtp premiumStopLoss premiumTargetT1 confidenceScore
  title body readAt createdAt
`;

export const IndexOptionNotificationsApi = {
  async fetch(unreadOnly = true, limit = 30): Promise<IndexOptionNotification[]> {
    const data = await gql<{ indexOptionNotifications: IndexOptionNotification[] }>(
      `query ($unreadOnly: Boolean!, $limit: Int!) {
        indexOptionNotifications(unreadOnly: $unreadOnly, limit: $limit) { ${FIELDS} }
      }`,
      { unreadOnly, limit },
    );
    return data.indexOptionNotifications;
  },

  async markRead(ids: string[]): Promise<number> {
    if (ids.length === 0) return 0;
    const data = await gql<{ markIndexOptionNotificationsRead: number }>(
      `mutation ($ids: [UUID!]!) {
        markIndexOptionNotificationsRead(ids: $ids)
      }`,
      { ids },
    );
    return data.markIndexOptionNotificationsRead;
  },
};
