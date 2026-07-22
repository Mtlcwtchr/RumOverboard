# Cooperative Sailing Game — GDD / TDD

**Document status:** Concept and pre-production specification  
**Engine:** Unity 
**Render pipeline:** URP  
**Networking:** Photon Fusion 2, Shared Mode preferred for prototype  
**Target platform:** Windows / Steam  
**Target session length:** 3–5 hours per campaign  
**Players:** 1–8, ideal group size 2–4

---

## 1. Executive Summary

This project is a systemic cooperative sailing game about transporting a fragile ship through a procedurally assembled sequence of dangerous maritime regions.

The game is inspired by the emergent cooperation and cascading failures of **PEAK**, **Lethal Company**, and **RV There Yet**, but is not intended to be a sailing simulator or a Sea of Thieves-like combat sandbox.

The core fantasy is not “being a pirate.” It is:

> A group of players trying to keep one physically simulated ship moving while weather, waves, mistakes, loose objects, damaged sails, flooding, fire, fish, exhaustion, and poor coordination compound into memorable stories.

The campaign has no missions, classes, RPG progression, upgrade tree, economy, inventory, or abstract resources. The only mandatory objective is to reach the final harbor.

Everything else is physical, optional, and vulnerable to loss.

---

## 2. Product Vision

### 2.1 Player promise

The game should consistently create stories such as:

- a player is swept overboard while repairing a torn sail;
- another player dives after them while the ship continues moving;
- a loose rope unties another sail under heavy wind;
- the ship heels over, cargo slides across the deck, and the compass disappears into the sea;
- a fish lands on deck and knocks someone into a burning powder barrel;
- the team reaches the checkpoint with no map, one working lantern, and no repair boards.

The systems must make these events feel caused rather than scripted.

### 2.2 Experience goals

The game should feel:

- physically expressive;
- cooperative without requiring formal role selection;
- chaotic but understandable;
- punishing without relying on permanent player death;
- funny because of consequences, not jokes;
- replayable through systemic variation rather than progression grind.

### 2.3 Non-goals

The game is not intended to be:

- a realistic sail-training simulator;
- a naval combat game;
- a quest-driven adventure;
- an RPG;
- a survival crafting game;
- a live-service progression platform;
- a competitive PvP experience;
- a game with protected critical items or anti-sabotage restrictions.

---

## 3. Design Pillars

### 3.1 Physical First

Whenever possible, game state should exist as physical objects and physical relationships.

Examples:

- boards are boards, not a number;
- cloth is a physical object;
- ropes have endpoints and attachment state;
- the map must be held and unfolded;
- loose cargo can slide, burn, fall, block passages, or be lost;
- a player in the sea is moved by waves, not by a symbolic current debuff.

### 3.2 Systemic Second

Systems should interact through shared physical state.

Examples:

- heel changes object movement;
- loose objects can hit players;
- player impacts produce temporary ragdoll;
- torn sails alter force generation;
- flooding changes ship mass and buoyancy;
- fire can consume objects and spread to powder;
- wind can overload poorly secured ropes.

### 3.3 Director Last

The director does not replace simulation.

The world should be conditionally honest. A director may:

- bias probabilities;
- select compatible events;
- intensify or soften upcoming conditions;
- choose when a physically plausible opportunity appears;
- occasionally help or punish.

It should not visibly violate causality.

### 3.4 No Abstract Inventory

The ship has no inventory.

There are no counters such as:

- Wood: 8
- Cloth: 3
- Rope: 5

There are only actual objects, which may be carried, moved, lost, burned, thrown, found, or used.

### 3.5 No Protected Systems

Players may intentionally or accidentally:

- destroy the ship;
- lose the map or compass;
- waste all repair material;
- fire the cannon into their own ship;
- explode powder;
- abandon a player;
- misroute ropes;
- sabotage mechanisms.

The game should not silently restore critical equipment merely because it is important.

### 3.6 The Ship Is the Main Character

The player avatar is only one part of a larger machine.

The central object of attention is the current state of the ship:

- sails;
- ropes;
- flooding;
- breaches;
- fire;
- lighting;
- anchor;
- steering;
- cargo;
- route;
- crew location.

---

## 4. Core Gameplay Loop

