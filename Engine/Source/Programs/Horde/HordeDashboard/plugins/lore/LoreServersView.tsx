// Copyright Epic Games, Inc. All Rights Reserved.

import { DetailsList, DetailsListLayoutMode, IColumn, SelectionMode, Stack, Text } from "@fluentui/react";
import { useEffect, useState } from "react";
import { Breadcrumbs } from "horde/components/Breadcrumbs";
import { TopNav } from "horde/components/TopNav";
import { getHordeStyling } from "horde/styles/Styles";
import { getLoreStatus, LoreCluster, LoreStatus } from "./api";

export const LoreServersView: React.FC = () => {

   const [status, setStatus] = useState<LoreStatus | undefined>(undefined);
   const [error, setError] = useState<string | undefined>(undefined);

   useEffect(() => {
      getLoreStatus().then(setStatus).catch(reason => setError(`Unable to load Lore status: ${reason}`));
   }, []);

   const { hordeClasses } = getHordeStyling();

   const serverColumns: IColumn[] = [
      { key: "name", name: "Cluster", fieldName: "name", minWidth: 160, maxWidth: 240, isResizable: true },
      { key: "server", name: "Server", fieldName: "serverAndPort", minWidth: 240, maxWidth: 420, isResizable: true },
      { key: "streams", name: "Streams", minWidth: 240, isResizable: true, onRender: (item: LoreCluster) => <Text>{item.streams.join(", ")}</Text> },
   ];

   const streamColumns: IColumn[] = [
      { key: "name", name: "Stream", fieldName: "name", minWidth: 160, maxWidth: 240, isResizable: true },
      { key: "repository", name: "Repository", fieldName: "repository", minWidth: 200, maxWidth: 320, isResizable: true },
      { key: "branch", name: "Branch", fieldName: "branch", minWidth: 100, maxWidth: 160, isResizable: true },
      { key: "cluster", name: "Cluster", fieldName: "cluster", minWidth: 140, maxWidth: 220, isResizable: true },
   ];

   return <Stack className={hordeClasses.horde}>
      <TopNav />
      <Breadcrumbs items={[{ text: 'Lore Servers' }]} />
      <Stack horizontalAlign="center" style={{ paddingTop: 12 }}>
         <Stack style={{ width: 1200, paddingTop: 12 }} tokens={{ childrenGap: 24 }}>
            {error && <Text style={{ color: "#d13438" }}>{error}</Text>}
            <Stack tokens={{ childrenGap: 8 }}>
               <Text variant="mediumPlus">Servers</Text>
               <DetailsList items={status?.clusters ?? []} columns={serverColumns} selectionMode={SelectionMode.none} layoutMode={DetailsListLayoutMode.justified} compact={true} />
            </Stack>
            <Stack tokens={{ childrenGap: 8 }}>
               <Text variant="mediumPlus">Streams</Text>
               <DetailsList items={status?.streams ?? []} columns={streamColumns} selectionMode={SelectionMode.none} layoutMode={DetailsListLayoutMode.justified} compact={true} />
            </Stack>
         </Stack>
      </Stack>
   </Stack>;
}
