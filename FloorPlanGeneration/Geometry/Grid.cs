using System;
using System.Collections.Generic;

namespace FloorPlanGeneration.Geometry
{
    /// <summary>
    /// Planning-grid snapping. A module of 0 (or less) disables snapping, so the
    /// historic free-proportion layouts stay byte-identical until a grid is opted
    /// in. Snapping is always best-effort: a snap that would push a partition
    /// outside its usable span is discarded, so watertight tiling and minimum room
    /// sizes are never violated.
    /// </summary>
    public static class Grid
    {
        private static bool Disabled(double module)
        {
            return module <= 0.0 || double.IsNaN(module) || double.IsInfinity(module);
        }

        /// <summary>Rounds a length/offset to the nearest grid module (no-op when module &lt;= 0).</summary>
        public static double Snap(double value, double module)
        {
            if (Disabled(module))
            {
                return value;
            }

            return Math.Round(value / module, MidpointRounding.AwayFromZero) * module;
        }

        /// <summary>
        /// Snaps a value to the grid (anchored at <paramref name="origin"/>) but only
        /// keeps the snapped result when it stays within [<paramref name="min"/>,
        /// <paramref name="max"/>]; otherwise the original value is returned. Used for
        /// a single band split where both sides have their own depth requirement.
        /// </summary>
        public static double SnapWithin(double value, double min, double max, double origin, double module)
        {
            if (Disabled(module))
            {
                return value;
            }

            double snapped = origin + Snap(value - origin, module);
            return snapped >= min && snapped <= max ? snapped : value;
        }

        /// <summary>
        /// Turns a left-to-right run of segment <paramref name="widths"/> into cumulative
        /// boundary coordinates over [<paramref name="start"/>, start + sum(widths)], with
        /// every interior boundary snapped to the module (anchored at start) where doing so
        /// keeps both neighbouring segments &gt;= <paramref name="minSegment"/>. The first and
        /// last boundaries are never moved, so the final segment absorbs the snapping
        /// remainder and the run still tiles its span exactly.
        /// </summary>
        public static double[] SnapBoundaries(double start, IReadOnlyList<double> widths, double minSegment, double module)
        {
            int count = widths.Count;
            double[] bounds = new double[count + 1];
            bounds[0] = start;
            double cursor = start;
            for (int i = 0; i < count; i++)
            {
                cursor += widths[i];
                bounds[i + 1] = cursor;
            }

            if (Disabled(module) || count < 2)
            {
                return bounds;
            }

            double end = bounds[count];
            for (int i = 1; i < count; i++)
            {
                double candidate = start + Snap(bounds[i] - start, module);
                // Guard against the already-finalised previous boundary and the raw next
                // one; when the next boundary snaps it re-checks against this finalised one,
                // so every resulting segment ends up >= minSegment.
                if (candidate - bounds[i - 1] >= minSegment && bounds[i + 1] - candidate >= minSegment &&
                    candidate > bounds[i - 1] && candidate < end)
                {
                    bounds[i] = candidate;
                }
            }

            return bounds;
        }
    }
}