```text
Depart checkpoint
↓
Navigate toward next harbor
↓
Operate sails, wheel, anchor, ropes, oars, lights
↓
Observe weather, hazards, landmarks, wind and currents
↓
A problem emerges
↓
Players physically respond
↓
Response creates or exposes secondary problems
↓
Crew stabilizes the ship or loses control
↓
Reach checkpoint, save, recover, scavenge
↓
Continue into a harder region
```

The game does not depend on authored quest chains.

The main content is a sequence of interacting physical problems.

---

## 5. Campaign Structure

### 5.1 Run format

A campaign lasts approximately 3–5 hours.

A run consists of:

- a starting harbor;
- a procedurally assembled chain of authored maritime regions;
- multiple possible routes through each region;
- optional islands, shortcuts, reefs, secrets, and hazards;
- intermediate checkpoint harbors;
- a final harbor.

### 5.2 Difficulty curve

The opening region is intentionally safe and readable.

Later regions increase difficulty through:

- stronger winds;
- larger waves;
- more dangerous currents;
- reduced visibility;
- narrower passages;
- reefs and shallows;
- cold and icing;
- storms;
- more aggressive large fish;
- higher event density;
- more difficult route choices.

The difficulty increase should feel geographical and environmental rather than numerical.

### 5.3 Completion

At the final harbor:

- the campaign ends;
- final statistics are shown;
- credits may be shown;
- cosmetic rewards are unlocked;
- the player can immediately begin another run.

There is no metagame currency.

---

## 6. World Structure

### 6.1 Procedural assembly

The world is generated from authored region definitions rather than fully procedural terrain generation.

Each region can define:

- geography scene or tile set;
- wave preset;
- wind preset;
- current preset;
- weather weights;
- event weights;
- lighting;
- ambience;
- music;
- island pools;
- reef pools;
- wildlife pools;
- secrets;
- shortcut conditions;
- harbor exits.

### 6.2 Static and dynamic elements

Static:

- geography;
- islands;
- reefs;
- cliffs;
- narrow passages;
- shallows;
- harbor locations;
- major landmarks.

Dynamic:

- wind;
- weather;
- waves;
- currents;
- storms;
- fog;
- wildlife;
- floating objects;
- selected environmental events.

### 6.3 Navigation

The player has:

- a physical paper map;
- a physical compass;
- a physical spyglass;
- visible destination markers on the map;
- visible checkpoint markings.

The player does not have:

- a world-space GPS marker;
- a minimap with live position;
- a player icon on the paper map.

Navigation relies on:

- compass headings;
- landmarks;
- coastline shapes;
- lighthouses;
- weather gaps;
- observation with the spyglass;
- crew communication.

---

## 7. Ship Overview

### 7.1 Ship scope

There is one primary playable ship.

The ship does not receive permanent upgrades.

Its gameplay depth comes from damage, repair, physical operation, and changing environmental conditions.

### 7.2 Main systems

- hull;
- sails;
- masts;
- ropes;
- wheel and rudder;
- anchor;
- oars;
- flooding;
- breaches;
- cargo;
- lighting;
- cannon;
- grog supply.

### 7.3 Physics composition

Preferred architecture:

- one main Rigidbody for the hull;
- separate Rigidbody assemblies for major moving parts;
- joints for rudder, anchor, spars, and selected mechanisms;
- predefined detachable or destructible elements;
- no ship-wide construction from hundreds of independent boards.

This balances physical expressiveness, performance, and network stability.

---

## 8. Sailing Model

### 8.1 Sail layout

The ship has three masts:

- forward mast: one sail;
- central mast: two sails;
- rear mast: one triangular sail.

Total: four independently controlled sails.

### 8.2 Sail inputs

Each sail has independent:

- hoist state;
- extension state;
- orientation;
- control ropes;
- attachment points;
- damage state;
- effective area.

### 8.3 Aerodynamic approximation

The game should account for:

- true wind;
- vessel velocity;
- apparent wind;
- sail orientation;
- sail deployment;
- effective sail area;
- sail damage;
- lift;
- drag;
- point of force application;
- heeling torque.

The game does not need to simulate detailed pressure distribution on cloth or stress transfer through the entire mast structure.

Each sail can resolve into a force and torque applied to the ship.

### 8.4 Capsize behavior

