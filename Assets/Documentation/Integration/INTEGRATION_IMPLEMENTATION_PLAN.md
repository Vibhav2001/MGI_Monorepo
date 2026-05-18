## MGI Offline Integration – 2–3 Week Implementation Plan

This plan turns `SYSTEM_DATA_ARCHITECTURE.md` into a concrete **2–3 week roadmap** for all teams, and is reflected in the living overview `SYSTEM_ARCHITECTURE_OVERVIEW.md`:

- Economy
- Progression
- CCAS (Acquisition / Packs & Cards)
- Coaches
- Facilities

The focus is to:

- Stand up **offline‑first services and JSON storage**.
- Replace **temporary PlayerPrefs / HTTP / mock data** with real shared services.
- Establish **stable contracts** so each team can safely ask an LLM to generate code against those contracts.

Time is divided into **phases**, but some work can be done in parallel where dependencies allow.

---

## 0. Global Prerequisites (All Teams)

These tasks are the foundational building blocks and have now been **implemented**. Future work in this plan assumes they are available.

- **0.1 Shared Runtime Paths & Utility (FilePathResolver)**
  - **Status**: ✅ Implemented (`Assets/Scripts/Shared/Storage/FilePathResolver.cs`).
  - **Goal**: All teams share the same conventions for reading/writing JSON to disk.
  - **What it does**
    - Provides:
      - `GetPlayerDataRoot(playerId)`
      - `GetEconomyPath(playerId, fileName)`
      - `GetProgressionPath(playerId, fileName)`
      - `GetCCASPath(playerId, fileName)`
      - `GetFacilitiesPath(playerId, fileName)`
      - `GetCoachesPath(playerId, fileName)`
      - `GetEventsLogPath()`
      - `GetProcessedEventsPath()`
    - Uses `Application.persistentDataPath/mgi_state` as the root and ensures all directories exist.
  - **Why it matters**
    - Centralizes the storage layout described in `SYSTEM_DATA_ARCHITECTURE.md` so every service writes to the same structure.
    - Future changes to the on‑disk layout only require edits in one place (`FilePathResolver`) instead of across all services.
  - **How it will be used**
    - All upcoming services in this plan (Economy/Progression/CCAS/Facilities/Coaches) should call these helpers whenever they read or write JSON.
    - The new `SYSTEM_ARCHITECTURE_OVERVIEW.md` file documents this behavior at a high level.

- **0.2 Minimal Shared EventBus**
  - **Status**: ✅ Implemented (`Assets/Scripts/Shared/Events/EventBus.cs`).
  - **Goal**: Single, simple abstraction for file‑backed, local events.
  - **What it does**
    - Exposes:
      - `void Publish(EventEnvelope evt);`
      - `IDisposable Subscribe(string eventType, Action<EventEnvelope> handler);`
    - `EventEnvelope` fields:
      - `string event_id`
      - `string event_type`
      - `string player_id`
      - `string timestamp`
      - `string payloadJson` (opaque JSON string for event‑specific data)
    - Maintains in‑memory subscribers and appends each published event as a JSON line to `events.log.jsonl` using `FilePathResolver.GetEventsLogPath()`.
  - **Why it matters**
    - Realizes the event flow pattern described in `SYSTEM_DATA_ARCHITECTURE.md` (file‑backed event bus with idempotent processing).
    - Decouples producers (e.g. CCAS/Coaches/Facilities) from consumers (Economy/Progression) by using `event_type` and payloads instead of direct calls.
  - **How it will be used**
    - Future tasks (pack purchase, coach hire, facility upgrade) will publish domain events (`buy_pack`, `hire_coach`, `upgrade_facility`, etc.) as `EventEnvelope` instances.
    - Consumers can subscribe by `event_type` to react or build analytics without changing producer code.

---

## Phase 1 (Week 1) – Establish Core Services & Canonical IDs

### 1. Economy Team – EconomyService & Wallet Canonicalization

