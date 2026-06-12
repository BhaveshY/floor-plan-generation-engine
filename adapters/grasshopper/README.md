# Grasshopper Adapter — EBA Floor Plan Generator

Generate floor plans from a written brief directly inside Grasshopper, preview
them as curves, and bake them to the contract layers (`FP::Generated::…`) —
using the same engine, AI brief parsing (Claude/Codex) and validation as the
web studio. Works in **Rhino 7 and Rhino 8**, no plugins or packages to install.

## 1. Start the engine server (once)

From the repo folder:

- Windows: `scripts\run-web.ps1`
- macOS/Linux: `./scripts/run-web.sh`

This self-installs a local .NET 8 SDK if needed and serves
`http://localhost:5127`. Leave it running. (Optional: with a `claude` or
`codex` CLI installed and logged in, briefs are read by AI; otherwise a
built-in parser handles common phrasings.)

## 2. Create the component

1. Open Grasshopper and drop a Python script component onto the canvas:
   - **Rhino 7**: `Maths → Script → GhPython Script`
   - **Rhino 8**: `Maths → Script → Python 3 Script` (or GhPython)
2. Zoom in on the component and add/rename inputs and outputs to match the
   tables below (right-click each input for *Type hint* and *Item Access*).
3. Double-click the component and paste the entire contents of
   [`fp_generate.py`](fp_generate.py). Done.

### Inputs

| Name       | Type hint | Access | Wire up                                | Required |
|------------|-----------|--------|----------------------------------------|----------|
| `brief`    | str       | item   | Panel — e.g. `a 2 bhk apartment`        | yes |
| `run`      | bool      | item   | Button or Toggle                        | yes |
| `bake`     | bool      | item   | Button — bake to Rhino layers           | no |
| `variant`  | int       | item   | Slider 0–3 — ranked variant to show     | no (0 = best) |
| `seed`     | int       | item   | Slider — override the layout seed       | no (brief-derived) |
| `variants` | int       | item   | Slider 1–20 — how many to generate      | no (4) |
| `use_ai`   | bool      | item   | Toggle — Claude/Codex reads the brief   | no (True) |
| `provider` | str       | item   | Panel — `claude` or `codex`             | no (server default) |
| `base_url` | str       | item   | Panel — engine URL                      | no (`http://localhost:5127`) |

### Outputs

| Name          | Content                                      |
|---------------|----------------------------------------------|
| `status`      | one-line run report (status, validity, score, seed, parser) |
| `rooms`       | closed PolylineCurves, one per room          |
| `room_names`  | `Bathroom`, `Bedroom`, … (parallel to rooms) |
| `room_areas`  | m² per room                                  |
| `units`       | closed PolylineCurves, one per unit          |
| `unit_names`  | unit type per curve                          |
| `walls`       | LineCurves (wall centerlines)                |
| `doors`       | Point3d door locations                       |
| `corridors`   | corridor outlines (empty for single dwellings) |
| `labels_text` | label strings                                |
| `labels_pts`  | Point3d label anchors                        |

## 3. Use it

- Type a brief in the panel — `a 1 room kitchen apartment`, `a 3 bhk apartment
  with a bright living room`, `residential floor with studios 36 x 20`,
  `l-shaped floor with a mix of studios and two beds` — and press **run**.
- With `use_ai` on, expect 10–20 s while Claude/Codex reads the brief; the
  built-in parser is instant. The `status` output names which parser ran.
- Slide `variant` to flip through the ranked alternatives; change `seed` to
  explore new schemes for the same brief.
- Press **bake** to write the variant into Rhino on the contract layers
  (`FP::Generated::Units/Rooms/Corridors/Walls/Doors`), with object names and
  the stable `externalId` stored as a user string on every object — ready for
  downstream BIM/IFC workflows per
  [`docs/rhino-grasshopper-adapter-contract.md`](../../docs/rhino-grasshopper-adapter-contract.md).

## Troubleshooting

- **"Engine call failed … Is the server running"** — start step 1; check
  `http://localhost:5127/api/health` in a browser.
- **AI parse is slow or unavailable** — set `use_ai` to False for instant
  heuristic parsing, or install/log in the `claude`/`codex` CLI.
- **Plain geometry only** — units are meters in world XY; set your Rhino
  document units to meters for true scale.

## Testing without Rhino

`test_fp_generate.py` runs the component's full pipeline (brief → intent →
engine input → variant → geometry) against a live server using a small
Rhino-geometry shim: `python test_fp_generate.py` (add `FP_TEST_AI=1` to also
exercise the AI parse).
