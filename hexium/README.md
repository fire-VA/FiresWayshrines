# FiresWayshrines

**Updated for Valheim 1.0.**

A fast-travel network for Valheim. Discover wayshrines by finding them, then travel between any two you know
for a coin toll.

## How it works

- **Discover.** Walk within about 20 m of a wayshrine. You get "Discovered: <name>" and the shrine pins to your
  map. Shrines name themselves after their biome, so the name tells you roughly where it is.
- **Travel.** Press E on any wayshrine and the map opens, ready for a destination. Left-click a discovered
  shrine's pin. A confirm popup shows the destination, distance, any cooldown multiplier and the coin cost.
  Cancel to pick another destination; closing the map stops the pick.
- **Safe arrival.** You land in a small ring around the destination shrine, on its floor, never in deep water,
  on lava or up on a roof.
- **Build your own.** The wayshrine piece is on the hammer's Misc tab. Shrines you place join the network for
  everyone who discovers them.
- **Hidden until found.** Undiscovered shrines don't show on the map. An option shows them as locked "???"
  pins instead.

## Dungeon networks

Some shrines belong to a closed network, such as the shrines inside one dungeon. A network's inner shrines only
connect to each other and to the network's parent shrine, once discovered, and never show on the world map.
The parent shrine is also on the world network, so it is the way in and out.

## The toll

- Cost = distance x `CoinsPerMeter` (default 0.25, so 1 km costs 250 Coins), never less than `MinimumCost`
  (default 25). Coins are taken from your inventory when you confirm.
- Rapid-fire travel gets expensive: recent trips inside a sliding 10-minute window raise the price. The first is
  normal, the second about 2x, the third about 4x, the fifth about 9x. Wait and it decays.
- Worlds with the No Craft Cost modifier waive the toll (on by default).

## Travel rules

Each is on by default and can be turned off:

- Carrying items that can't go through portals (ore and the like) blocks wayshrine travel too.
- The No Portals world modifier disables wayshrines.
- With No Boss Portals set, an active boss fight blocks travel.

## Scroll of Town Portal

A consumable that offers a Bed / Wayshrine / Cancel choice and teleports you for free. Its recipe needs a cheat
sword, so in practice only admins can make it, for handing out as rewards or events.

## Requires

- BepInExPack for Valheim
- FiresUnifiedCore 0.2.389 or newer

## Installation

Install with a mod manager, or copy the plugin folder into `BepInEx/plugins`. Install it on every client and on
the dedicated server, so the shrine network and discoveries are shared.

## Configuration

Settings live in the mod's `.cfg` file in `BepInEx/config` and can be edited in the Fires config window (F1).

- `[Wayshrines.Discovery] DiscoveryRange` (20 m) - how close you must walk to discover a shrine.
- `[Wayshrines.Cost] CoinsPerMeter` (0.25), `MinimumCost` (25), `WaiveCostInCreative` (on).
- `[Wayshrines.Cooldown] WindowSeconds` (600) and `Exponent` (1.5) - the repeat-travel price curve.
- `[Wayshrines.Rules] RespectNoTeleportItems`, `RespectNoPortalsKey`, `RespectBossLockout` (all on).
- `[Wayshrines.Map] ShowUndiscoveredAsLocked` (off) - show unknown shrines as locked pins. Takes effect
  immediately, no relog.

## For admins

Hold Alt+Shift and right-click a shrine for its admin options:

- Rename, set a fixed teleport cost (0 = free, blank = automatic) or a per-shrine cooldown.
- Auto-discover on/off - everyone knows the shrine as soon as it loads.
- Admin-only on/off - hides the pin and blocks use for non-admins.
- Set trigger size for trigger-box shrines (Right Shift + scroll while placing).
- Network group and Group parent - build closed networks such as a dungeon's shrines.
- Wipe map marker for all players.

Changes reach every online player within a couple of seconds. Alt+right-click a shrine's map pin to rename it.

**Coordinate hubs.** Fixed destinations that aren't physical shrines (town squares, event arenas) are defined in
`.cfg` files under `BepInEx/config/FiresWayshrines/Teleporters` (an `example.cfg` is created for you). Marketplace
teleporter files in `BepInEx/config/Marketplace/Configs/Teleporters` load too, unchanged. Files reload while the
game runs.

The in-game Fires help panel ("? Help" on the pause menu) has a Wayshrines section; its admin page shows for
admins.

## Licence

MIT License, Copyright (c) 2025 fire-VA. See the LICENSE file.

## Issues / questions

https://discord.gg/zQRgHbqms4
