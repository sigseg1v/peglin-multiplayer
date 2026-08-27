# Changelog

## Multipeglin v0.1.14

- improved: clients no longer lock up for several frames every 2-5 seconds during battle
- improved: the map token on clients now walks between nodes instead of teleporting to them (thanks Garrett-Webb for reporting and helping)
- fixed: red and black (rigged) bombs could be swapped between host and client on the mines levels; this also affected bomb-related relics and run stats, not just how they looked
- fixed: long pegs could vanish on the client, leaving whole arcs of the spiral boards missing for the rest of the battle (thanks Garrett-Webb for reporting and fixing)
- fixed: crit/bonus pegs stayed plain on the client through entire crit windows while the host showed them red
- fixed: the next-node overlay could stop responding to clicks after the first navigation, softlocking the run
- fixed: a lost vote during navigation could hang the run indefinitely; it now resolves on its own after 60 seconds
- fixed: bombs on the client could show a lit fuse the host never lit, and could disappear instead of playing their explosion
- fixed: the client could permanently destroy pegs that were never out of sync in the first place (slimes and obstacle pegs), causing the desync it was meant to prevent
- fixed: the client aim line did not bend off pegs correctly after the board changed (thanks Garrett-Webb for reporting and fixing)
- fixed: long peg healing and the crit tint were not restored after a board refresh
- fixed: clients could crash while the map token was walking

## Multipeglin v0.1.13

- fixed: softlock in the "choose between 3 things" question mark event (thanks to Garrett-Webb for reporting and for a fix PR)

## Multipeglin v0.1.12

- fixed: "Multiplayer desync + softlock when all bombs detonated in chest navigation" (thanks haencyl for reporting)
- fixed: softlock when one player is unable to throw their ball in one of the "?" orb minigames (thanks CheeseThanos for reporting)
- fixed: issue where when doing Continue on a multiplayer run without exiting the game, it was prompting players to select a new relic and leaving a dark overlay over the screen

## Multipeglin v0.1.11

- improved: performance of host when there are lots of orbs/pegs on the board greatly improved by a caching and optimization pass (reported by: Marikovka)
- fixed: clients couldn't discard after the host had already used their discard that turn
- fixed: continue/load session was being cleared at the wrong time on host start and disconnect; fixed so that clicking New Game clears cached state (reported by: Marikovka)

## Multipeglin v0.1.10

- general: Peglin game version updated to match 2.0.12
- improved: clientside ball movement now has variable latency compensation and should be smoother on high latency connections
- improved: added variable compression to network messages over a certain size threshold, should help with low bandwidth connections
- fixed: Doctorb heal was not applying in multiplayer (note, for level 1 it does 0 base damage unless you crit)
- fixed: aiming dotted line was sometimes not showing up on host
- fixed: Pumpkin π relic was not working; now applies as a general board state for all players
- fixed: Summoning Circle was not showing an aimer and was not applying shot velocity
- fixed: discarding an orb was sometimes not updating the aim preview graphic for the next orb drawn
- fixed: transpherency wasn't applying to splash projectile damage
- fixed: the aimer on the host was sometimes not showing up
- fixed: the aimer on clients was sometimes drawing the dotted line through solid pegs that should collide with it

Thanks to Marikovka for reporting several of the above issues.
