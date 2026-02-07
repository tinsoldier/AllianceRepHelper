# Alliance Rep Helper

A Space Engineers mod that lets faction leaders align their faction with a configured NPC faction via a simple chat command. Designed for servers using Modular Encounters Systems (MES) and Alliance-style territorial gameplay where two or more NPC factions compete for territory.

Instead of relying on slow organic reputation grinding or requiring an admin to manually adjust faction relationships, a faction's Founder or Leader can commit their faction to an alliance with a single command.

## How It Works

When a faction Founder or Leader runs `/alliance <FactionTag>`:

1. Their **faction's** reputation with the **chosen NPC faction** is set to the configured ally value (default: **+1500**).
2. Their **faction's** reputation with **all other configured NPC factions** is set to the configured enemy value (default: **-1500**).
3. The choice is recorded and persisted per-world.

### Requirements

- The player **must belong to a faction**. Factionless players cannot use the command.
- The player **must be a Founder or Leader** of their faction. Regular members cannot change faction alliances.
- The target faction must be in the server's configured list of allowed NPC factions.

This is a **faction-to-faction** reputation change, not a personal one. All members of the player's faction inherit the reputation set by their leadership.

### New Factions

When a player creates a new faction, it is automatically set to the configured default reputation (default: **-600**, Enemies) with all configured NPC factions. No action is needed -- new factions start hostile to everyone until they choose an alliance.

## Chat Commands

All commands are entered in the in-game chat. They are case-insensitive and are **not** visible to other players.

### Player Commands

| Command | Description |
|---|---|
| `/alliance <FactionTag>` | Align your faction with the specified NPC faction. Sets your faction's reputation to the configured ally value with that NPC faction and the enemy value with all other configured NPC factions. **Requires Founder or Leader role.** One-time only. |
| `/alliance list` | Shows all NPC factions available for alliance (as configured by the server owner). |
| `/alliance status` | Shows your faction's current reputation with each configured NPC faction and whether your faction has already made a choice. |
| `/alliance help` | Displays the in-game help text listing available commands. |

### Admin Commands

These commands require server admin privileges.

| Command | Description |
|---|---|
| `/alliance reset <Tag>` | Reset a specific player faction to default reputation and clear its alliance choice. The faction can then use `/alliance` again. |
| `/alliance resetall` | Reset **all** player factions to default reputation and clear all alliance choices. Every faction can then use `/alliance` again. |

### Example

```
/alliance SOBAN
```

> Your faction [MYFC] has allied with [SOBAN] Soban Fleet!
> Reputation set to 1500 with [SOBAN].
> Reputation set to -1500 with all other alliance factions.

## Setup & Installation

1. Subscribe to the mod on the Steam Workshop, **or** place the built mod folder into your world's `Mods` directory.
2. Load or restart the world/server. On first load, the mod will generate a default configuration file.
3. Edit the configuration file to list the NPC faction tags you want player factions to be able to align with.
4. Restart the world/server for configuration changes to take effect.

## Configuration

### File Location

The configuration file is stored in the **world storage** directory for the mod. The exact path depends on your setup:

- **Dedicated server:**
  `<SE Install>/Instances/<InstanceName>/Saves/<WorldName>/Storage/AllianceRepHelper_AllianceRepHelper/AllianceRepHelper.cfg`
- **Local mod (single-player / listen server):**
  `%AppData%/SpaceEngineers/Saves/<SteamId>/<WorldName>/Storage/AllianceRepHelper_AllianceRepHelper/AllianceRepHelper.cfg`
- **Workshop mod (single-player / listen server):**
  `%AppData%/SpaceEngineers/Saves/<SteamId>/<WorldName>/Storage/<WorkshopId>.sbm_AllianceRepHelper/AllianceRepHelper.cfg`

The storage folder name is derived from how the mod is loaded:
- **Local mods** (placed in the `Mods` folder by folder name): `<FolderName>_<Namespace>` -- e.g. `AllianceRepHelper_AllianceRepHelper`
- **Workshop mods** (subscribed via Steam): `<WorkshopId>.sbm_<Namespace>` -- e.g. `1234567890.sbm_AllianceRepHelper`

