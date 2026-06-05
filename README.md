# Puck Admin Panel

An admin panel plugin for **[Puck](https://store.steampowered.com/app/1776180/Puck/)** that gives server admins full in-game control via a draggable, minimisable UIToolkit UI.

Press **X** to toggle the panel.

---

## Features

- **Two-tab layout** — Players tab (player list, search, kick/ban) and Commands tab (quick-action bar + text command input)
- **Draggable** — grab the header bar to reposition
- **Minimisable** — click the `_` button to collapse to just the header
- **Player list** with name, team, role, mute status, and Steam ID — click **Sel** to select a player for targeted commands
- **Quick-action buttons** for every command, organised by category
- **Text command input** — type any command with arguments (e.g. `/mute Bob 10m`) and press Enter or click Run

---

## Commands

All commands are slash-prefixed. Player names are matched by partial name (case-insensitive), exact Steam ID, or partial Steam ID. Most commands that target a player will use the currently selected player if no name is given.

### Game Flow

| Command | Description | Example |
|---------|-------------|---------|
| `/warmup [seconds]` | Start warmup (default 60s) | `/warmup 120` |
| `/start` | Start the game (Play phase, 300s clock, 0-0) | `/start` |
| `/pause` | Pause the game clock only | `/pause` |
| `/resume` | Resume the game clock only | `/resume` |
| `/pauseall` | Pause clock **and** freeze all players | `/pauseall` |
| `/resumeall` | Resume clock **and** unfreeze all players | `/resumeall` |

### Player Actions

| Command | Description | Example |
|---------|-------------|---------|
| `/freeze [player\|puck]` | Freeze a player's body (or `puck`) | `/freeze puck` |
| `/unfreeze [player\|puck]` | Unfreeze a player (or `puck`) | `/unfreeze puck` |
| `/freezeall` | Freeze all players + puck | `/freezeall` |
| `/unfreezeall` | Unfreeze all players + puck | `/unfreezeall` |
| `/slap [player]` | Slap a player with random force | `/slap Bob` |
| `/jump [player]` | Make a player jump | `/jump` |

### Mute System

| Command | Description | Example |
|---------|-------------|---------|
| `/mute <player> [duration]` | Mute a player from chat | `/mute Bob 10m` |
| `/unmute <player>` | Unmute a player | `/unmute Bob` |
| `/muted` | List all currently muted players | `/muted` |

Mutes are set directly on the player's `IsMuted` NetworkVariable. Duration is informational only (displayed in chat) — unmute manually with `/unmute`.

### Team Management

| Command | Description | Example |
|---------|-------------|---------|
| `/changeteam <player> <team>` | Move player to `blue`, `red`, or `spectator` | `/changeteam Bob red` |
| `/swap <player1> <player2>` | Swap two players' teams | `/swap Bob Alice` |

### Kick & Ban

| Command | Description | Example |
|---------|-------------|---------|
| `/kick [player]` | Kick selected/named player from server | `/kick Bob` |
| `/kicksteamid <steamId>` | Kick by exact Steam ID | `/kicksteamid 76561198000000000` |

Bans are available via the **Players** tab — select a player and click **Ban**, or enter a Steam ID in the bottom bar and click **Ban**.

### Info

| Command | Description | Example |
|---------|-------------|---------|
| `/whoami [player]` | Show player info (name, Steam ID, team, role, admin level, mute status) | `/whoami Bob` |

### Game State

| Command | Description | Example |
|---------|-------------|---------|
| `/settime <seconds>` | Set game clock | `/settime 180` |
| `/setgoals <team> <amount>` | Set team score (absolute or +/-) | `/setgoals blue +1` |
| `/setstate <period>` | Set game period (1–3, 4+ = OT) | `/setstate 3` |

---

## UI Walkthrough

### Players Tab
- **Search bar** — filter by player name or Steam ID
- **Kick / Ban** buttons — act on the currently selected player
- **Refresh** — re-scan connected players
- **Sel** button on each row — select that player for targeted commands
- **Bottom bar** — enter a Steam ID manually and Kick/Ban by ID

### Commands Tab
- **Command input** — type `/command args` and press Enter
- **Run** button — execute the typed command
- **Quick-action buttons** — organised into sections:
  - **Game FLOW** — Warmup, Start, Pause, Resume, Pause All, Resume All, Freeze All, Unfreeze All
  - **PLAYER ACTIONS** — Freeze, Unfreeze, Slap, Jump, Mute, Unmute, Kick, Ban
  - **TEAM MANAGEMENT** — To Blue, To Red, To Spec, Swap Teams
  - **GAME STATE** — P1, P2, P3, OT, Blue ±1, Red ±1
  - **INFO** — Who Am I, Muted List, Freeze Puck, Unfreeze Puck

---
