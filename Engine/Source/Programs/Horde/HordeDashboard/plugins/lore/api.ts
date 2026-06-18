// Copyright Epic Games, Inc. All Rights Reserved.

import backend from "horde/backend";

export type LoreStream = {
   id: string;
   name: string;
   cluster?: string;
   branch: string;
   repository?: string;
};

export type LoreCluster = {
   name: string;
   serverAndPort: string;
   streams: string[];
};

export type LoreStatus = {
   clusters: LoreCluster[];
   streams: LoreStream[];
};

export async function getLoreStatus(): Promise<LoreStatus> {
   const response = await backend.fetch.get(`/api/v1/lore/status`);
   return response.data as LoreStatus;
}
