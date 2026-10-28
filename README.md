# thegameio Racing: Unity sample

This is a playable Unity 3D sample of the **thegameio Multiplayer Racing Game**, redesigned to match
`thegameio_Racing_Game.ppt`:

* 2 cars for now (Sport and Regular), from the client's own Asset Store models; more can be added
* 4 routes through a night-time fictional African coastal city
* a 10-car grid
* ordered checkpoints
* lowest-total-time winner rules with tie-breaks and DNF
* admin-created pools (max 10)
* pool-wise, daily, weekly and all-time leaderboards
* thegameio branding from the splash screen to the victory screen

Every PPT requirement is mapped to its implementation in
**[Docs/PPT_Requirements_Checklist.md](Docs/PPT_Requirements_Checklist.md)**. That file also records
the conflicts found in the brief and how each was resolved.

> This is an **offline sample**. Pools, entry fees, results and leaderboards are stored locally.
> There is no server, login, wallet, payment gateway or thegameio sync. The PPT excludes those from
> the current quotation. Other racers are AI-driven; online multiplayer is not implemented.

| | |
|---|---|
| Engine | **Unity 6000.0.38f1** (Unity 6 LTS) |
| Rendering | **URP 17.0.3**, Forward+, Linear colour space, ACES tonemapping and bloom |
| Real-world assets | Poly Haven scans (CC0), in `Assets/ThirdParty/PolyHaven`; see section 3 |
| Packages | `com.unity.render-pipelines.universal`, `com.unity.ugui`, `com.unity.test-framework` |
| Start scene | `Assets/Scenes/MainMenu.unity` (build index 0) |
| Routes | `Assets/Scenes/Route01_DowntownCircuit`, `Route02_WaterfrontRun`, `Route03_IndustrialPort`, `Route04_HillsideExpress` |
| Windows build | `Builds/Windows/thegameioRacing.exe` |

---

## 1. Open and play

1. In Unity Hub, add this folder and open it with **6000.0.38f1**.
2. Open **`Assets/Scenes/MainMenu.unity`** and press **Play**. You can also run `Builds/Windows/thegameioRacing.exe`.
3. The game goes **splash → main menu** and offers:
   * **Garage**: pick one of the cars and one of 10 paint colours.
   * **Quick race**: pick a route (1-4) and laps (4-10, default 4), then race 9 AI cars.
   * **Pools**: an admin panel creates pools, and you join an Open pool and race its members.
   * **Leaderboard**: views are Pool, Daily, Weekly and All-time.
4. Each race runs: branded loader → 10-car grid → 3-2-1-GO → race → **results / victory screen**.

You can also press Play directly in a route scene. It starts a quick race with default settings.

| Key | Action |
|---|---|
| W / Up | Accelerate |
| S / Down | Brake, then reverse |
| A D / Left Right | Steer |
| R | Reset to the last safe point on the road |
| Esc | Pause (resume / restart / main menu) |
| P | Autopilot on/off. This is a demo aid: the AI drives your car so the sample can be shown hands-free |

Command-line options for the `.exe` (demonstration and QA):
* `-demo` starts an autopilot quick race from the main menu. Add `-route N` (1-4) and `-laps N` (4-10) to choose.
* `-capture <folder>` walks splash → menu → garage → pools → leaderboard → route select → loader → race → results, saving a screenshot of each to the folder, then quits.
* `-fpslog` logs the frame rate every 5 seconds.

---

## 2. What is implemented

### Cars
**Client decision (2026-10-28): 2 cars for now, both from the client's own Asset Store models, with
more to be added later.** This replaces the PPT's 5 categories for the time being (checklist B1, N12).

| Car | Model | Source | Tuning |
|---|---|---|---|
| Sport | Apex GT | *ARCADE: FREE Racing Car* (Mena, Unity Asset Store) | 53 m/s top speed, quickest acceleration, 1,350 kg |
| Regular | thegameio Street | The client's normal car, **still being downloaded**. Until it is imported, the original generated model stands in | 48 m/s, balanced handling, 1,200 kg |