> **Tip:** If you can't find the storage folder, load the world once with the mod enabled and it will be created automatically with a default config file.

### File Format

The config file uses a simple `Key = Value` format. Lines starting with `#` or `//` are treated as comments.

### Default Configuration

```ini
# Alliance Rep Helper Configuration
# -----------------------------------
# Comma-separated list of faction tags that players can align with.
# These MUST be set for the mod to function.
# Example: Factions = SOBAN, KHAANEPH
Factions = 

# Reputation value granted to the chosen ally faction (default: 1500)
AllyReputation = 1500

# Reputation value set for all OTHER configured factions (default: -1500)
EnemyReputation = -1500

# Default reputation for newly created factions with all configured NPC factions (default: -600)
# New factions start negative (Enemies). Players can improve rep through gameplay
# (e.g., killing enemies of an NPC faction) or commit fully with /alliance.
# Note: The enemy/neutral threshold is around -500. Values at or below -600 reliably show as Enemies (red).
DefaultReputation = -600

# Only allow NPC factions to be selected (default: true)
# This prevents players from using the command to force relationships with player factions.
AllowOnlyNpcFactions = true
```

### Configuration Options

| Key | Type | Default | Description |
|---|---|---|---|
| `Factions` | Comma-separated strings | *(empty)* | **Required.** The NPC faction tags that player factions can choose to align with. Must match the in-game faction tag exactly. Example: `SOBAN, KHAANEPH`. The mod will not function until at least one valid faction tag is configured. |
| `AllyReputation` | Integer | `1500` | The reputation value set between the player's faction and their chosen NPC faction. |
| `EnemyReputation` | Integer | `-1500` | The reputation value set between the player's faction and all *other* configured NPC factions. |
| `DefaultReputation` | Integer | `-600` | The reputation value for newly created factions with all configured NPC factions. The enemy/neutral threshold is around -500; use -600 or lower for reliable Enemies (red) status. |
| `AllowOnlyNpcFactions` | Boolean | `true` | When `true`, factions listed in `Factions` are only allowed if they are NPC-only factions (i.e., contain no human players). Set to `false` if you need to include mixed or player factions in the list. |

### Example Configuration

For a server with two competing NPC alliances -- Soban Fleet and Khaaneph:

```ini
Factions = SOBAN, KHAANEPH
AllyReputation = 1500
EnemyReputation = -1500
DefaultReputation = -600
AllowOnlyNpcFactions = true
```

## Faction Choice Tracking

When a faction Founder or Leader uses the `/alliance` command, their **faction's ID** is recorded in a separate data file (`AllianceRepHelper_FactionChoices.dat`) in the same world storage directory. This file is a simple list of faction IDs, one per line.

**Alliance choice is one-time only.** Once a faction has chosen an alliance, the command cannot be used again for that faction. The `/alliance status` command will show whether your faction has already made a choice.

> **Admin tip:** Use `/alliance reset <Tag>` to reset a specific faction's choice in-game, or `/alliance resetall` to reset all factions. No file editing or server restart required. The faction ID is also logged when the original `/alliance` command is used.

## Planned Features (Phase 2+)

- [x] Enforce one-time alliance choice (block repeat usage of `/alliance`)
- [x] Admin override commands to reset faction choices in-game (`/alliance reset`, `/alliance resetall`)
- [x] Default reputation for newly created factions
- [x] Automatic member reputation sync on faction join
- [ ] Optional cooldown or prerequisites before allowing alignment
- [ ] Configurable neutral-reputation requirement (must be neutral with all NPC factions before choosing)

## Technical Notes

