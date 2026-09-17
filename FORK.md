# WebMap cartography fork

Upstream is pinned at annotated tag `v2.9.0`, commit
`02c96115af19c772b732cac0e25b1e6eb72db80e`.

This local, numeric BepInEx candidate is version `2.10.0` (not an upstream
release). The generated BepInEx config contains these new safe defaults:

```ini
[Cartography]
import_cartography_pins = true

[Interval]
cartography_pin_update_interval = 5
player_update_interval = 1
```

Cartography pins are read from all `piece_cartographytable` ZDOs, deduplicated
by type, exact unmodified label, and exact coordinates, and exposed as transient
`cartography:<hash>` pins. They are not written to `pins.csv`; each corrupt
table retains its own last-good snapshot while healthy tables continue to
refresh. Disabling import removes only those transient pins.

`/players` and player WebSocket frames include current `stamina` and `eitr` as
nullable numbers. They are sourced from replicated player ZDO data; a missing,
negative, infinite, or invalid value is represented as `null` and rendered as
`—` in the player list.

## Candidate status

The source and browser bundle passes its fixture and browser-build checks. C# DLL
and package validation for this candidate still requires a local .NET SDK and
the BepInEx/Valheim reference assemblies; no pre-existing archive represents
this candidate. Earlier DLL compatibility checks used BepInEx and Valheim
reference assemblies copied from the isolated Valheim 1.0.7 server. The exact
Valheim 1.0.7 `MapTable` shared-map wire format
was also checked against that server's assembly: version 3 stores owner, name,
position, pin type, checked state, and platform author in the order decoded by
`CartographyTablePins`.

Produce the 2.10.0 archive and checksum with the local packaging check only
after that validation succeeds. Runtime compatibility remains pending
until the staged candidate is activated in the isolated server under a
separately approved stop/start operation. Do not deploy it to production based
on build evidence alone.