Sails may create enough heeling torque to knock the ship heavily onto its side.

The intended result is usually a knockdown rather than permanent inversion:

- players are thrown;
- loose cargo is lost;
- water may enter;
- ropes and sails lose control;
- the ship gradually rights itself through hull shape and buoyancy.

A full loss still occurs through sinking, not merely through temporary extreme heel.

### 8.5 Cargo mass

Ordinary cargo and player mass do not meaningfully alter the ship’s center of mass.

Primary stability inputs are:

- hull parameters;
- flooding;
- major ship components;
- sail forces;
- waves;
- buoyancy configuration.

This is an intentional stability and performance concession.

---

## 9. Rope System

### 9.1 Core behavior

Ropes are physical gameplay objects.

A player may:

- grab an end;
- pull it;
- move it between attachment points;
- release it;
- secure it;
- attach it incorrectly;
- route it around geometry;
- use it to secure objects;
- hang from it;
- swing from it.

### 9.2 Free rope behavior

If a player releases a loaded rope:

- it is pulled from the hand;
- its free end moves physically;
- it may whip across the deck;
- it may strike or knock down a player;
- it may catch on ship geometry;
- the related sail loses support from that line.

### 9.3 Knot model

Initial implementation:

- contextual secure action;
- hold interaction;
- rope snaps into a valid secured state.

Future implementation:

- knot-tying minigame;
- knot quality may affect resistance to wind load;
- quick knots may be less reliable than properly completed knots.

### 9.4 Incorrect routing

Players may:

- attach to the wrong cleat;
- cross lines;
- block mechanisms;
- route around unsuitable geometry;
- leave dangerous loose lines across walkways.

### 9.5 Rope network synchronization

Replicate:

- endpoints;
- control points;
- attachment state;
- knot state;
- length;
- relevant tension state;
- held-by-player state;
- attached-object state.

Compute locally:

- spline shape;
- Bezier curvature;
- visual segment placement;
- small secondary oscillations;
- decorative sag;
- minor wind flutter.

A limited local collision or proxy system may represent dangerous free rope ends.

---

## 10. Sail Damage and Repair

### 10.1 Damage

Sails support localized damage:

- holes;
- tears;
- expanding tears;
- reduced effective area;
- unstable force output;
- complete failure at critical damage.

Nearby damage events should merge into a larger damage region rather than create excessive independent damage entities.

### 10.2 Repair

Repair process:

1. obtain a physical cloth object;
2. climb to the damaged area;
3. hold the repair action;
4. consume the cloth object;
5. reduce or remove the tear.

Repairing sails during strong wind should be dangerous because the player is exposed to movement, height, and unstable cloth.

---

## 11. Hull Damage and Flooding

### 11.1 Breach model

Hull damage creates localized breaches.

Each breach includes:

- position;
- surface normal;
- size;
- severity;
- water ingress rate;
- repair progress.

Nearby impacts enlarge the existing breach rather than creating many overlapping holes.

### 11.2 Repair

Repair process:

1. bring a physical board;
2. interact near the breach;
3. hold the repair action;
4. consume the board;
5. shrink or close the breach.

The board does not need to remain as a fully simulated permanent patch.

### 11.3 Flooding target model

The desired final system includes water inside the ship.

Minimum viable implementation:

- numeric water volume;
- visual water surface;
- water-level representation;
- additional ship mass;
- reduced buoyancy or altered equilibrium;
- sinking when thresholds are exceeded.

Advanced implementation:

- multiple compartments;
- local water levels;
- flow between compartments;
- floating objects;
- player swimming in flooded interiors;
- localized breach-to-compartment relationships.

---

## 12. Anchor

The anchor is a physical object.

It can:

- be released;
- fall;
- drag;
- contact the seabed;
- catch on terrain or obstacles;
- resist ship movement;
- become stuck.

When properly secured to the ship, it should not randomly detach.

Detachment requires explicit player action or a defined destruction case.

---

## 13. Player Character

### 13.1 Perspective

- first-person during normal play;
- third-person while knocked out or ragdolled.

### 13.2 Movement

The player physically stands on the moving ship.

No fake parenting should be used as the primary solution.

The player is represented as a networked physical character using:

- NetworkBehaviour;
- networked Rigidbody or compatible Fusion physics approach;
- moving-platform compensation where necessary;
- procedural animation.