- The mod runs as a **server-side session component**. Chat commands are processed on the server via the `MessageEnteredSender` event.
- On **dedicated servers**, feedback messages are sent to the player's client over a registered network channel (`ushort 39471`). On **single-player or listen servers**, messages are displayed directly.
- Faction-to-faction reputation is set using `MyVisualScriptLogicProvider.SetRelationBetweenFactions(tagA, tagB, reputation)` from `Sandbox.Game`, which sets both the reputation value and the diplomatic relation (enemy/neutral/friendly color) in one call. Player-to-faction reputation uses `IMyFactionCollection.SetReputationBetweenPlayerAndFaction`.
- A **two-phase reputation approach** is used for new faction creation and resets: first set to -1500 (to reliably establish the Enemies relation), then adjust to the configured default on the next tick. This ensures consistent diplomatic state regardless of prior faction state, as the enemy/neutral threshold is around -500.
- Event handlers (`FactionCreated`, `FactionStateChanged`) queue work to a deferred action list processed by `UpdateAfterSimulation`, avoiding race conditions with same-tick faction creation.
- Role checks use `IMyFaction.IsFounder(identityId)` and `IMyFaction.IsLeader(identityId)`.
- Admin checks use `MyAPIGateway.Session.IsUserAdmin(steamId)`.
- The mod targets **.NET Framework 4.8** and **C# 6.0**, built with [MDK2](https://github.com/malware-dev/MDK-SE).



## Steam Workshop Description

Below is the mod description formatted in Steam Workshop BBCode. Copy everything between the `<!-- STEAM START -->` and `<!-- STEAM END -->` markers.

