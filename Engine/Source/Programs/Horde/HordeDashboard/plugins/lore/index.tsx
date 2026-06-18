// Copyright Epic Games, Inc. All Rights Reserved.

import { registerHordePlugin, MountType } from "..";
import { LoreServersView } from "./LoreServersView";

registerHordePlugin({
   id: "lore",
   routes: [{ path: "lore/servers", element: <LoreServersView /> }],
   mount: {
      type: MountType.ServerMonitoring,
      text: "Lore Servers",
      route: `/lore/servers`
   }
})
