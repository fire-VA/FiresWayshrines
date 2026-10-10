# Changelog

## 1.0.0
- Initial release. Standalone wayshrine teleport network split out of the FiresNPCs refactor.
- Buildable Wayshrine piece (runtime-cloned wood portal, recoloured gold, Jotunn-free,
  relogin-safe).
- Destination chooser listing every placed wayshrine + config-defined hub.
- Ported from VikingLands.Core TownPortal (used with permission): return-point storage,
  carry-ore transport rule, TeleportWorld repurposing patches, visual recolour.
- `Configs/Teleporters/*.cfg` hub grammar (Marketplace Teleporter schema) with server→client
  sync over routed RPC.
- Optional per-jump activation cost, per-player placement cap, minimap pins on spawn.
- Public `WayshrinesApi` (ApiMajorVersion 1) and a help/compendium section.