* **AI grid.** The 10-car grid spreads the AI cars across both models.
* **Paint.** Every car keeps the 10 paint swatches. The ARCADE car is palette-textured, so only its
  paint cells are repainted at runtime and trim, glass, lights and tyres keep their colours.
* **Stat bars.** These aren't hand-entered: `CarStats` computes them from each car's real
  `CarController` values.
* **Plates.** Every car carries a **thegameio front number plate** and the **`unisolutions.in`
  rear plate**, placed on the model's real bumpers.
* **Adding a car.** Add a `Style` entry in `Assets/Scripts/Editor/CarModelBuilder.cs` and point
  `importedModel` at the car's prefab or FBX. The builder then:
  * finds the wheels by name and makes them spin and steer;
  * converts Built-in materials to URP;
  * adds the plates, physics and AI.

  The catalog, garage cards, AI grid and stat bars follow automatically.
* **The ARCADE car's look.** It's a stylised low-poly model (flat palette colours), not a
  photoreal scan. Its silhouette is a modern muscle coupe with twin stripes and carries no
  manufacturer badges.

### Routes (4 confirmed, PPT slides 2, 5-8, 14)
| # | Route | Districts | Lap | Direction | Road width |
|---|---|---|---|---|---|
| 1 | Downtown Circuit | Downtown avenue · Commercial junction · Market corner · Residential street · Civic square | 1.35 km | clockwise (the concept loop map) | 18 m opening straight, 13-14 m elsewhere |
| 2 | Waterfront Run | Waterfront promenade · Hotel boulevard | 1.60 km | anticlockwise | 15-16 m |
| 3 | Industrial Port | Port quay · Container yard · Warehouses | 1.56 km | clockwise | 18-20 m (broad drift zones) |
| 4 | Hillside Express | Switchbacks (2 hairpins) · Ridge road above the city and sea | 1.68 km | anticlockwise | 12-13 m |

Each route has its own layout, corner sequence, scenery, 4 ordered checkpoints, start/finish
gantry, 10-car grid and minimap. The Market District from slide 14 is the market sector of Route 1
(see checklist N3).

**The city.** It's built from modular pieces. Real scanned surfaces and props replace the generated ones where they matter most (see section 3):
* **Road:** asphalt with lane markings, red/white kerbs in corners, gutters with drains, raised
  sidewalks, side-street junctions with zebra crossings, and continuous **jersey barriers** with
  colliders. Route 4 uses guardrails.
* **Buildings:** houses with boundary walls and gates, shops with storefronts, shutters, awnings and
  signs, apartments with balconies, offices, hotels, warehouses and skyline towers.
* **Street dressing:** street lights at a consistent 40 m spacing per side, palms and street trees,
  chevron direction boards, market stalls, a civic-square monument, thegameio billboards, containers,
  cranes, a ship, the sea, cliffs and the city below.
* **Spectators:** African spectators in everyday clothing stand only on the sidewalks behind the
  barriers.

### Race rules (PPT slide 12 + handwritten requirements)
* Default **4 laps**, configurable **4-10**.
* A lap counts only after checkpoints **1, 2, 3, 4 in order**, then the finish line in the driving
  direction. Wrong-way and skipped checkpoints never count.
* **Lowest total race time wins.** The tie-breaks are best single lap, then earliest finish.
  Unfinished cars are **DNF** once the race time limit (2 × par) runs out.
* Crossing times are interpolated to the moment each car crossed the line, so they don't depend on
  the physics step.
* **Results are re-checked locally before they are final.** The check covers lap count, checkpoint
  order on every lap, lap times adding up to the total, and no physically impossible lap. Server-side
  re-checking (PPT) needs a real server and isn't part of this sample.

