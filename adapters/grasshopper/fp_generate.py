# EBA Floor Plan Generator - Grasshopper component
#
# Generates floor plans from a natural-language brief by calling the local
# engine server (run scripts/run-web.ps1 or .sh first) and returns the chosen
# variant as Rhino geometry, ready to preview or bake to the contract layers.
#
# Works in Rhino 7 (GHPython, IronPython 2.7) and Rhino 8 (GHPython or the
# Script component in Python 3). No external packages required.
#
# Component inputs (set "Type hint" and item access as noted):
#   brief    (str,  item)  - e.g. "a 2 bhk apartment with a big kitchen"
#   run      (bool, item)  - execute when True (wire a Button or Toggle)
#   bake     (bool, item)  - optional: bake the variant to Rhino layers
#   variant  (int,  item)  - optional: ranked variant index, default 0 (best)
#   seed     (int,  item)  - optional: layout seed, default derived from brief
#   variants (int,  item)  - optional: how many variants to generate, default 4
#   use_ai   (bool, item)  - optional: let Claude/Codex read the brief, default True
#   provider (str,  item)  - optional: "claude" or "codex" when both installed
#   base_url (str,  item)  - optional: engine URL, default http://localhost:5127
#
# Component outputs:
#   status      - one-line run report
#   rooms       - closed PolylineCurves, one per room
#   room_names  - room type per curve (Bathroom, Bedroom, ...)
#   room_areas  - area in m2 per curve
#   units       - closed PolylineCurves, one per unit
#   unit_names  - unit type per curve
#   walls       - LineCurves (wall centerlines)
#   doors       - Point3d door locations
#   corridors   - closed PolylineCurves (empty for single dwellings)
#   labels_text - label strings
#   labels_pts  - Point3d label anchors

import json
import re

try:  # Rhino 8 Script component / CPython 3
    from urllib.request import Request, urlopen
except ImportError:  # Rhino 7 GHPython / IronPython 2.7
    from urllib2 import Request, urlopen

try:  # Only present when running inside Rhino/Grasshopper
    import Rhino
    import Rhino.Geometry as rg
    import scriptcontext as sc
    IN_RHINO = True
except ImportError:
    Rhino = None
    rg = None
    sc = None
    IN_RHINO = False

DEFAULT_URL = "http://localhost:5127"

# Mirrors the web studio's dwelling presets (app.js dwellingPresets).
DWELLING_PRESETS = {
    0: {"width": 7.2, "depth": 5.6, "type": "studio", "minArea": 24, "maxArea": 45},
    1: {"width": 9.0, "depth": 6.8, "type": "one_bed", "minArea": 40, "maxArea": 70},
    2: {"width": 11.2, "depth": 8.4, "type": "two_bed", "minArea": 65, "maxArea": 110},
    3: {"width": 13.2, "depth": 9.6, "type": "three_bed", "minArea": 90, "maxArea": 150},
    4: {"width": 15.0, "depth": 10.5, "type": "three_bed", "minArea": 110, "maxArea": 180},
}

TEMPLATES = ("rectangular-core", "l-shaped-core", "moderately-irregular-core")


def http_json(url, payload=None, timeout=60):
    data = None
    request = Request(url)
    request.add_header("Accept", "application/json")
    if payload is not None:
        data = json.dumps(payload).encode("utf-8")
        request.add_header("Content-Type", "application/json")
    response = urlopen(request, data, timeout)
    body = response.read()
    if isinstance(body, bytes):
        body = body.decode("utf-8")
    return json.loads(body)


def normalize_brief(text):
    # Faithful port of the studio's normalizePromptText so the same brief
    # produces the same layout seed in Grasshopper and in the browser.
    t = " " + (text or "").lower() + " "
    t = re.sub(r"[,;:!?]", " ", t)
    t = re.sub(r"(\d)([a-z])", r"\1 \2", t)
    t = re.sub(r"([a-z])(\d)", r"\1 \2", t)
    t = re.sub(
        r"\b(bhk|bedroom|bed|br|rk)(apartments?|flats?|homes?|houses?|residences?|units?|plans?)\b",
        r"\1 \2",
        t,
    )
    t = re.sub(r"\bfloorplans?\b", "floor plan", t)
    return re.sub(r"\s+", " ", t)


