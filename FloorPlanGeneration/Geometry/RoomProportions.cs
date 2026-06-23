using System;
using System.Collections.Generic;

namespace FloorPlanGeneration.Geometry
{
    /// <summary>
    /// Furniture-derived room proportioning (architectural-finetuning Phase 1).
    /// Best-effort, like the planning <see cref="Grid"/>: it never moves the band
    /// edges (so watertight tiling holds) and never pushes a room below its own
    /// minimum. Default behaviour is a no-op so opted-out plans stay byte-identical.
    /// </summary>
    public static class RoomProportions
    {
        /// <summary>
        /// Grows each segment up to its per-index minimum by taking width from
        /// segments that sit above their own minimum, keeping the total sum
        /// unchanged. When the band is too small to satisfy every minimum the
        /// available slack is shared across the deficits in proportion to each
        /// deficit. Segments with a non-positive minimum are frozen (neither grow
        /// nor donate). Returns a new array; the input is left unchanged.
        /// </summary>
        public static double[] GrowToMinimums(IReadOnlyList<double> widths, IReadOnlyList<double> minWidths)
        {
            int n = widths.Count;
            double[] result = new double[n];
            double need = 0.0;
            double avail = 0.0;
            for (int i = 0; i < n; i++)
            {
                result[i] = widths[i];
                double min = i < minWidths.Count ? minWidths[i] : 0.0;
                if (min <= 0.0)
                {
                    continue;
                }

                if (widths[i] < min)
                {
                    need += min - widths[i];
                }
                else
                {
                    avail += widths[i] - min;
                }
            }

            if (need <= 1e-9 || avail <= 1e-9)
            {
                // No deficits to fill, or no room above its minimum to donate from:
                // best-effort leaves the band untouched (sum and edges preserved).
                return result;
            }

            // Scale <= 1: when the band cannot satisfy every minimum, fill the
            // deficits proportionally to how much each one needs. Donors give up
            // width in proportion to their slack, so none drops below its own
            // minimum and the total taken exactly equals the total given.
            double scale = avail < need ? avail / need : 1.0;
            double takeRatio = (need * scale) / avail;
            for (int i = 0; i < n; i++)
            {
                double min = i < minWidths.Count ? minWidths[i] : 0.0;
                if (min <= 0.0)
                {
                    continue;
                }

                if (widths[i] < min)
                {
                    result[i] = widths[i] + (min - widths[i]) * scale;
                }
                else if (widths[i] > min)
                {
                    result[i] = widths[i] - (widths[i] - min) * takeRatio;
                }
            }

            return result;
        }

        /// <summary>
        /// Mirror of <see cref="GrowToMinimums"/> for the upper bound: shrinks each
        /// segment down to its per-index maximum (e.g. an aspect-ratio cap) by handing
        /// the excess width to segments that still sit below their own maximum, keeping
        /// the total sum unchanged. When the band cannot absorb all the excess the
        /// shrink is shared proportionally (best-effort). Segments with a non-positive
        /// maximum are treated as unconstrained and frozen (they neither shrink nor
        /// absorb). Returns a new array; the input is left unchanged.
        /// </summary>
        public static double[] ShrinkToMaximums(IReadOnlyList<double> widths, IReadOnlyList<double> maxWidths)
        {
            int n = widths.Count;
            double[] result = new double[n];
            double excess = 0.0;
            double headroom = 0.0;
            for (int i = 0; i < n; i++)
            {
                result[i] = widths[i];
                double max = i < maxWidths.Count ? maxWidths[i] : 0.0;
                if (max <= 0.0)
                {
                    continue;
                }

                if (widths[i] > max)
                {
                    excess += widths[i] - max;
                }
                else
                {
                    headroom += max - widths[i];
                }
            }

            if (excess <= 1e-9 || headroom <= 1e-9)
            {
                // Nothing over its cap, or nowhere with headroom to receive the excess:
                // best-effort leaves the band untouched (sum and edges preserved).
                return result;
            }

            // Scale <= 1: when the band cannot absorb every overflow, shrink the
            // over-cap rooms proportionally to how far each exceeds its cap. Receivers
            // take width in proportion to their headroom, so none rises above its own
            // maximum and the total taken exactly equals the total given.
            double scale = headroom < excess ? headroom / excess : 1.0;
            double giveRatio = (excess * scale) / headroom;
            for (int i = 0; i < n; i++)
            {
                double max = i < maxWidths.Count ? maxWidths[i] : 0.0;
                if (max <= 0.0)
                {
                    continue;
                }

                if (widths[i] > max)
                {
                    result[i] = widths[i] - (widths[i] - max) * scale;
                }
                else if (widths[i] < max)
                {
                    result[i] = widths[i] + (max - widths[i]) * giveRatio;
                }
            }

            return result;
        }