- **1.1 Define Canonical Wallet & Transaction Models**
  - **Goal**: Align on a single wallet and transaction schema as described in `SYSTEM_DATA_ARCHITECTURE.md`.
  - **Work**
    - Create `Assets/Scripts/Economy/Backend/Models.cs` with:
      - `Wallet` class: `player_id`, `coins`, `gems`, `coaching_credits`, `last_updated`.
      - `WalletTransaction` class: `id`, `player_id`, `amount`, `currency`, `type`, `timestamp`, `source`.
    - Write/update JSON schemas in `Assets/StreamingAssets/Economy`:
      - `wallet_schema.json`, `wallet_transactions_schema.json` documenting these fields.
  - **Dependencies**: None, but coordinate `player_id` format with Progression.

- **1.2 Implement EconomyService**
  - **Goal**: Single entry point for all wallet operations.
  - **Work**
    - Create `Assets/Scripts/Economy/Backend/EconomyService.cs`:
      - Methods:
        - `Wallet GetWallet(string playerId, bool createIfMissing = true)`
        - `bool TrySpend(string playerId, int coins, int gems, string source, out Wallet updatedWallet)`
        - `void AddCurrency(string playerId, int coins, int gems, string source)`
        - `IEnumerable<WalletTransaction> GetRecentTransactions(string playerId, int limit)`
      - Implementation:
        - Use `FilePathResolver` to read/write `wallet.json` and `wallet_transactions.json`.
        - Generate GUIDs for `WalletTransaction.id`.
        - Ensure writes are atomic (e.g. write temp + move).
    - Integrate with `EventBus`:
      - On `TrySpend` / `AddCurrency`, optionally publish `wallet_updated` event.
  - **Dependencies**: `FilePathResolver`, `EventBus`.

- **1.3 Migrate Demo UI (`WalletInfoPanel`) to EconomyService**
  - **Goal**: Remove hard‑coded balances from `WalletInfoPanel.cs`.
  - **Work**
    - Update `WalletInfoPanel`:
      - On `Start()`, call `EconomyService.GetWallet(playerId)` instead of using default fields.
      - Subscribe to `EventBus` (`wallet_updated`) or `EconomyService` events to refresh labels.
      - Use `Wallet` values for coin/gem/credit labels.
    - Ensure `TransactionLedgerPanel` reads from `EconomyService.GetRecentTransactions`.
  - **Dependencies**: `EconomyService`.

### 2. Progression Team – ProgressionService & Player Canonicalization

- **2.1 Define Player & PlayerProgressionState Models**
  - **Goal**: Ensure everyone uses the same `player_id` representation.
  - **Work**
    - Create `Assets/Scripts/Progression/Models/PlayerModels.cs`:
      - `Player` (minimal): `player_id`, `display_name`, `created_at`.
      - `PlayerProgressionState`:
        - `player_id`, `current_xp`, `current_tier`, `List<XpHistoryEntry> xp_history`.
      - `XpHistoryEntry`: `id`, `player_id`, `timestamp`, `xp_gained`, `source`.
    - Write `progression_state_schema.json` & `xp_history_schema.json` in `StreamingAssets/Progression`.
  - **Dependencies**: Coordinate `player_id` format with Economy.

- **2.2 Implement ProgressionService**
  - **Goal**: Central service to manage XP and tiers offline.
  - **Work**
    - Create `Assets/Scripts/Progression/Backend/ProgressionService.cs`:
      - Methods:
        - `PlayerProgressionState GetState(string playerId, bool createIfMissing = true)`
        - `void AddXp(string playerId, int xp, string source)`
        - `TierData GetCurrentTier(string playerId)`
      - Implementation:
        - Load `progression/progression.json` as read‑only config.
        - Read/write `progression_state.json` and `xp_history.json` via `FilePathResolver`.
        - When XP changes:
          - Append `XpHistoryEntry`.
          - Recalculate `current_tier` from `tier_progression`.
          - Publish `xp_updated` event via `EventBus`.
  - **Dependencies**: `FilePathResolver`, `EventBus`.

- **2.3 Stub Offline Replacement for ApiClient**
  - **Goal**: Begin decoupling from HTTP‑based `ApiClient` in `SeasonManager`.
  - **Work**
    - Introduce an interface `ISeasonBackend` with methods currently used by `SeasonManager` (e.g. `CreateSeason`, `SimulateWeek`, `GetPlayerProgression`).
    - Create `LocalSeasonBackend` that uses JSON files (even if logic is stubbed) while `ApiClient` remains as a fallback during transition.
    - Update `SeasonManager` to depend on `ISeasonBackend` instead of concrete `ApiClient`.
  - **Dependencies**: None (can be partially stubbed this week).