def brief_seed(text):
    # FNV-1a, identical to the studio's promptSeed.
    value = 0x811C9DC5
    for ch in text:
        value ^= ord(ch)
        value = (value * 0x01000193) & 0xFFFFFFFF
    return value % 1000000 + 1


WORD_NUMBERS = {"one": 1, "two": 2, "three": 3, "four": 4, "single": 1}


def heuristic_intent(brief):
    # Offline fallback mirroring the studio's key patterns; the AI parse
    # (Claude/Codex via the server) is richer when available.
    t = normalize_brief(brief)
    intent = {}
    match = re.search(r"(\d{1,3}(?:\.\d+)?)\s*(?:m|metres?|meters?)?\s*(?:x|by)\s*(\d{1,3}(?:\.\d+)?)", t)
    if match and float(match.group(1)) >= 3 and float(match.group(2)) >= 3:
        intent["width"] = float(match.group(1))
        intent["depth"] = float(match.group(2))
    bed = re.search(r"\b(\d|one|two|three|four|single)\s*(?:bhk|bed|bedroom|br)s?\b", t)
    studio_like = bool(
        re.search(r"\b(?:1|one)\s*rk\b", t)
        or re.search(r"\bstudio\b", t)
        or re.search(r"\b(?:1|one)\s+room\s+kitchen\b", t)
    )
    if studio_like:
        intent["bedrooms"] = 0
    elif bed:
        token = bed.group(1)
        intent["bedrooms"] = int(token) if token.isdigit() else WORD_NUMBERS.get(token, 1)
    singular = re.search(
        r"\b(?:a|an|one|single|my|this|1)\s+(?:\d(?:\.\d+)?\s+)?"
        r"(?:(?:bhk|rk|bed(?:room)?|room\s+kitchen)\s+)?(?:apartment|flat|home|house|unit|dwelling)\b(?!s)",
        t,
    )
    if singular or re.search(r"\b(?:1|one)\s*rk\b", t):
        intent["dwelling"] = "single"
    if re.search(r"\bl[\s-]?shaped?\b", t):
        intent["template"] = "l-shaped-core"
    elif re.search(r"\b(?:irregular|stepped|articulated)\b", t):
        intent["template"] = "moderately-irregular-core"
    return intent


def ai_intent(base_url, brief, provider):
    payload = {"brief": brief}
    if provider:
        payload["provider"] = provider
    outcome = http_json(base_url + "/api/prompt/parse", payload, timeout=240)
    if not outcome.get("ok") or not outcome.get("intent"):
        return None, None
    return outcome["intent"], outcome.get("provider")


def clampv(value, lo, hi):
    return max(lo, min(hi, value))


def rect_points(x, y, width, depth):
    return [
        {"x": x, "y": y},
        {"x": x + width, "y": y},
        {"x": x + width, "y": y + depth},
        {"x": x, "y": y + depth},
    ]