<!-- STEAM START -->
```
[h1]Alliance Rep Helper[/h1]

Lets faction leaders align their faction with a configured NPC faction using a simple chat command. Designed for servers using Modular Encounters Systems (MES) and Alliance-style territorial gameplay.

Instead of slow reputation grinding or bugging an admin, your faction's Founder or Leader can commit to an NPC alliance with a single command.

[hr][/hr]

[h2]How It Works[/h2]

When a faction Founder or Leader runs [b]/alliance <FactionTag>[/b]:

[olist]
[*] Your [b]faction's[/b] reputation with the [b]chosen NPC faction[/b] is set to the ally value (default: +1500).
[*] Your [b]faction's[/b] reputation with [b]all other configured NPC factions[/b] is set to the enemy value (default: -1500).
[*] The choice is recorded and persisted for the world.
[/olist]

[b]Requirements:[/b]
[list]
[*] You [b]must be in a faction[/b]. Factionless players cannot use the command.
[*] You [b]must be a Founder or Leader[/b] of your faction.
[*] The target must be in the server's configured list of allowed NPC factions.
[/list]

This sets [b]faction-to-faction[/b] reputation. All members of your faction inherit the relationship.

[h3]New Factions[/h3]

When a player creates a new faction, it is automatically set to the configured default reputation (default: -600, Enemies) with all configured NPC factions. New factions start hostile to everyone until they choose an alliance.

[hr][/hr]

[h2]Chat Commands[/h2]

All commands are typed in chat. They are case-insensitive and [b]not[/b] visible to other players.

[h3]Player Commands[/h3]

[table]
[tr]
[th]Command[/th]
[th]Description[/th]
[/tr]
[tr]
[td][b]/alliance <FactionTag>[/b][/td]
[td]Align your faction with the specified NPC faction. Requires Founder or Leader role. One-time only.[/td]
[/tr]
[tr]
[td][b]/alliance list[/b][/td]
[td]Show available NPC factions.[/td]
[/tr]
[tr]
[td][b]/alliance status[/b][/td]
[td]Show your faction's current reputation with each configured NPC faction.[/td]
[/tr]
[tr]
[td][b]/alliance help[/b][/td]
[td]Show in-game help.[/td]
[/tr]
[/table]

[h3]Admin Commands[/h3]

These commands require server admin privileges.

[table]
[tr]
[th]Command[/th]
[th]Description[/th]
[/tr]
[tr]
[td][b]/alliance reset <Tag>[/b][/td]
[td]Reset a specific player faction to default reputation and clear its alliance choice. The faction can then use /alliance again.[/td]
[/tr]
[tr]
[td][b]/alliance resetall[/b][/td]
[td]Reset ALL player factions to default reputation and clear all alliance choices. Every faction can then use /alliance again.[/td]
[/tr]
[/table]

[h3]Example[/h3]
[code]
/alliance SOBAN
[/code]

[quote]
Your faction [MYFC] has allied with [SOBAN] Soban Fleet!
Reputation set to 1500 with [SOBAN].
Reputation set to -1500 with all other alliance factions.
[/quote]

[hr][/hr]

[h2]Setup (Server Owners)[/h2]

[olist]
[*] Subscribe to the mod on the Workshop.
[*] Add it to your world/server and load it once. A default config file will be created.
[*] Edit the config file to list the NPC faction tags players can align with.
[*] Restart the world/server.
[/olist]

[hr][/hr]

[h2]Configuration[/h2]

[h3]Config File Location[/h3]

The config is stored in the world storage directory:

[b]Dedicated server:[/b]
[code]
<SE Install>/Instances/<InstanceName>/Saves/<WorldName>/Storage/<ModId>.sbm_AllianceRepHelper/AllianceRepHelper.cfg
[/code]

[b]Local / single-player:[/b]
[code]
%AppData%/SpaceEngineers/Saves/<SteamId>/<WorldName>/Storage/AllianceRepHelper_AllianceRepHelper/AllianceRepHelper.cfg
[/code]

[i]Tip: If you can't find it, load the world once with the mod enabled and it will be created automatically.[/i]

[h3]Config Format[/h3]

Simple [b]Key = Value[/b] format. Lines starting with # or // are comments.

[code]
# Comma-separated faction tags players can align with.
# MUST be set for the mod to work.
Factions = SOBAN, KHAANEPH

# Rep granted to chosen ally faction (default: 1500)
AllyReputation = 1500

# Rep set for all OTHER configured factions (default: -1500)
EnemyReputation = -1500

# Default rep for newly created factions (default: -600)
# Values at or below -600 reliably show as Enemies (red).
# The enemy/neutral threshold is around -500.
DefaultReputation = -600

# Only allow NPC factions (default: true)
AllowOnlyNpcFactions = true
[/code]

[h3]Config Options[/h3]

[table]
[tr]
[th]Key[/th]
[th]Type[/th]
[th]Default[/th]
[th]Description[/th]
[/tr]
[tr]
[td]Factions[/td]
[td]Comma-separated strings[/td]
[td](empty)[/td]
[td][b]Required.[/b] NPC faction tags players can choose. Must match in-game tags exactly.[/td]
[/tr]
[tr]
[td]AllyReputation[/td]
[td]Integer[/td]
[td]1500[/td]
[td]Reputation set with the chosen NPC faction.[/td]
[/tr]
[tr]
[td]EnemyReputation[/td]
[td]Integer[/td]
[td]-1500[/td]
[td]Reputation set with all other configured NPC factions.[/td]
[/tr]
[tr]
[td]DefaultReputation[/td]
[td]Integer[/td]
[td]-600[/td]
[td]Reputation for newly created factions. The enemy/neutral threshold is ~-500; use -600 or lower for reliable Enemies (red) status.[/td]
[/tr]
[tr]
[td]AllowOnlyNpcFactions[/td]
[td]Boolean[/td]
[td]true[/td]
[td]When true, only NPC-only factions can be selected. Prevents abuse on PvP servers.[/td]
[/tr]
[/table]

[hr][/hr]

[h2]Faction Choice Tracking[/h2]

When the command is used, the faction's ID is saved to [b]AllianceRepHelper_FactionChoices.dat[/b] in the same storage folder.

[b]Alliance choice is one-time only.[/b] Once a faction has chosen, the command cannot be used again for that faction. Use [b]/alliance status[/b] to check whether your faction has already chosen.

[b]Admin tip:[/b] Use [b]/alliance reset <Tag>[/b] to reset a specific faction's choice in-game, or [b]/alliance resetall[/b] to reset everyone. No file editing or server restart required.

[hr][/hr]

[h2]Source Code[/h2]

[url=https://github.com/tinsoldier/AllianceRepHelper]GitHub Repository[/url]
```
<!-- STEAM END -->