### 3. CCAS Team – CCASService Skeleton & Pack/Collection Models

- **3.1 Formalize Card, PackType, Collection, and PackHistory Models**
  - **Goal**: Align runtime models with `phase1_config.json` and card catalog.
  - **Work**
    - Under `Assets/Scripts/CCAS/Backend/Models.cs`:
      - `Card` (match catalog).
      - `PackType` (use existing `CCAS.Config.PackType`).
      - `CardCollectionEntry`: `player_id`, `card_id`, `quantity`, `first_acquired_at`.
      - `PackDropHistoryEntry`: `id`, `player_id`, `pack_type_id`, `cost_paid`, `cards_pulled[]`, `timestamp`.
    - Add or update schemas in `StreamingAssets/CCAS`:
      - `card_collection_schema.json`, `pack_drop_history_schema.json`.
  - **Dependencies**: Coordination with Progression for XP sources, Economy for spending sources.

- **3.2 Implement CCASService (Skeleton)**
  - **Goal**: Central place for pack opening logic.
  - **Work**
    - Create `Assets/Scripts/CCAS/Backend/CCASService.cs`:
      - Methods (can be stubbed initially):
        - `PackResult OpenPack(string playerId, string packTypeId)`
        - `IEnumerable<CardCollectionEntry> GetCollection(string playerId)`
      - Phase 1 implementation:
        - Load `cards_catalog.json` & `phase1_config.json`.
        - Use existing `DropConfigManager` logic or share it to roll cards.
        - **For this week**, just return a `PackResult` without writing to disk; file I/O can be phased in in Phase 2.
  - **Dependencies**: None at first; Economy & Progression integration comes in next phase.

---

## Phase 2 (Week 2) – Wire Cross‑Team Flows & Replace Legacy Paths

### 4. CCAS + Economy + Progression – Pack Purchase End‑to‑End

- **4.1 Integrate CCASService with EconomyService**
  - **Goal**: Pack opening always checks and updates wallet.
  - **Work**
    - Update `CCASService.OpenPack`:
      - Given `playerId`, `packTypeId`:
        - Resolve `PackType` and cost.
        - Call `EconomyService.TrySpend(playerId, costCoins, costGems, source: "pack_purchase")`.
        - If insufficient, return a `PackResult` with failure status; no cards rolled.
      - On success:
        - Roll cards.
        - Prepare `cards_pulled` with `is_duplicate` and tentative `xp_awarded` (computed next step).
    - Append a `WalletTransaction` through `EconomyService`.
  - **Dependencies**: `EconomyService` from Phase 1.

- **4.2 Implement Duplicate Detection & XP Awards**
  - **Goal**: Duplicate cards grant XP via ProgressionService.
  - **Work**
    - In `CCASService.OpenPack`:
      - Load existing `CardCollectionEntry` list for `playerId` from `card_collection.json`.
      - For each pulled `Card`:
        - If `card_id` already present ⇒ duplicate.
        - Read `DuplicateXPRule` from `phase1_config.json` (or `CCASConfigRoot.duplicate_xp`).
        - Compute `xp_awarded` based on rarity.
        - Call `ProgressionService.AddXp(playerId, xp_awarded, source: "duplicate_card_<rarity>")`.
      - Update `card_collection.json` with new quantities.
    - Log pack results:
      - Append `PackDropHistoryEntry` to `pack_drop_history.json`.
    - Publish `buy_pack` event through `EventBus` with the payload structure from `SYSTEM_DATA_ARCHITECTURE.md`.
  - **Dependencies**: `ProgressionService`, `EventBus`.

- **4.3 Connect PackOpeningController to CCASService**
  - **Goal**: UI uses the service instead of raw `DropConfigManager`.
  - **Work**
    - In `PackOpeningController.OpenPackOfType` and `OpenPack`:
      - Replace direct `DropConfigManager.PullCards` call with `CCASService.OpenPack(playerId, packTypeKey)`.
      - Use returned `PackResult.cards` to render UI (still via `CardView`).
      - Handle insufficient funds by showing an appropriate error UI.
    - Optionally, keep legacy path as fallback during transition (guarded by a flag).
  - **Dependencies**: `CCASService`, `EconomyService`, `ProgressionService`.

### 5. Facilities + Economy + Progression – Facility Upgrade Offline