### 13.3 Climbing

Climbing is available only inside authored climbable areas:

- ropes;
- ladders;
- masts;
- nets;
- selected geometry.

The player may:

- climb;
- hang;
- swing;
- transition between climbable structures.

### 13.4 Stamina

Stamina is an action resource.

It is consumed by:

- running;
- swimming;
- climbing;
- jumping;
- rowing;
- moving while holding a loaded rope.

Stamina regenerates automatically.

There is no hunger or food requirement.

---

## 14. Knockdown, Drowning, and Rescue

### 14.1 No health system

The player has no HP.

There are no injuries, bleeding, limb damage, or long-term debuffs.

### 14.2 Temporary knockdown

Strong impacts cause temporary loss of control.

Sources include:

- falls;
- collisions;
- explosions;
- large fish impacts;
- moving objects;
- violent ship motion.

Flow:

1. impact occurs;
2. control is disabled for approximately 1–2 seconds;
3. the player enters ragdoll;
4. physics settles;
5. a get-up animation plays;
6. control returns.

Temporary knockdown always resolves automatically.

### 14.3 Drowning

While swimming, stamina is consumed.

If the player grabs a stable or floating object, stamina may regenerate.

When stamina reaches zero:

- a separate drowning meter begins;
- once full, the player becomes drowned/unconscious;
- this state does not recover automatically;
- another player must perform rescue and resuscitation.

### 14.4 Water movement

Waves and currents physically move the player.

They may carry the player progressively farther from the ship.

### 14.5 Self-rescue

A conscious player may return to the ship if:

- stamina remains;
- they reach a ladder, rope, net, or climbable hull area;
- environmental forces do not prevent the climb.

### 14.6 Body recovery

A teammate can:

- swim to a drowned player;
- grab the body with one hand;
- drag or pull it onto the ship;
- place it on deck;
- hold an interaction to resuscitate.

Future implementation may replace the hold interaction with a rhythm minigame.

### 14.7 Extreme distance fallback

A drowned player may remain in the world indefinitely.

If the ship moves farther than a large configured recovery distance:

- the body is relocated to the ship’s hold;
- the player remains drowned;
- resuscitation is still required.

This is a technical anti-loss mechanism, not a free revival.

### 14.8 Failure conditions

The run ends when:

- the ship sinks; or
- all players are drowned simultaneously.

If all players are only temporarily knocked down, play continues.

Players cannot permanently kill or finish one another through ordinary damage.

---

## 15. Grog and Hallucinations

### 15.1 Purpose

Grog is not hydration.

It maintains mental stability or sanity during the voyage.

### 15.2 Threshold model

A required sanity threshold slowly rises over the course of a campaign.

If the player remains below the threshold, hallucinations begin.

### 15.3 Hallucination scope

Hallucinations are primarily local to the affected player.

Possible hallucinations:

- distant impossible silhouettes;
- false lights;
- phantom sounds;
- false rope motion;
- apparent creatures;
- misleading deck objects;
- momentary fake damage indicators;
- voices or calls from nonexistent teammates.

Hallucinations should create uncertainty without becoming unavoidable instant failure.

---

## 16. Objects and Resource Economy

### 16.1 Required object types

Primary useful objects:

- boards;
- cloth;
- grog;
- map;
- spyglass;
- compass;
- ropes;
- lighting objects.

### 16.2 Optional objects

Other objects may exist for:

- achievements;
- comedy;
- optional challenges;
- route secrets;
- environmental storytelling;
- self-imposed transport goals.

There is no mandatory mission cargo.

### 16.3 Object discovery

Objects may be found:

- floating in the sea;
- on islands;
- at checkpoints;
- inside ship spaces;
- after environmental events;
- after fish or debris interactions.

### 16.4 Storage

There are no abstract storage slots.

Objects are placed physically.

Players may improvise storage through:

- careful placement;
- rooms;
- corners;
- rope attachment;
- stacking;
- containment by geometry.

### 16.5 Permanent loss

Critical objects may be permanently lost:

- map;
- compass;
- spyglass;
- all boards;
- all cloth;
- all lights.

The game does not automatically restore them outside checkpoint content or authored recovery opportunities.

---

## 17. Fishing and Wildlife

