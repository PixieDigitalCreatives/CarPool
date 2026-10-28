# thegameio Racing: PPT requirements and implementation checklist

**Sources:** `thegameio_Racing_Game.ppt` (14 slides plus speaker notes). This is the authoritative spec.
Supporting sources are the handwritten-requirements brief (default 4 laps, laps 4-10, `unisolutions.in` on
the rear of each car) and `thegameio Racing — Demo Details & Costing.docx` (5 Oct 2026). The docx is used
only where the PPT is silent.

**Starting point:** the phase-1 prototype. That was a Unity 6000.0.38f1 Built-in RP project with one daytime
countryside track ("Green Valley"), five cars in different categories, a 5-car grid, working
checkpoint/lap/physics/AI systems, 11 PlayMode tests and a generated scene.

Status legend: **Keep** = existing system kept as is or refined · **Redo** = redesigned ·
**New** = added · **Note** = discrepancy or decision recorded.

## A. Product, branding and platform
| # | PPT requirement (slide) | Existing project | Action |
|---|---|---|---|
| A1 | thegameio branding: splash and loader, menu and garage skin, in-race HUD logo, track billboards, car number plates, victory screen (13) | Branded "unisolutions racing", no splash, loader or menu | **Redo/New.** `BrandConfig` asset (wordmark, colours, optional logo sprite) applied at every touchpoint. The logo is a *placeholder wordmark* until client assets arrive (13 notes: "Client supplies logo, colours and fonts"). |
| A2 | Brand colours: amber wordmark #FFB400, red #E10600, dark navy #141619 / #232833 (sampled from slides 1, 2, 11, 12) | Different palette | **Redo** |
| A3 | `unisolutions.in` on the rear of each car (handwritten) | Done | **Keep.** Rear plate = `unisolutions.in`. Front plate = thegameio + race number (see N1). |
| A4 | "Browser-based" game (1 notes); docx deliverable: WebGL + Android | Windows only; this editor has **no WebGL or Android module** | **Note.** Windows build validated. WebGL/Android need the modules installed (N7). |
| A5 | Wallet, payment gateway, airtime: excluded from quotation (13) | None | **Out of scope.** Entry fees are display-only and labelled. |

## B. Cars (slide 2 + notes)
| # | Requirement | Existing | Action |
|---|---|---|---|
| B1 | Exactly 5 categories: Regular, Fast & Furious style, Speedo, Classic Muscle, Street Rally | 5 cars in other categories | **Client-approved deviation (2026-10-28):** 2 cars for now, Sport and Regular, from the client's own Asset Store models. More will be added later. The line-up is data-driven, one `Style` per car (N12) |
| B2 | Each has its own speed, acceleration and handling; bars indicative (2 notes) | Hand-entered bars | **Redo.** Bars are *computed* from the real `CarController` values. The garage also shows top speed and 0-100 km/h. |
| B3 | Original designs and names; "Fast & Furious" is a style description only, no film branding | n/a | **New.** Original models. Category labels per PPT, original model names per docx (N2). |
| B4 | Body, wheels, glass, lights, materials, consistent scale | Done | **Redo** with the new silhouettes |
| B5 | Car selection and colour customisation | Done (in-scene garage) | **Redo** in the branded garage screen of the menu scene |

## C. Routes and environment (slides 2-10, 14)
| # | Requirement | Existing | Action |
|---|---|---|---|
| C1 | **4 confirmed routes**, each with its own layout, corners, scenery, checkpoints, start grid and minimap (2) | 1 track | **New.** 4 route scenes |
| C2 | Five districts: Downtown, Waterfront, Market, Industrial Port, Hillside (14). Slide 14 also says "5 route layouts" | n/a | **Note (N3).** Kept to 4 routes. Route 1 Downtown Circuit contains Downtown and Market; Routes 2-4 = Waterfront, Port, Hillside. |
| C3 | Route 1 follows the concept loop: clockwise, start/finish → downtown avenue → commercial junction → market corner → residential street → civic square (5-8) | n/a | **New** |
| C4 | Night-time fictional African coastal metropolis; restrained street lighting; selected lit windows and shop signs; dry roads; mixed small/large buildings (3, 6-8, 14) | Daytime countryside | **Redo.** Night lighting, modular buildings, URP post-processing |
| C5 | Road: asphalt, lane markings, kerbs, sidewalks, medians, junctions, drains, pavement transitions (9, 14) | Road, kerbs, run-off | **Redo/New** |
| C6 | Buildings: houses, shops/storefronts, apartments, offices, tall skyline; awnings, shutters, balconies, boundary walls, roofs (6-9) | Grandstand and pit only | **New** modular generator |
| C7 | Dressing: streetlights, direction boards, barriers, street furniture, trees, shop signs, spectator zones (9, 14) | Lamps, trees, billboards | **Redo/New** |
| C8 | African spectators in contemporary everyday clothing, on sidewalks behind barriers (4, 7) | None | **New.** Low-poly, palette-varied, placed only beyond the barriers |
| C9 | Keep props away from racing lines; barriers define turns; clear sightlines (7) | n/a | **New.** Placement rules plus automated checks |
| C10 | Lighting: consistent streetlight spacing, modest signs, readable skyline (6) | n/a | **New** |

## D. Grid and race rules (slides 4, 12 + handwritten)
| # | Requirement | Existing | Action |
|---|---|---|---|
| D1 | 10-car grid, 2 columns × 5 rows, visible start line, wider opening straight (4) | 5 slots | **Redo** |
| D2 | Default 4 laps, configurable 4-10 (handwritten). Pool example "e.g. 3" (11) | Done | **Keep.** Pool form also 4-10 (N4) |
| D3 | Checkpoint validation: no shortcuts or wrong way; missed checkpoint = lap not counted (12) | Done and tested | **Keep** on all 4 routes |
| D4 | Minimum total time wins; tie-break best lap, then earliest finish; unfinished = DNF (12) | First finisher wins, no DNF | **Redo.** Ranking + DNF + race time limit (docx: 2× par) |
| D5 | Times re-checked before results are final (12) | None | **New.** Local result validator. *Not* server-side (N5) |
| D6 | Barriers on both sides with real collision | Done | **Keep/Redo** visuals (jersey barriers, guardrails) |