- **5.1 Replace HTTP in FacilityUpgradeHandler**
  - **Goal**: Use FacilitiesService instead of HTTP backend.
  - **Work**
    - Create `Assets/Scripts/Facilities/Backend/FacilitiesService.cs`:
      - Methods:
        - `bool TryUpgradeFacility(string playerId, string facilityTypeId, out PlayerFacilityProgress newState)`
      - Implementation:
        - Read `facilities_config.json` (static definitions).
        - Read `player_facilities.json` (player state).
        - Find current level and cost from config.
        - Use `EconomyService.TrySpend(playerId, costCoins, costGems, source: "upgrade_facility")`.
        - If success, increment level, save `player_facilities.json`, publish `upgrade_facility` event.
    - Update `FacilityUpgradeHandler.OnUpgradeButtonClick`:
      - Instead of building a JSON payload and sending `UnityWebRequest`:
        - Call `FacilitiesService.TryUpgradeFacility(playerId, facilityTypeId, out var newState)`.
        - On success, call `FacilityDetailsHandler.RefreshFromLocalState(newState)` (a new method) instead of `RefreshFromServer`.
  - **Dependencies**: `EconomyService`, `FilePathResolver`.

- **5.2 Split Facilities Config vs Player State**
  - **Goal**: Normalize `facilities.json` into two files.
  - **Work**
    - Extract facility definitions into `StreamingAssets/Facilities/facilities_config.json`:
      - `facility_type_id`, `display_name`, `max_level`, `levels[]` (costs, benefits, validation).
    - Create runtime `player_facilities.json` under persistent data:
      - `player_facilities[]` with per‑player levels only (as shown in `SYSTEM_DATA_ARCHITECTURE.md`).
    - Update FacilitiesService to write only `player_facilities.json`.
  - **Dependencies**: none (owned by Facilities).

- **5.3 Progression: Apply Facility Multipliers in XP Calculations**
  - **Goal**: Facilities influence XP via `ProgressionService`.
  - **Work**
    - Extend `ProgressionService.AddXp`:
      - Before finalizing XP, look up `PlayerFacilityState` via `FacilitiesService` (or a lightweight reader).
      - Apply relevant multipliers (e.g. weight room strength bonus to training XP).
      - Ensure this is **read‑only** access to Facilities data.
  - **Dependencies**: Facilities normalized config/state, `FacilitiesService` or read helpers.

### 6. Coaches + Economy + Progression – Coach Hiring

- **6.1 Implement CoachesService**
  - **Goal**: Single place for coach catalog and hiring logic.
  - **Work**
    - Create `Assets/Scripts/Coaches/Backend/CoachesService.cs`:
      - Methods:
        - `IEnumerable<Coach> GetAvailableCoaches(string playerId)`
        - `bool TryHireCoach(string playerId, string teamId, string coachId, out Coach updatedCoach)`
      - Implementation:
        - Load coach catalog from `StreamingAssets/Coaches/coaches_schema_data.json` (or new `coaches_catalog.json`).
        - Read/write `teams.json` and/or `coach_contracts.json` in persistent data.
        - Use `EconomyService.TrySpend(playerId, coachCost, 0, source: "coach_hiring")`.
        - On success, update team’s coach fields and publish `hire_coach` event.
  - **Dependencies**: `EconomyService`.

- **6.2 Integrate Coaching UI with CoachesService**
  - **Goal**: All coach hiring flows go through the service.
  - **Work**
    - Identify existing UI scripts (e.g. `BudgetHud`, any coach selection UI).
    - Modify button handlers to call `CoachesService.TryHireCoach`.
    - Update UI to reflect hired coaches from `teams.json`.
  - **Dependencies**: `CoachesService`.

- **6.3 Progression: Recognize Coach Bonuses (Optional in Week 2)**
  - **Goal**: Coach assignments influence XP gains.
  - **Work**
    - Define a simple mapping in `coaches/coaches_bonus_config.json` (e.g. offence coach adds +5% XP from offensive drills).
    - In `ProgressionService.AddXp`, consult `CoachesService` (read‑only) for active coaches and apply bonuses on top of facility multipliers.
  - **Dependencies**: `CoachesService`.

---

## Phase 3 (Week 3) – Hardening, Tooling, and LLM‑Friendly Contracts

