# Scrabble — Unity 6.5 version

A single-player Scrabble game (you vs. an ELO-scaled AI) for **Unity 6.5
(6000.5.3f1)**. This is a straight port of the previously-built Android game's
engine to C#, with a self-contained IMGUI front-end. **Coherence networking is
now included ** 

## What's in here

```
Assets/
  Scripts/
    Engine/            Pure-C# game engine (no Unity dependencies)
      ScrabbleData.cs  Letter values, tile distribution, premium board
      Dict.cs          Compact prefix trie (SOWPODS word list)
      Stemmer.cs       Inflected-form fallback for definitions
      Move.cs, Board.cs, MoveGen.cs, Bag.cs, Ai.cs, Game.cs
    ScrabbleApp.cs     IMGUI UI + flow + AI threading + save/resume
  StreamingAssets/
    words.txt          267,751-word Collins/SOWPODS list (2–15 letters)
    defs.tsv           Offline definitions for ~95% of playable words: wordset (MIT),
                       Wiktionary (CC BY-SA 4.0), Open English WordNet (CC BY 4.0),
                       Webster's 1913 (public domain), UWU (MIT). Inflections are
                       stored as ">BASE|note" pointers that ScrabbleApp follows.
    wordfreq.tsv       How common each word is (wordfreq, CC BY-SA 4.0); limits a
                       low-ELO AI to words a beginner would know
    DEFINITIONS_CREDITS.txt  Full sources/licences for defs.tsv and wordfreq.tsv
Packages/manifest.json
ProjectSettings/ProjectVersion.txt
```

## Features
- Full 15×15 board with premium squares, tile bag, racks, scoring
  (cross-words, letter/word premiums, 50-point bingo), pass, exchange.
- Real move-generating AI (Appel–Jacobson) whose strength scales with the ELO
  you enter (300 ≈ beginner … 2100 ≈ expert). Low ratings only play everyday
  words; higher ratings value the tiles they keep (blanks/S good, Q/V/duplicates
  bad) and swap a bad rack. Exchanges need at least 7 tiles in the bag.
- Tile point values shown on the rack; AI tiles animate dropping into place.
- Tap any played word to see its meaning (offline).
- Manual rack rearranging (Arrange), shuffle, and one-game save/resume.

## Requirements
- **Unity 6000.5.3f1** with **Android Build Support** (Android SDK & NDK Tools +
  OpenJDK) installed via Unity Hub.

## Run it in the Editor (no setup needed)
1. Open this folder as a project in Unity Hub (it will match 6000.5.3f1).
2. Press **Play**. The game bootstraps itself via
   `[RuntimeInitializeOnLoadMethod]` — you do **not** need to place anything in a
   scene, wire an EventSystem, or add a Canvas. The whole UI is drawn with IMGUI,
   which works regardless of the project's Input System setting.

## Build an Android APK
1. **File → New Scene**, choose an empty/basic scene, and **Save As**
   `Assets/Scenes/Main.unity`. (Any scene works — the game spawns itself on load;
   the scene just needs to exist so it can be added to the build.)
2. Open **File → Build Profiles** (Unity 6), switch the platform to **Android**,
   and **Add Open Scenes**.
3. In **Player Settings**:
   - *Other Settings → Identification*: set **Package Name** to
     `com.shaderpark.scrabble`, **Minimum API Level** 24+.
   - *Other Settings → Configuration*: **Scripting Backend = IL2CPP**, and tick
     **ARM64** (required for 64-bit devices / Play).
4. **Build** (or Build and Run with a device attached).

Notes:
- `words.txt`/`defs.tsv` live in **StreamingAssets** and are read at runtime with
  `UnityWebRequest` (required on Android, where StreamingAssets are inside the
  APK). First launch spends ~1s loading/parsing on a background thread.
- The saved game is written to `Application.persistentDataPath/savegame.dat`.

## Where Coherence will plug in (later)
The engine is deterministic and UI-agnostic, which makes the multiplayer add-on
clean:
- Make one host/simulator authoritative over `Bag`, both racks, and move
  validation (`Board.ValidateHuman`).
- Sync the shared state (225 board cells, scores, turn, tiles-left) via
  `CoherenceSync` variables; keep each rack private to its owner.
- Route `SubmitMove` / `Pass` / `Exchange` as Coherence **commands** to the
  authority, which validates and updates the synced state.
- `Ai` runs on the host for "vs computer" rooms.

## Caveat
The C# engine is a mechanical port of the Java engine that was tested with
300+ self-play games (0 tile/scoring/validation errors) and verified save/restore.
However, **no C# compiler was available in the build environment**, so these
scripts have not been compiled or run — Unity will surface any issues on first
import. The logic mirrors the tested Java 1:1; watch for trivial API differences
if anything doesn't compile.