### 17.1 Fishing

Fishing exists as a physical side activity.

Fish are not required as food.

Caught fish may:

- be consumed for effects;
- be used for achievements;
- be stored;
- be thrown;
- become physical hazards.

### 17.2 Fish entering the ship

Fish may jump onto the deck naturally.

A loose fish may:

- flop;
- knock players;
- slide with the ship;
- block movement;
- fall back overboard.

### 17.3 Large fish

Large fish may:

- ram the hull;
- create breaches;
- jump aboard;
- strike players;
- push players overboard.

They are wildlife hazards, not conventional enemies.

### 17.4 Rats

Rats may:

- move small objects;
- untie knots;
- interfere with ropes;
- sabotage supplies;
- create distractions.

No supernatural monsters are required.

Supernatural content is limited primarily to hallucinations.

---

## 18. Fire, Lightning, and Explosions

### 18.1 Fire sources

- dropped lights;
- lightning;
- powder;
- player mistakes;
- burning cargo;
- environmental effects.

### 18.2 Fire behavior

Fire should:

- spread between valid materials;
- consume objects;
- damage sails and ropes;
- ignite powder;
- create local visibility and navigation problems;
- force players to physically respond.

### 18.3 Powder

Powder is an optional physical hazard and cannon resource.

It may:

- explode;
- damage the hull;
- throw players;
- ignite nearby objects;
- destroy optional cargo.

---

## 19. Cannon

The cannon has no required enemy-combat loop.

It can be used to:

- attack large fish;
- destroy obstacles;
- signal;
- trigger environmental interactions;
- create breaches;
- damage sails;
- throw players or cargo;
- deliberately sabotage the ship.

The cannon is a systemic tool and risk source.

---

## 20. Environmental Hazards

Primary hazards:

- storms;
- fog;
- night;
- currents;
- large waves;
- whirlpools;
- reefs;
- shallows;
- cold;
- icing;
- lightning;
- fish;
- fire;
- loose cargo;
- player mistakes.

### 20.1 Cold

Cold regions may cause:

- icing on ship surfaces;
- reduced traction;
- slower mechanisms;
- increased stamina pressure;
- visibility problems.

### 20.2 Fog and night

Fog and night increase dependence on:

- compass;
- map;
- spyglass;
- lighting;
- landmarks;
- team communication.

---

## 21. Checkpoints

Checkpoint harbors provide:

- campaign save;
- player restoration;
- safe state transition;
- optional scavenging;
- repair materials that physically exist in the scene;
- miscellaneous optional objects;
- access to the next region.

Checkpoints do not automatically fully repair or restock the ship unless specifically authored.

Players must move found objects aboard manually.

---

## 22. Achievements and Cosmetic Progression

### 22.1 Progression philosophy

There is no gameplay progression.

No permanent unlock may improve:

- speed;
- stamina;
- ship durability;
- repair speed;
- navigation;
- combat;
- resource quantity.

### 22.2 Cosmetic scope

Cosmetics apply to player characters.

Possible categories:

- clothing;
- hats;
- facial accessories;
- body customization;
- emotes;
- voice or expression options;
- small non-gameplay visual effects.

### 22.3 Achievement role

Achievements are a major replayability driver.

An achievement may also unlock a cosmetic.

Achievement themes:

- complete a run under unusual conditions;
- preserve an optional object;
- lose specific critical tools and still finish;
- rescue multiple drowned players;
- transport absurd cargo;
- finish without using a compass;
- survive extreme weather;
- fire the cannon in unusual ways;
- catch rare fish;
- reach hidden islands;
- trigger rare systemic outcomes.

There is no metagame currency.

---

## 23. Multiplayer Architecture

### 23.1 Networking model

Photon Fusion 2 is the selected networking framework.

Shared Mode is the likely prototype model.

The architecture should be designed so that critical gameplay state remains authoritative and deterministic enough for network correction.

### 23.2 Replication tiers

#### Tier A — critical authoritative state

Replicate tightly:

- ship transform and velocity;
- player transforms and physical states;
- active ragdoll state;
- grabbed objects;
- critical object transforms;
- rope endpoints and attachment state;
- sail configuration;
- breach state;
- water volume;
- fire state;
- anchor state;
- checkpoint and campaign state;
- drowned/revived state.