def dwelling_input(intent):
    # Mirrors the studio's buildSingleDwellingInput.
    bedrooms = intent.get("bedrooms")
    bedrooms = int(clampv(bedrooms if isinstance(bedrooms, (int, float)) else 1, 0, 4))
    preset = DWELLING_PRESETS[bedrooms]
    width = clampv(float(intent.get("width") or preset["width"]), 4, 40)
    depth = clampv(float(intent.get("depth") or preset["depth"]), 4, 30)
    return {
        "project": {"id": "single-dwelling", "name": "Apartment Plan", "units": "m", "tolerance": 0.01, "seed": 1},
        "floorplate": {"outer": {"id": "floorplate-01", "points": rect_points(0, 0, width, depth)}, "holes": []},
        "fixedElements": [],
        "access": {
            "entryPoints": [{"x": round(width / 2.0, 2), "y": 0}],
            "verticalCoreAccess": [],
            "corridorStartPoints": [],
            "corridorEndPoints": [],
            "corridorCenterlines": [],
        },
        "program": {
            "targetUnitTypes": [{
                "type": preset["type"],
                "minArea": preset["minArea"],
                "maxArea": preset["maxArea"],
                "targetCount": 1,
                "targetRatio": 1,
                "weight": 1,
            }],
            "roomTypes": [
                {"type": "bedroom", "minArea": 10, "minWidth": 2.7, "minDepth": 2.7, "requiresDaylight": True},
                {"type": "living", "minArea": 14, "minWidth": 3, "minDepth": 3, "requiresDaylight": True},
                {"type": "bathroom", "minArea": 3.5, "minWidth": 1.6, "minDepth": 2, "isWet": True},
            ],
        },
        "rules": {
            "minCorridorWidth": 1.2,
            "minRoomWidth": 2.0,
            "minRoomDepth": 2.0,
            "doorWidth": 0.9,
            "wetRoomAdjacencyPreferred": True,
            "requireDaylightForBedrooms": True,
            "requireDaylightForLiving": True,
            "minUnitArea": 16,
        },
        "generationSettings": {
            "variantCount": 4,
            "timeLimitMilliseconds": 1000,
            "strictness": "balanced",
            "weightedVariation": True,
            "layoutMode": "single_dwelling",
            "scoringWeights": {
                "efficiency": 0.3,
                "netGrossRatio": 0.2,
                "unitMixMatch": 0.25,
                "unitQuality": 0.15,
                "daylight": 0.1,
            },
        },
    }


def scale_points(points, bounds, sx, sy):
    for point in points or []:
        point["x"] = round((float(point["x"]) - bounds[0]) * sx, 4)
        point["y"] = round((float(point["y"]) - bounds[1]) * sy, 4)


def building_input(base_url, intent):
    template = intent.get("template")
    if template not in TEMPLATES:
        template = "rectangular-core"
    sample = http_json(base_url + "/api/samples/" + template, timeout=30)

    width = intent.get("width")
    depth = intent.get("depth")
    if width or depth:
        # The plate and everything anchored to it ride one affine map, exactly
        # like the studio's rescale (a stranded core is a hard engine error).
        outer = sample["floorplate"]["outer"]["points"]
        xs = [float(p["x"]) for p in outer]
        ys = [float(p["y"]) for p in outer]
        bounds = (min(xs), min(ys), max(xs), max(ys))
        sx = (clampv(float(width), 8, 300) / (bounds[2] - bounds[0])) if width else 1.0
        sy = (clampv(float(depth), 8, 300) / (bounds[3] - bounds[1])) if depth else 1.0
        scale_points(outer, bounds, sx, sy)
        for hole in sample["floorplate"].get("holes") or []:
            scale_points(hole.get("points"), bounds, sx, sy)
        for fixed in sample.get("fixedElements") or []:
            scale_points((fixed.get("polygon") or {}).get("points"), bounds, sx, sy)
        access = sample.get("access") or {}
        for key in ("entryPoints", "verticalCoreAccess", "corridorStartPoints", "corridorEndPoints"):
            scale_points(access.get(key), bounds, sx, sy)

    mix = intent.get("mix") or {}
    if mix:
        for target in (sample.get("program") or {}).get("targetUnitTypes") or []:
            target["targetRatio"] = float(mix.get(target.get("type"), 0)) / 100.0
            target["targetCount"] = 0
    rules = sample.get("rules") or {}
    if intent.get("corridor"):
        rules["minCorridorWidth"] = clampv(float(intent["corridor"]), 0.9, 12)
    if intent.get("minUnit"):
        rules["minUnitArea"] = clampv(float(intent["minUnit"]), 10, 400)
    return sample