### 7. Cross‑Team – Document Contracts & Provide Stubs

- **7.1 Contract Documentation Files per Team**
  - **Goal**: Make it trivial to ask an LLM for new features without breaking contracts.
  - **Work**
    - For each subsystem, create a short `README` under its folder, e.g.:
      - `Assets/Documentation/Integration/Economy_CONTRACT.md`
      - `Assets/Documentation/Integration/Progression_CONTRACT.md`
      - `Assets/Documentation/Integration/CCAS_CONTRACT.md`
      - `Assets/Documentation/Integration/Facilities_CONTRACT.md`
      - `Assets/Documentation/Integration/Coaches_CONTRACT.md`
    - Each contract file should:
      - Enumerate **public service methods** (signatures & behavior).
      - Specify **owned JSON files** and their schemas.
      - List **events produced/consumed** with example payloads.
  - **Dependencies**: Services must exist from Phases 1–2.

### 8. Stability & Idempotency

- **8.1 Event Processing Idempotency**
  - **Goal**: Ensure re‑processing events does not corrupt state.
  - **Work**
    - In each service that consumes events (Economy, Progression, etc.):
      - Maintain `processed_events.json` with `event_id` entries.
      - Before processing an event, check if `event_id` is already present.
      - Only apply side effects if it’s new; then record id.
    - This makes EventBus replays safe.
  - **Dependencies**: `EventBus` in use, events carrying `event_id`.

- **8.2 Basic Error Handling & Validation**
  - **Goal**: Prevent corrupt JSON or mismatched IDs.
  - **Work**
    - Each service:
      - Validates required fields (e.g. `player_id`, `card_id`, `facility_type_id`) when loading.
      - Logs warnings or errors for unknown ids (e.g. pack refers to unknown card).
    - Optionally, add a small validation menu item in Unity (`Tools > Validate Data`) that runs a simple check:
      - Loads all config/state JSONs and checks referential integrity (FK checks described in `SYSTEM_DATA_ARCHITECTURE.md`).
  - **Dependencies**: All schemas defined and used.

### 9. Optional–Nice‑to‑Have in Week 3

- **9.1 Simple In‑Editor Dashboards**
  - **Goal**: Help designers/engineers see state quickly.
  - **Work**
    - Create one debug screen (or editor window) per subsystem that:
      - Shows JSON contents (wallet, progression, card collection, facilities levels, coaches).
      - Buttons to “Reset Player State” for quick iteration.
  - **Dependencies**: Services stable.

- **9.2 Additional Flows (Match Results → Economy & Progression)**
  - **Goal**: Tie match simulation into the same pattern.
  - **Work**
    - When `SeasonManager` simulates a week:
      - For each match result, publish a `match_result` event with XP and rewards.
      - EconomyService handles rewards (coins/gems).
      - ProgressionService handles XP.
  - **Dependencies**: `LocalSeasonBackend`, `ProgressionService`, `EconomyService`.

---

## 10. Dependency Summary (Who Waits on Whom?)

- **Global**
  - All teams benefit from **FilePathResolver** and **EventBus**, but can stub locally if needed.

- **Economy**
  - Can start immediately (Phase 1).
  - Other teams depend on:
    - `EconomyService.TrySpend`
    - `EconomyService.GetWallet`

- **Progression**
  - Can define models & service in Week 1 independently.
  - CCAS and Facilities **consume** `ProgressionService.AddXp` in Phase 2.

- **CCAS**
  - Week 1: Define models & skeleton `CCASService` using local config only.
  - Week 2: Needs `EconomyService` and `ProgressionService` to complete pack flow.

- **Facilities**
  - Week 2:
    - Relies on `EconomyService` for spending.
    - Optionally interacts with `ProgressionService` for multipliers.

- **Coaches**
  - Week 2:
    - Relies on `EconomyService` for hire cost.
    - Optionally interacts with `ProgressionService` for XP bonuses.

In practice:

- **Week 1**: Economy + Progression focus on services; CCAS preps models/service; Global utilities built.  
- **Week 2**: CCAS, Facilities, Coaches integrate with Economy & Progression; legacy PlayerPrefs/HTTP usages are removed.  
- **Week 3**: Contracts, idempotency, validation, and optional tooling hardened for LLM‑friendly workflows and future features. 