        /// <summary>
        /// Two-sided best-effort proportioning: first grows every segment up to its
        /// minimum width, then caps any segment above its maximum width, each pass
        /// sum-preserving so the band still tiles its span exactly. Running grow before
        /// cap keeps the minimum satisfied — the cap pass only removes width from
        /// over-max rooms (which stay at or above their max >= min) and adds width to
        /// the rest, so it never reintroduces a deficit. A non-positive bound means
        /// "unconstrained" on that side. Returns a new array; the input is unchanged.
        /// </summary>
        public static double[] ConstrainToBounds(
            IReadOnlyList<double> widths, IReadOnlyList<double> minWidths, IReadOnlyList<double> maxWidths)
        {
            double[] grown = GrowToMinimums(widths, minWidths);
            return ShrinkToMaximums(grown, maxWidths);
        }

        /// <summary>
        /// Nudges each segment toward its target share of the band total (the sum of
        /// <paramref name="widths"/>) by <paramref name="strength"/> in [0,1], then
        /// projects the result back so the sum is preserved exactly and no segment
        /// leaves [min,max] (a non-positive bound is unconstrained on that side). The
        /// raw nudge is sum-neutral by construction — only clamping to bounds creates a
        /// residual, which is redistributed onto the segments that still have room in
        /// the needed direction. Deterministic and best-effort: when the bounds cannot
        /// absorb the residual the band stays as close as the bounds allow.
        /// (architectural-finetuning Phase 3 — owned-data proportion priors.)
        ///
        /// Shares are normalised, so they need not sum to one; a non-positive share is
        /// treated as zero. With no positive share, a zero band total, or zero strength
        /// the input is returned unchanged. Returns a new array; the input is unchanged.
        /// </summary>
        public static double[] PullToTargets(
            IReadOnlyList<double> widths,
            IReadOnlyList<double> targetShares,
            IReadOnlyList<double> minWidths,
            IReadOnlyList<double> maxWidths,
            double strength)
        {
            int n = widths.Count;
            double[] result = new double[n];
            double total = 0.0;
            for (int i = 0; i < n; i++)
            {
                result[i] = widths[i];
                total += widths[i];
            }

            double shareSum = 0.0;
            for (int i = 0; i < n; i++)
            {
                double share = i < targetShares.Count ? targetShares[i] : 0.0;
                if (share > 0.0)
                {
                    shareSum += share;
                }
            }

            if (total <= 1e-9 || shareSum <= 1e-9 || strength <= 0.0)
            {
                return result;
            }

            // Sum-neutral nudge: sum(targets) == total and sum(widths) == total, so the
            // deltas sum to zero before any clamping touches them.
            for (int i = 0; i < n; i++)
            {
                double share = i < targetShares.Count && targetShares[i] > 0.0 ? targetShares[i] : 0.0;
                double target = total * share / shareSum;
                result[i] = widths[i] + (strength * (target - widths[i]));
            }

            // Restore the sum while honouring [min,max]: clamp, measure the residual the
            // clamp introduced, and hand it to the segments that can still move that way.
            for (int pass = 0; pass < 64; pass++)
            {
                double sum = 0.0;
                for (int i = 0; i < n; i++)
                {
                    double lo = LowerBound(minWidths, i);
                    double hi = UpperBound(maxWidths, i);
                    if (result[i] < lo)
                    {
                        result[i] = lo;
                    }
                    else if (result[i] > hi)
                    {
                        result[i] = hi;
                    }

                    sum += result[i];
                }

                double residual = total - sum;
                if (Math.Abs(residual) <= 1e-12)
                {
                    break;
                }

                double[] capacity = new double[n];
                double capacityTotal = 0.0;
                for (int i = 0; i < n; i++)
                {
                    double lo = LowerBound(minWidths, i);
                    double hi = UpperBound(maxWidths, i);
                    double room = residual > 0.0
                        ? (double.IsPositiveInfinity(hi) ? result[i] : hi - result[i])
                        : (double.IsNegativeInfinity(lo) ? result[i] : result[i] - lo);
                    room = Math.Max(0.0, room);
                    capacity[i] = room;
                    capacityTotal += room;
                }

                if (capacityTotal <= 1e-12)
                {
                    break;
                }

                for (int i = 0; i < n; i++)
                {
                    result[i] += residual * (capacity[i] / capacityTotal);
                }
            }

            return result;
        }

        private static double LowerBound(IReadOnlyList<double> minWidths, int i)
        {
            return minWidths != null && i < minWidths.Count && minWidths[i] > 0.0 ? minWidths[i] : double.NegativeInfinity;
        }

        private static double UpperBound(IReadOnlyList<double> maxWidths, int i)
        {
            return maxWidths != null && i < maxWidths.Count && maxWidths[i] > 0.0 ? maxWidths[i] : double.PositiveInfinity;
        }
    }
}