### Pools and leaderboard (PPT slides 11-12)
* **Pools.** The admin panel creates pools with a name, entry fee, laps, route, start date/time and
  max 10 players. Pool cards show seats P1-P10, "x / 10 joined" and the status
  **Open / Full / Racing / Finished**.
  * The PPT examples Pool 1 (Open 7/10), Pool 2 (Full) and Pool 3 (Racing) are seeded as labelled
    sample data.
  * Joining takes a seat and starts the race with the joined racers.
  * The entry fee is shown but not charged.
* **Leaderboard.** It has Rank, Player, Best lap and Total time, with gold, silver and bronze for the
  top 3. Views are pool-wise, daily, weekly and all-time. It's built from locally stored results; rows
  from the presentation are marked "sample".
  * Data lives in `%USERPROFILE%\AppData\LocalLow\thegameio\thegameio Racing\thegameio_racing_sample.json`.
  * **Pools > Restore sample data** resets it.

### Branding
thegameio appears on the splash, the loader, the main menu and garage skin, the in-race HUD logo, the
start/finish gantry, checkpoint gates, trackside billboards, front number plates and the victory
screen. The colours were sampled from the PPT: amber #FFB400, red #E10600 and navy #141619.

**The logo is a placeholder wordmark.** Assign the real logo sprite, font and colours in
`Assets/Resources/BrandConfig.asset` and every screen and sign picks them up when the scenes are next
regenerated.

---

## 3. Real-world assets (Poly Haven, CC0)