## E. Pools (slide 11)
| # | Requirement | Existing | Action |
|---|---|---|---|
| E1 | Admin creates pools (Pool 1, 2, 3 … not limited) with name, entry fee (set by admin), laps, route 1-4, start date/time, max 10 | None | **New.** Admin panel in the pools screen (local sample, no web backend) |
| E2 | Pool cards with P1-P10 slot grid, "x / 10 joined", status Open / Full / Racing / Finished | None | **New** |
| E3 | Flow: admin creates → user pays fee → joins (max 10) → race starts → results & prizes | None | **New.** "Pays" is shown but not charged. Prizes are not implemented. Labelled |

## F. HUD, leaderboard, results (slides 2, 12, 13)
| # | Requirement | Existing | Action |
|---|---|---|---|
| F1 | HUD: branding, lap/total, time, speed, position, checkpoint feedback, minimap | Most items, no minimap or logo | **Redo** |
| F2 | Leaderboard: rank, player, best lap, total time; all pool players; gold/silver/bronze top 3; views pool-wise, daily, weekly, all-time (12) | Live in-race table only | **New** leaderboard screen with locally stored results. "Synced with thegameio" is shown as *not connected* (N6) |
| F3 | Victory screen: winner, rank, best lap, total time, top 3 | Results panel | **Redo** with branded podium and trophies |

## G. Real-world asset pass (follow-up request: "use real assets, not self-made")
| # | Item | Status |
|---|---|---|
| G1 | Road, sidewalk, plaza, kerb, ground, quay surfaces | **Done.** Poly Haven CC0 scans: asphalt, concrete pavement, pavers, concrete |
| G2 | Building walls, roofs, barriers, warehouses | **Done.** Real plaster, stucco, brick and concrete-tile detail on the generated facades. Corrugated iron, concrete and factory wall scans |
| G3 | Night sky | **Done.** CC0 HDRI (qwantani_night_puresky), darkened |
| G4 | Street trees and coastal trees | **Done.** Scanned African wild syringa and an island tree, as 3-view billboard impostors. The scans are 1-2 M triangles with no LODs |
| G5 | Street props | **Done.** Heritage lamps, roller shutters, utility cabinets, manholes, concrete road barriers at side streets, port barrels |
| G6 | Realistic cars | **In progress** (N12). Sport uses the client's ARCADE car; Regular waits for the client's normal car |
| G7 | Realistic spectators and building geometry | **Not available as free, no-login assets** (N13, N14) |

## Discrepancies and decisions needing approval
* **N1 Number plates.** The PPT lists "car number plates" as a thegameio touchpoint, but the handwritten brief requires
  `unisolutions.in` on the rear. Implemented: rear plate `unisolutions.in`, front plate `thegameio` plus race number.
* **N2 Car names.** The PPT uses "Fast & Furious style" and "Speedo". The docx warns both are trademarks and
  suggests original names. Implemented: PPT category labels, with the docx's original model names
  (thegameio Street, Turbo Fury, Speedster X) plus two new ones for Muscle and Rally. Each label is one data field.
  *Currently moot:* only Sport ("Apex GT") and Regular ("thegameio Street") are in the game (N12).
* **N3 Route count.** Slides 1, 2 and the docx say 4 routes; slide 14 says "5 route layouts". Kept at 4. The Market
  District is the market sector of Route 1.
* **N4 Laps.** The pool form example is "e.g. 3" and the docx allows any number. The handwritten rule (4-10, default 4) is
  applied everywhere.
* **N5 Anti-cheat.** The PPT specifies server-side re-checking. This offline sample re-checks locally only.
* **N6 Sync.** The thegameio leaderboard sync (SSO, API) needs thegameio API access. Shown as "not connected".
* **N7 Platforms.** The docx deliverables are WebGL and Android, but only the Windows module is installed here.
* **N8 Tie-break.** The PPT says "earliest finish" second; the docx says "prize split". The PPT rule is implemented.
* **N9 Leaderboard ranking.** The PPT pool view ranks by total time. Daily, weekly and all-time views rank by wins,
  then best lap (docx rule, since the PPT doesn't define aggregate ranking).
* **N10 Hillside Express "elevated roads".** The car physics is flat-ground arcade. The route sits on an elevated
  plateau with cliffs and the city below, but the road itself has no slopes.
* **N12 Car models.** By client decision the game uses 2 cars for now, both from the client's own
  Asset Store models:
  * **Sport** is *ARCADE: FREE Racing Car* (Mena), imported. It's stylised low-poly, not photoreal, and
    carries no badges. Its modern muscle-coupe shape may still remind people of a real model; confirm
    that's acceptable.
  * **Regular** will be the client's normal car once downloaded and imported. Until then the generated
    model stands in.

  The PPT's other categories (Fast & Furious style, Speedo, Street Rally) are dropped for now and can
  return as more `Style` entries.
* **N13 Spectators.** These are still blocky low-poly figures. Realistic people need Mixamo (an Adobe
  account) or a licensed people pack.
* **N14 Building geometry.** Buildings are generated box shapes with real surface textures.
  Fully modelled realistic buildings need a licensed city pack.
* **N11 Old track.** The daytime "Green Valley" track conflicts with the art direction and would be a 5th route. It is
  removed from the build.