def to_polyline(points):
    pts = [rg.Point3d(float(p["x"]), float(p["y"]), 0.0) for p in points or []]
    if len(pts) >= 3 and pts[0].DistanceTo(pts[-1]) > 1e-9:
        pts.append(rg.Point3d(pts[0]))
    return rg.PolylineCurve(pts)


def pretty(name):
    return " ".join(part.capitalize() for part in str(name or "").replace("_", " ").split())


def extract_variant(output, index):
    ranked = output.get("variants") or []
    if not ranked:
        return None
    return ranked[int(clampv(index, 0, len(ranked) - 1))]


def bake_variant(variant, layers):
    doc = Rhino.RhinoDoc.ActiveDoc
    layer_index = {}

    def layer_for(key, fallback):
        name = (layers or {}).get(key) or fallback
        if name not in layer_index:
            existing = doc.Layers.FindName(name)
            if existing is None:
                layer = Rhino.DocObjects.Layer()
                layer.Name = name
                layer_index[name] = doc.Layers.Add(layer)
            else:
                layer_index[name] = existing.Index
        return layer_index[name]

    def attributes(key, fallback, name, external_id):
        attr = Rhino.DocObjects.ObjectAttributes()
        attr.LayerIndex = layer_for(key, fallback)
        attr.Name = name or ""
        if external_id:
            attr.SetUserString("externalId", external_id)
        return attr

    baked = 0
    for unit in variant.get("units") or []:
        curve = to_polyline((unit.get("polygon") or {}).get("points"))
        doc.Objects.AddCurve(curve, attributes("units", "FP::Generated::Units", pretty(unit.get("type")), unit.get("externalId")))
        baked += 1
    for room in variant.get("rooms") or []:
        curve = to_polyline((room.get("polygon") or {}).get("points"))
        doc.Objects.AddCurve(curve, attributes("rooms", "FP::Generated::Rooms", pretty(room.get("roomType")), room.get("externalId")))
        baked += 1
    for corridor in variant.get("corridors") or []:
        curve = to_polyline((corridor.get("polygon") or {}).get("points"))
        doc.Objects.AddCurve(curve, attributes("corridors", "FP::Generated::Corridors", "Corridor", corridor.get("externalId")))
        baked += 1
    for wall in variant.get("walls") or []:
        line = wall.get("centerline") or {}
        start = line.get("start") or {}
        end = line.get("end") or {}
        curve = rg.LineCurve(
            rg.Point3d(float(start.get("x", 0)), float(start.get("y", 0)), 0.0),
            rg.Point3d(float(end.get("x", 0)), float(end.get("y", 0)), 0.0),
        )
        doc.Objects.AddCurve(curve, attributes("walls", "FP::Generated::Walls", "", wall.get("externalId")))
        baked += 1
    for door in variant.get("doorsOpenings") or []:
        location = door.get("location") or {}
        point = rg.Point3d(float(location.get("x", 0)), float(location.get("y", 0)), 0.0)
        doc.Objects.AddPoint(point, attributes("doors", "FP::Generated::Doors", "Door", door.get("externalId")))
        baked += 1
    doc.Views.Redraw()
    return baked


def generate(brief, base_url=None, seed=None, variants=None, use_ai=True, provider=None):
    base = (base_url or DEFAULT_URL).rstrip("/")
    text = (brief or "").strip()
    intent = heuristic_intent(text)
    source = "heuristic parser"
    if use_ai and text:
        try:
            parsed, used_provider = ai_intent(base, text, provider)
            if parsed:
                merged = dict(intent)
                for key, value in parsed.items():
                    if value not in (None, "", []):
                        merged[key] = value
                intent = merged
                source = "brief read by " + pretty(used_provider or "ai")
        except Exception:
            source = "heuristic parser (AI unavailable)"

    if intent.get("dwelling") == "single":
        engine_input = dwelling_input(intent)
    else:
        engine_input = building_input(base, intent)

    request = {
        "input": engine_input,
        "seed": int(seed) if seed else brief_seed(normalize_brief(text)),
        "variants": int(clampv(variants or 4, 1, 20)),
    }
    response = http_json(base + "/api/generate", request, timeout=120)
    return response, request["seed"], source