The environment uses photo-scanned assets from [Poly Haven](https://polyhaven.com). All of them are
CC0 (public domain, free for commercial use, no attribution needed). The full list is in
`Assets/ThirdParty/PolyHaven/LICENSE.md`. In total they add 331 MB.

* **Surfaces.** The road, sidewalks, civic-square pavers, kerbs, gutters, quay walls and ground use
  scanned asphalt, concrete and paving. Building walls keep their generated window layouts, with real
  plaster, stucco, brick or concrete tile layered on as detail. Barriers and roofs get scanned
  concrete or corrugated-iron relief, and warehouses use a real corrugated factory wall.
* **Sky.** The night sky is the `qwantani_night_puresky` HDRI, darkened for a city night.
* **Trees.** The scanned trees are African wild syringa on the streets and a windswept coastal tree
  by the sea. Each scan is 1-2 million triangles with no lower LODs in the download, so the builder
  renders it once into a 3-view billboard (a "tree impostor", 6 triangles) that lines the routes.
* **Props.** These are real models:
  * heritage street lamps on the waterfront and civic square, with their own light;
  * roller shutters on closed shops;
  * utility cabinets on sidewalks;
  * manhole covers in the lanes;
  * rows of concrete road barriers closing each side street;
  * barrels in the port.

  Where a scan has LODs, only a lighter level is kept.
* **Fallback.** If the folder is removed, the generator falls back to its procedural versions
  (`PolyHaven.cs` returns null for missing assets).

**Still generated:** the Regular car until the client's normal car is imported, the building shapes
and the spectators. See the checklist, items N12-N14.

## 4. Project layout

```
Assets/
  Scenes/        MainMenu + Route01..04
  Scripts/       runtime (assembly RacingPrototype)
    Core/          RaceRules/RaceSession, BrandConfig, LocalDataStore (pools, results), SceneLoader (branded loader)
    Track/         TrackDefinition, TrackSpline, TrackPath, TrackCheckpoint, RouteInfo, RouteCatalog
    Cars/          CarController, CarStats, PlayerCarInput, AIController, CarColourCustomizer, CarCatalog, NitroExhaustFx, CarAudio
    Race/          RaceManager, LapManager, RaceResults (ranking + validator), RaceTimer, LeaderboardManager, CarSelection, RaceParticipant
    UI/            MenuUI (splash, menu, garage, routes, pools, leaderboard), RaceHUD (HUD + victory), Minimap, UIFactory
    Camera/        CameraFollow
    Editor/        generators: ProjectBuilder, RouteBuilder, TrackGeometryBuilder, BuildingKit, PropKit, CarModelBuilder, UrpSetup, ...
  Resources/     BrandConfig, CarCatalog, RouteCatalog, UI sprites, minimaps
  ThirdParty/    PolyHaven: CC0 scanned surfaces, HDRI sky, props and trees (+ LICENSE.md)
  Data/Routes/   the 4 TrackDefinition assets (control points, widths, sectors, junctions)
  Prefabs/       Cars (5), Environment (street light, palm, tree, stall, billboard, crane, monument, ...)
  Materials/     URP materials + generated textures (albedo, normal, emission)
  Models/        generated meshes (cars, props, spectators)
  Settings/URP/  pipeline asset, renderer, post-processing profiles
  Tests/PlayMode automated tests
Docs/            PPT checklist, screenshots
```

## 5. Regenerating content

The scenes and assets are generated by `Assets/Scripts/Editor/ProjectBuilder.cs`. **Hand edits to
the generated scenes are lost when you regenerate**, so change the generator or the route data
instead.

* **Racing Prototype > Rebuild Everything (menu + 4 routes)** regenerates everything.
* **Racing Prototype > Rebuild Selected Route Definition**: after editing a route in `Assets/Data/Routes`.
* **Racing Prototype > Capture Preview Images**: review stills written to `Logs/Screenshots`.
* **Racing Prototype > Build Windows Player**.
* Headless: `Unity -batchmode -projectPath <path> -executeMethod RacingPrototype.EditorTools.ProjectBuilder.BuildAllBatch`. Don't add `-quit`.

## 6. Tests

The PlayMode tests in `Assets/Tests/PlayMode` run from **Window > General > Test Runner**, or headless:
`Unity -batchmode -projectPath <path> -runTests -testPlatform PlayMode -testResults results.xml`.
Set `RACING_CAPTURE_DIR` to also save UI screenshots. The results are in the Verification section below.

## Verification (2026-10-28)

**Automated: 27 of 27 PlayMode tests pass** (headless, about 5 minutes). Every run was against
freshly regenerated scenes including the real assets.
* **Rules.**
  * Laps are clamped to 4-10.
  * A lap counts only after checkpoints 1-4 in order. Backwards crossings never count, and gate
    direction is detected.
  * Ranking is by total time, then best lap, then earliest finish.
  * The validator flags tampered results.
  * R puts the car back on the road facing forward.
* **Each of the 4 routes:**
  * The grid holds 10 cars in 2 columns × 5 rows.
  * Barriers stop a car leaving the track.
  * Buildings, spectators and checkpoints sit where they should.
  * A full 4-lap race with 10 cars finishes with 10/10 results validated and 0 resets.
* **Flows:**
  * Splash → menu → garage → route and laps → quick race starts the chosen setup.
  * Joining a pool, racing and finishing marks the pool Finished and updates the leaderboard.
  * A 10-lap race needs 10 full laps.
  * Restart keeps the setup.

**Built player** (`Builds/Windows/thegameioRacing.exe`, 353 MB, 0 build errors or warnings):
* Run with `-capture`, it went through every menu screen, the loader and a full 4-lap race on
  Route 2, then the results screen. 10/10 results were validated.
* Screenshots of every step are in `Docs/Screenshots`.
* **Frame rate** on the test machine (Intel UHD Graphics, 1920×1080 windowed):
  * 53-59 fps during the race, after 41 fps in the first 5 seconds after loading.
  * A Route 1 run earlier the same day, before the tree impostors, measured 44-56 fps.

**Not tested or not built:**
* No person has playtested the keyboard handling in this session. All races were driven by the AI
  or autopilot.
* WebGL and Android aren't built, because those modules aren't installed.
* Online multiplayer, server-side validation and thegameio sync aren't implemented. This is an
  offline sample.
