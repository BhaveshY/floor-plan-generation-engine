# Plain-Python harness for the Grasshopper component (no Rhino required).
# Substitutes a minimal Rhino.Geometry shim and exercises the full pipeline
# against a running engine server: brief -> intent -> engine input -> variant
# -> geometry extraction. Run with the server up:  python test_fp_generate.py
# Set FP_TEST_AI=1 to also exercise the Claude/Codex parse path (slow).

import math
import os
import sys


class Point3d(object):
    def __init__(self, x, y=None, z=None):
        if y is None:  # copy constructor, mirroring Rhino's Point3d(Point3d)
            x, y, z = x.X, x.Y, x.Z
        self.X, self.Y, self.Z = float(x), float(y), float(z or 0.0)

    def DistanceTo(self, other):
        return math.sqrt((self.X - other.X) ** 2 + (self.Y - other.Y) ** 2 + (self.Z - other.Z) ** 2)


class PolylineCurve(object):
    def __init__(self, points):
        self.points = list(points)


class LineCurve(object):
    def __init__(self, start, end):
        self.start, self.end = start, end


class _RG(object):
    Point3d = Point3d
    PolylineCurve = PolylineCurve
    LineCurve = LineCurve


import fp_generate  # noqa: E402

fp_generate.rg = _RG


def check(condition, message):
    if not condition:
        raise AssertionError(message)
    print("ok - " + message)


def closed(curve):
    return len(curve.points) >= 4 and curve.points[0].DistanceTo(curve.points[-1]) < 1e-9


def run(name, expect_corridors, **kwargs):
    print("\n== " + name)
    result = fp_generate.run_component(**kwargs)
    (status, rooms, room_names, room_areas, units, unit_names,
     walls, doors, corridors, labels_text, labels_pts) = result
    print("   " + status)
    check("Succeeded" in status or "Partial" in status, "engine run succeeded")
    check(len(rooms) > 0 and len(rooms) == len(room_names) == len(room_areas), "rooms with names and areas")
    check(all(closed(c) for c in rooms), "room polylines are closed")
    check(len(units) > 0 and all(closed(c) for c in units), "unit polylines are closed")
    check(len(walls) > 0, "walls present")
    check(len(doors) > 0, "doors present")
    if expect_corridors:
        check(len(corridors) > 0, "corridors present for building floor")
    else:
        check(len(corridors) == 0, "no corridors for a single dwelling")
    check(len(labels_text) == len(labels_pts), "labels aligned")
    return result


def main():
    base_url = os.environ.get("FP_TEST_URL", "http://localhost:5127")

    # Heuristic intent sanity (offline, no server needed).
    intent = fp_generate.heuristic_intent("a 2 bhk apartment")
    check(intent.get("dwelling") == "single" and intent.get("bedrooms") == 2, "heuristic: '2 bhk apartment' is a 2-bed dwelling")
    intent = fp_generate.heuristic_intent("a 1 room kitchen apartment 7 x 5")
    check(intent.get("dwelling") == "single" and intent.get("width") == 7.0, "heuristic: dims parse for dwellings")
    intent = fp_generate.heuristic_intent("residential floor with studios and two bed apartments")
    check(intent.get("dwelling") != "single", "heuristic: plural brief stays a building")

    run("dwelling (heuristic)", expect_corridors=False,
        brief="a 2 bhk apartment", run=True, use_ai=False, base_url=base_url)
    run("building with dims (heuristic)", expect_corridors=True,
        brief="residential floor with studios 36 x 20", run=True, use_ai=False, base_url=base_url)
    run("building L-shaped (heuristic)", expect_corridors=True,
        brief="l-shaped residential floor with apartments", run=True, use_ai=False, base_url=base_url)

    if os.environ.get("FP_TEST_AI") == "1":
        run("dwelling (AI parse)", expect_corridors=False,
            brief="a small one bedroom flat for a single person", run=True, use_ai=True, base_url=base_url)

    print("\nall checks passed")


if __name__ == "__main__":
    sys.exit(main())