#### Tier B — gameplay-relevant secondary state

Replicate at reduced frequency or event-based:

- object sleeping/wake state;
- wildlife state;
- loose cargo state;
- local damage visuals;
- fish flop state;
- dynamic obstacle state.

#### Tier C — client-derived cosmetic state

Compute locally:

- rope curves;
- sail flutter;
- minor cloth simulation;
- secondary water visuals;
- foam;
- spray;
- particles;
- camera shake;
- minor debris;
- hallucinations;
- decorative physics.

### 23.3 Interest management

Large-world networking should use relevance zones or proximity filtering.

Priority should be given to:

- ship vicinity;
- current island;
- nearby floating objects;
- nearby wildlife;
- rescue targets;
- visible regional hazards.

### 23.4 Player-local hallucinations

Hallucinations should normally not be replicated as shared physical truth.

They may derive from:

- local sanity state;
- deterministic player seed;
- region state;
- director hints;
- local presentation systems.

---

## 24. Save System

### 24.1 Save points

Saving occurs at checkpoints.

### 24.2 Save contents

Save data should include:

- run seed;
- current region index;
- selected route history;
- current checkpoint;
- ship damage;
- breach state;
- flooding state;
- sail damage;
- rope attachment state;
- surviving physical objects;
- important object transforms or storage locations;
- discovered secrets;
- achievement progress relevant to the run;
- player cosmetic selections.

### 24.3 Failure recovery

On campaign failure:

- reload the latest checkpoint;
- restore the checkpoint snapshot;
- do not preserve post-checkpoint gains or losses.

---

## 25. Director Architecture

### 25.1 Purpose

The director manages pacing, not outcomes.

### 25.2 Inputs

Possible inputs:

- region difficulty;
- time since last major event;
- current ship damage;
- flooding level;
- available repair objects;
- crew separation;
- player knockdown frequency;
- current wind and wave state;
- remaining route distance;
- recent failures;
- object loss;
- current chaos score.

### 25.3 Outputs

Possible outputs:

- weather transition weighting;
- fish encounter chance;
- debris spawn chance;
- fog timing;
- storm intensity trend;
- floating supply opportunity;
- rare event eligibility;
- rat activity;
- lightning probability;
- optional route opportunity.

### 25.4 Constraints

The director should not:

- teleport obvious hazards directly onto players;
- fabricate damage without a cause;
- restore lost critical items silently;
- override all simulation outcomes;
- guarantee rescue;
- guarantee failure.

---

## 26. Interaction Framework

### 26.1 General principles

Interactions should be:

- contextual;
- physical;
- readable;
- consistent;
- usable under stress;
- minimally menu-driven.

### 26.2 Common interactions

- grab;
- release;
- carry;
- throw;
- attach;
- detach;
- pull;
- push;
- hold to repair;
- hold to secure;
- climb;
- drag body;
- revive;
- drink;
- aim/use spyglass;
- unfold/read map;
- inspect compass.

### 26.3 Object carrying

The player carries one object at a time.

There is no personal inventory.

Throwing objects to teammates is supported.

---

## 27. Presentation and UI

### 27.1 HUD philosophy

The HUD should be minimal.

Prefer diegetic or contextual information.

Likely HUD elements:

- stamina;
- drowning meter;
- interaction prompt;
- temporary repair progress;
- temporary revive progress;
- current grab or rope state;
- limited contextual warnings.

### 27.2 Information intentionally absent

- health bar;
- minimap;
- quest list;
- mission tracker;
- inventory grid;
- abstract resource counters;
- live player position on map.

### 27.3 Statistics screen

End-of-run statistics may include:

- run duration;
- regions completed;
- players rescued;
- drownings;
- knockdowns;
- objects lost;
- sails repaired;
- hull breaches repaired;
- fish caught;
- cannon shots;
- fires started/extinguished;
- optional cargo delivered;
- rare events encountered;
- distance sailed;
- time spent overboard.

---

## 28. Audio Direction

Audio is essential for systemic readability.

Important cues:

- rope tension;
- knot strain;
- sail flutter;
- sail tearing;
- hull creaking;
- breach impact;
- incoming water;
- shifting cargo;
- anchor drag;
- distant waves;
- approaching storm;
- lightning;
- fish impact;
- drowning state;
- teammate calls;
- hallucination layers.

