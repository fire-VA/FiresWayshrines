# FiresWayshrines

A buildable **wayshrine teleport network** for Valheim. Part of the Fires mod family;
hard-depends on **FiresUnifiedCore**.

Build a Wayshrine from the Hammer. Channel any wayshrine to open a destination list and
travel to **any other wayshrine in the world** or to an **admin-defined hub**. Mechanics
(return-point storage, the carry-ore restriction, the portal-repurposing teleport patches,
the recolour) are ported from VikingLands.Core's TownPortal feature (used with permission,
re-namespaced); the multi-destination hub model and the `Configs/Teleporters/*.cfg` grammar
follow the Marketplace Teleporter schema.

## Features
- **Buildable Wayshrine piece** (Hammer → Misc). v1 clones the vanilla wood portal at
  runtime (no asset bundle), recolours it gold, and registers it Jotunn-free so placed
  shrines survive relogin (registered into `ZNetScene` before `ZDOMan.Load`).
- **Destination network** — every placed wayshrine is discovered from its replicated ZDO
  (no custom sync), plus fixed hubs from config. Pick a destination from the chooser to jump.
- **Transport rules** — carrying non-teleportable items (ore/metal) blocks travel unless
  `AllowNonTeleportableItems` is on (the Marketplace `CanTeleportWithOre` option).
- **Activation cost** — optional per-jump toll (item + amount) charged from the inventory.
  `0` = free. This is the seam a future RPGmaker-banker soft-dep would charge through instead.
- **Return point** — your pre-teleport position is stored in your character data for a recall.
- **Minimap pins** for every known wayshrine + hub, dropped when you spawn.
- **Help section** registered into the shared Fires compendium.

## Config (`BepInEx/config/com.Fire.FiresWayshrines.cfg`, server-synced)
| Key | Default | Notes |
| --- | --- | --- |
| Enabled | true | Master switch. |
| AllowBuilding | true | Wayshrine appears in the Hammer. |
| MaxPerPlayer | 0 | Per-player placement cap (0 = unlimited). |
| BuildRequirements | FineWood:20,GreydwarfEye:10,SurtlingCore:2 | `prefab:amount` list. |
| AllowNonTeleportableItems | false | Allow teleporting with ore/metal. |
| SaveReturnPoint | true | Store pre-teleport position for recall. |
| ActivationCostItem / ActivationCostAmount | Coins / 0 | Per-jump toll; 0 = free. |
| ShowMinimapPins | true | Pin every wayshrine on spawn. |

## Hub destinations — `BepInEx/config/FiresWayshrines/Teleporters/*.cfg`
```cfg
[spawn_hub]
Black Forest Portal,1500,30,-2200,PortalIcon
Swamp Entrance <speed=8>,-800,25,3400
@from:meadows_hub
```
- `[hub]` — a named hub profile.
- `Name,x,y,z[,sprite]` — a destination pin. The name may contain spaces and an inline
  `<speed=N>` tag (`<= 0` = instant). Coordinates are integer world positions.
- `@from:otherhub` — import another hub's pins.
- Files are watched/served by the server to clients over routed RPC.

## Public API (`FiresWayshrines.WayshrinesApi`, `ApiMajorVersion = 1`)
`OpenSelection(player)`, `OpenSelectionForProfile(player, profile)`,
`GetProfileNames()`, `TeleportToNamed(player, name, out reason)` — for a consumer mod
(e.g. an NPC "Teleporter" type) to drive the network without a hard reference.

## Build
Old-style csproj; every new `.cs` must be added to `<Compile Include>`. `assembly_utils` is
pinned to `..\..\assembly_valheim\lib\assembly_utils.dll` (branch-deterministic
`GetStableHashCode` IL). Build with the no-hang flags:
```
MSBuild FiresWayshrines.csproj /p:Configuration=Debug /p:UseSharedCompilation=false /nodeReuse:false /m:1
```
A post-build target deploys the DLL+PDB into the BlueHills r2modman test profile.

## Not yet wired (next steps)
- **FiresNPCs bridge** — `NpcType.Teleporter` + `Bridges/WayshrinesBridge.cs` so an NPC can
  be a wayshrine hub front-end (calls `WayshrinesApi`). Deferred until the standalone mod is
  verified in-game.
- **Custom model** — v1 reuses the wood-portal mesh; a baked `fireswayshrines` bundle prefab
  is a later asset task.
- **uGUI chooser** — v1 selection UI is IMGUI; migrate onto Core's UI builder.
- **Thunderstore** — add the FiresUnifiedCore dependency string to `thunderstore/manifest.json`
  before publishing (it is a hard dependency).