def run_component(brief, run, bake=False, variant=0, seed=None, variants=None, use_ai=True, provider=None, base_url=None):
    empty = ("", [], [], [], [], [], [], [], [], [], [])
    if not run:
        return ("Set run = True to generate (start the server with scripts/run-web first).",) + empty[1:]
    try:
        response, used_seed, source = generate(brief, base_url, seed, variants, use_ai, provider)
    except Exception as error:  # noqa: BLE001 - surface anything to the canvas
        return ("Engine call failed: {0}. Is the server running at {1}?".format(error, base_url or DEFAULT_URL),) + empty[1:]

    output = response.get("output") or {}
    picked = extract_variant(output, variant or 0)
    if picked is None:
        return ("No variants generated ({0}). Check diagnostics in the web studio.".format(response.get("status")),) + empty[1:]

    rooms, room_names, room_areas = [], [], []
    for room in picked.get("rooms") or []:
        rooms.append(to_polyline((room.get("polygon") or {}).get("points")))
        room_names.append(pretty(room.get("roomType")))
        room_areas.append(float(room.get("area") or 0))
    units, unit_names = [], []
    for unit in picked.get("units") or []:
        units.append(to_polyline((unit.get("polygon") or {}).get("points")))
        unit_names.append(pretty(unit.get("type")))
    walls = []
    for wall in picked.get("walls") or []:
        line = wall.get("centerline") or {}
        start, end = line.get("start") or {}, line.get("end") or {}
        walls.append(rg.LineCurve(
            rg.Point3d(float(start.get("x", 0)), float(start.get("y", 0)), 0.0),
            rg.Point3d(float(end.get("x", 0)), float(end.get("y", 0)), 0.0)))
    doors = []
    for door in picked.get("doorsOpenings") or []:
        location = door.get("location") or {}
        doors.append(rg.Point3d(float(location.get("x", 0)), float(location.get("y", 0)), 0.0))
    corridors = [to_polyline((c.get("polygon") or {}).get("points")) for c in picked.get("corridors") or []]
    labels_text, labels_pts = [], []
    for label in picked.get("labels") or []:
        location = label.get("location") or {}
        labels_text.append(label.get("text") or "")
        labels_pts.append(rg.Point3d(float(location.get("x", 0)), float(location.get("y", 0)), 0.0))

    baked_note = ""
    if bake and IN_RHINO:
        ghdoc = sc.doc
        sc.doc = Rhino.RhinoDoc.ActiveDoc
        try:
            count = bake_variant(picked, (output.get("metadata") or {}).get("layers"))
            baked_note = " - baked {0} objects".format(count)
        finally:
            sc.doc = ghdoc

    score = ((picked.get("metrics") or {}).get("score"))
    status = "{0} - {1}/{2} valid - showing {3} (score {4}) - seed {5} - {6}{7}".format(
        pretty(response.get("status")),
        response.get("validVariantCount"),
        response.get("variantCount"),
        picked.get("variantId"),
        score,
        used_seed,
        source,
        baked_note,
    )
    return (status, rooms, room_names, room_areas, units, unit_names, walls, doors, corridors, labels_text, labels_pts)


if IN_RHINO:
    # Grasshopper executes this file top-to-bottom with inputs as globals.
    _g = globals()
    (status, rooms, room_names, room_areas, units, unit_names,
     walls, doors, corridors, labels_text, labels_pts) = run_component(
        _g.get("brief"), _g.get("run"),
        bake=_g.get("bake") or False,
        variant=_g.get("variant") or 0,
        seed=_g.get("seed"),
        variants=_g.get("variants"),
        use_ai=True if _g.get("use_ai") is None else bool(_g.get("use_ai")),
        provider=_g.get("provider"),
        base_url=_g.get("base_url"),
    )