Many events should be heard before they are seen.

---

## 29. Technical Architecture

### 29.1 Core modules

Suggested top-level runtime modules:

- Campaign;
- WorldGeneration;
- RegionRuntime;
- Director;
- Ship;
- Sailing;
- Buoyancy;
- Rope;
- Sail;
- Damage;
- Flooding;
- Fire;
- Interaction;
- Player;
- Ragdoll;
- Climbing;
- Objects;
- Wildlife;
- Weather;
- SaveLoad;
- Networking;
- Achievements;
- Cosmetics;
- Audio;
- Presentation.

### 29.2 Data architecture

Use ScriptableObject-based authored data for:

- region definitions;
- weather presets;
- wind presets;
- current fields;
- wave presets;
- event definitions;
- object definitions;
- damage tuning;
- sail tuning;
- rope tuning;
- achievement definitions;
- cosmetic definitions.

Runtime state should remain separate from authored data.

### 29.3 Existing packages

Current planned or present packages:

- Photon Fusion 2;
- Fusion Physics;
- UniTask;
- UniRx;
- Moq.

No current requirement for:

- ECS;
- dependency injection framework;
- Addressables.

These can be reconsidered only when a clear production need appears.

---

## 30. Physics Strategy

### 30.1 Simulation hierarchy

High-authority physics:

- ship hull;
- players;
- major ship mechanisms;
- held objects;
- dangerous loose objects;
- anchor;
- key wildlife impacts.

Simplified or local physics:

- rope rendering;
- sail flutter;
- small debris;
- cosmetic water;
- particles;
- minor clutter.

### 30.2 Stability priorities

When forced to choose, prioritize:

1. network stability;
2. readable physical consequences;
3. gameplay consistency;
4. visual physical detail;
5. full realism.

### 30.3 Object sleeping

Aggressive but safe sleeping should be used for loose objects.

Objects should wake when:

- ship acceleration exceeds a threshold;
- nearby collision occurs;
- a player interacts;
- water reaches them;
- fire affects them;
- the ship heels significantly.

---

## 31. Suggested State Machines

### 31.1 Player state

```text
Grounded
Swimming
Climbing
Hanging
RagdollTemporary
GettingUp
Drowning
Drowned
BeingDragged
Reviving
```

### 31.2 Rope endpoint state

```text
Secured
Held
Loose
CaughtOnGeometry
AttachedToObject
InTransition
```

### 31.3 Sail state

```text
Furled
Deploying
Deployed
Uncontrolled
Damaged
CriticallyTorn
Detached
```

### 31.4 Breach state

```text
Active
Expanding
BeingRepaired
Closed
```

### 31.5 Run state

```text
Lobby
Loading
AtCheckpoint
Voyaging
Failed
Completed
Results
```

---

## 32. Prototype Milestones

### Milestone 1 — Moving Ship Foundation

Goal: prove that multiple players can reliably stand and move on a networked ship.

Deliverables:

- network lobby;
- player spawning;
- movement and jumping;
- moving ship Rigidbody;
- stable player-on-ship behavior;
- basic ocean plane;
- basic buoyancy;
- reconnect and host edge-case tests.

### Milestone 2 — Basic Sailing

Goal: prove that physical sail operation is understandable and fun.

Deliverables:

- four sails;
- basic wind;
- sail forces;
- wheel and rudder;
- one rope per core sail function;
- contextual secure/release;
- local rope rendering;
- basic ship heel.

### Milestone 3 — Physical Chaos

Goal: prove cascading failures.

Deliverables:

- loose objects;
- temporary ragdoll;
- throwing;
- objects sliding on deck;
- players falling overboard;
- stamina;
- swimming;
- self-rescue.

### Milestone 4 — Damage Loop

Goal: prove that damage creates meaningful cooperative work.

Deliverables:

- localized breaches;
- physical boards;
- repair interaction;
- flooding volume;
- ship sinking;
- sail tears;
- cloth repair;
- checkpoint reload.

### Milestone 5 — Rescue Loop

Goal: prove that overboard incidents are tense but recoverable.

Deliverables:

- drowning meter;
- drowned state;
- body dragging;
- revival;
- all-drowned failure;
- extreme-distance body recovery.

### Milestone 6 — Region Slice

Goal: prove a 20–30 minute vertical slice.

Deliverables:

- start harbor;
- one authored region;
- one checkpoint;
- paper map;
- compass;
- spyglass;
- waves, fog, wind, reefs;
- basic director;
- save/load.

### Milestone 7 — Content Expansion

Deliverables:

- region chain generation;
- storms;
- lightning;
- fire;
- fish;
- rats;
- cannon;
- achievements;
- cosmetics;
- results screen.

---

## 33. Technical Risks

### 33.1 Players on a networked moving Rigidbody

Risk:

- jitter;
- desynchronization;
- incorrect friction;
- tunneling;
- host migration issues;
- ragdoll instability.

Mitigation:

- prototype first;
- keep ship root authoritative;
- carefully separate locomotion and platform velocity;
- avoid excessive independent ship bodies;
- test at poor latency early.

### 33.2 Rope complexity

Risk:

- excessive network data;
- unstable collision;
- expensive physics;
- impossible debugging.

Mitigation:

- replicate sparse control data;
- client-render spline;
- use limited collision proxies;
- avoid full chain-of-rigidbodies simulation for every rope.

### 33.3 Flooding simulation

Risk:

- scope explosion;
- difficult buoyancy coupling;
- expensive compartment simulation;
- visual mismatch.

Mitigation:

- begin with one numeric volume;
- add compartments only after the core loop works;
- keep visuals decoupled from authoritative water state.

### 33.4 Excessive physical objects

Risk:

- CPU cost;
- bandwidth cost;
- clutter;
- save complexity;
- unstable piles.

Mitigation:

- object budgets;
- sleep states;
- relevance culling;
- cleanup rules only for noncritical distant clutter;
- simplified collision meshes.

### 33.5 Full player freedom

Risk:

- griefing;
- irreversible soft-locks;
- critical item loss;
- impossible solo recovery.

Mitigation:

- embrace consequences;
- use checkpoints;
- provide multiple physical solutions;
- place optional recovery opportunities in the world;
- do not undermine the design with invisible protections.

### 33.6 Campaign duration

Risk:

- a 3–5 hour run may be too long for some groups;
- checkpoint sessions may fragment social continuity.

Mitigation:

- robust checkpoint saves;
- clear region duration targets;
- strong end-of-region cadence;
- easy resume flow.

---

## 34. Production Principles

1. Prototype the most dangerous technical assumptions first.
2. Do not build broad content before the ship is fun in an empty ocean.
3. Every new system must create at least one meaningful interaction with an existing system.
4. Avoid UI abstractions that bypass physical gameplay.
5. Prefer one expressive mechanic over five shallow mechanics.
6. Test with 2–4 players constantly; support for 8 should not dictate every interaction.
7. Solo play should remain possible but intentionally difficult.
8. All critical systems must be tested under latency and packet loss.
9. Save/load must be designed early because physical object state is central.
10. The director must never become a substitute for simulation quality.

---

## 35. Open Questions

The following topics remain intentionally undecided or require prototyping:

- exact visual style;
- final title;
- exact ship dimensions and layout;
- precise buoyancy method;
- Shared Mode production viability versus another Fusion topology;
- host migration requirements;
- number and geometry of flooding compartments;
- exact rope collision implementation;
- knot minigame design;
- sail tear representation;
- fire propagation complexity;
- cannon ammunition model;
- grog threshold pacing;
- hallucination content budget;
- exact region count per campaign;
- route generation rules;
- solo-assist concessions, if any;
- maximum persistent loose-object count;
- checkpoint snapshot implementation;
- console and macOS input/performance constraints;
- accessibility options;
- achievement count and reward cadence.

---

## 36. Final Product Definition

The game is a cooperative physical voyage in which the crew must keep one vulnerable ship operational across a dangerous procedural route.

There are no missions, no inventory, no classes, no upgrades, and no protected critical objects.

The ship moves because players physically operate it.

The ship survives because players notice problems and solve them.

The run becomes memorable because every solution can create a new problem.

The intended emotional arc is:

```text
confidence
→ mild confusion
→ coordination
→ cascading chaos
→ desperate recovery
→ temporary calm
→ worse chaos
→ exhausted arrival
```

That arc is the product.
