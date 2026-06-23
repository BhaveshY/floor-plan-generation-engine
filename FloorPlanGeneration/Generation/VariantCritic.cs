using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FloorPlanGeneration.Geometry;
using FloorPlanGeneration.Schema;

namespace FloorPlanGeneration.Generation
{
    /// <summary>
    /// Gate levels for the Phase-5 critic. Defaults are the production thresholds; held in a
    /// type (the <see cref="PortfolioPriors"/> pattern) so they are explicit and testable.
    /// Not exposed via the input schema — the critic is gated by a single opt-in flag.
    /// </summary>
    public sealed class CritiqueThresholds
    {
        public CritiqueThresholds()
        {
            DaylightFloor = 1.0;
            EgressFloor = 1.0;
            MaxAspectRatio = 3.0;
            AdjacencyFloor = 0.5;
        }

        public double DaylightFloor { get; set; }
        public double EgressFloor { get; set; }
        public double MaxAspectRatio { get; set; }
        public double AdjacencyFloor { get; set; }

        public static CritiqueThresholds Default()
        {
            return new CritiqueThresholds();
        }
    }

    /// <summary>
    /// Soft quality gate (architectural-finetuning Phase 5): inspects each variant across four
    /// dimensions — daylight, egress, proportion, adjacency — and raises advisory findings for
    /// the weak ones, flagging those variants. Pure and deterministic; never changes pass/fail,
    /// ordering, or geometry. Consumed by the engine only when
    /// <see cref="GenerationSettings.CritiqueVariants"/> is set.
    /// </summary>
    public static class VariantCritic
    {
        private static readonly HashSet<string> HabitableRoomTypes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bedroom", "living" };

        public static QualityCritique Critique(
            IReadOnlyList<LayoutVariant> variants,
            PortfolioPriors priors,
            double tolerance,
            CritiqueThresholds thresholds)
        {
            QualityCritique critique = new QualityCritique();
            if (variants == null)
            {
                return critique;
            }

            foreach (LayoutVariant variant in variants)
            {
                VariantCritique assessment = new VariantCritique
                {
                    VariantId = variant.VariantId,
                    Passed = variant.Validation != null && variant.Validation.Passed
                };

                AddFinding(assessment, DaylightFinding(variant, thresholds));
                AddFinding(assessment, EgressFinding(variant, thresholds));
                AddFinding(assessment, ProportionFinding(variant, thresholds));
                AddFinding(assessment, AdjacencyFinding(variant, priors, tolerance, thresholds));

                critique.Variants.Add(assessment);
                if (assessment.Findings.Count > 0)
                {
                    critique.FlaggedVariantIds.Add(variant.VariantId);
                }
            }

            return critique;
        }

        private static void AddFinding(VariantCritique assessment, CritiqueFinding finding)
        {
            if (finding != null)
            {
                assessment.Findings.Add(finding);
            }
        }

        private static CritiqueFinding DaylightFinding(LayoutVariant variant, CritiqueThresholds thresholds)
        {
            List<RoomLayout> habitable = HabitableRooms(variant);
            if (habitable.Count == 0)
            {
                return null;
            }

            int daylit = habitable.Count(room => room.Daylight);
            double score = daylit / (double)habitable.Count;
            if (score >= thresholds.DaylightFloor)
            {
                return null;
            }

            int dark = habitable.Count - daylit;
            return Finding(
                "daylight",
                "warning",
                dark.ToString(CultureInfo.InvariantCulture) + " of " + habitable.Count.ToString(CultureInfo.InvariantCulture)
                    + " habitable rooms lack daylight.",
                score);
        }

        private static CritiqueFinding EgressFinding(LayoutVariant variant, CritiqueThresholds thresholds)
        {
            List<UnitLayout> units = variant.Units ?? new List<UnitLayout>();
            List<CorridorLayout> corridors = variant.Corridors ?? new List<CorridorLayout>();
            if (units.Count == 0 || corridors.Count == 0)
            {
                // No circulation to reach (e.g. single_dwelling has no corridor) -> not assessed.
                return null;
            }

            HashSet<string> corridorIds = new HashSet<string>(corridors.Select(corridor => corridor.Id), StringComparer.OrdinalIgnoreCase);
            List<DoorOpening> doors = variant.DoorsOpenings ?? new List<DoorOpening>();
            int connected = units.Count(unit => ReachesCorridor(unit, doors, corridorIds));
            double score = connected / (double)units.Count;
            if (score >= thresholds.EgressFloor)
            {
                return null;
            }

            int isolated = units.Count - connected;
            return Finding(
                "egress",
                "warning",
                isolated.ToString(CultureInfo.InvariantCulture) + " of " + units.Count.ToString(CultureInfo.InvariantCulture)
                    + " units lack a door to circulation.",
                score);
        }

        private static bool ReachesCorridor(UnitLayout unit, List<DoorOpening> doors, HashSet<string> corridorIds)
        {
            foreach (DoorOpening door in doors)
            {
                List<string> spaces = door.ConnectsSpaces;
                if (spaces == null)
                {
                    continue;
                }

                bool touchesUnit = spaces.Any(id => string.Equals(id, unit.Id, StringComparison.OrdinalIgnoreCase));
                bool touchesCorridor = spaces.Any(corridorIds.Contains);
                if (touchesUnit && touchesCorridor)
                {
                    return true;
                }
            }

            return false;
        }

        private static CritiqueFinding ProportionFinding(LayoutVariant variant, CritiqueThresholds thresholds)
        {
            List<RoomLayout> habitable = HabitableRooms(variant);
            if (habitable.Count == 0)
            {
                return null;
            }

            RoomLayout worst = null;
            double worstRatio = 0.0;
            int overLimit = 0;
            foreach (RoomLayout room in habitable)
            {
                double ratio = AspectRatio(room.Bounds);
                if (ratio > thresholds.MaxAspectRatio)
                {
                    overLimit++;
                }

                if (ratio > worstRatio)
                {
                    worstRatio = ratio;
                    worst = room;
                }
            }

            if (overLimit == 0 || worst == null)
            {
                return null;
            }

            return Finding(
                "proportion",
                "info",
                "Room " + worst.Id + " has aspect ratio " + worstRatio.ToString("0.0", CultureInfo.InvariantCulture)
                    + " (max " + thresholds.MaxAspectRatio.ToString("0.0", CultureInfo.InvariantCulture) + ").",
                worstRatio);
        }

        private static CritiqueFinding AdjacencyFinding(
            LayoutVariant variant,
            PortfolioPriors priors,
            double tolerance,
            CritiqueThresholds thresholds)
        {
            List<UnitLayout> units = variant.Units ?? new List<UnitLayout>();
            if (units.Count == 0)
            {
                return null;
            }

            double score = AdjacencyScorer.Score(units, priors, tolerance);
            if (score >= thresholds.AdjacencyFloor)
            {
                return null;
            }

            return Finding(
                "adjacency",
                "info",
                "Adjacency preference " + score.ToString("0.00", CultureInfo.InvariantCulture)
                    + " is below the target " + thresholds.AdjacencyFloor.ToString("0.00", CultureInfo.InvariantCulture) + ".",
                score);
        }

        private static List<RoomLayout> HabitableRooms(LayoutVariant variant)
        {
            List<RoomLayout> rooms = variant.Rooms ?? new List<RoomLayout>();
            return rooms.Where(room => room != null && HabitableRoomTypes.Contains(room.RoomType ?? string.Empty)).ToList();
        }

        private static double AspectRatio(Bounds2 bounds)
        {
            if (bounds == null)
            {
                return 1.0;
            }

            double longer = Math.Max(Math.Abs(bounds.Width), Math.Abs(bounds.Height));
            double shorter = Math.Min(Math.Abs(bounds.Width), Math.Abs(bounds.Height));
            if (shorter <= 1e-9)
            {
                // Degenerate room: report a large but finite ratio so it never serializes as Infinity.
                return longer <= 1e-9 ? 1.0 : 999.0;
            }

            return longer / shorter;
        }

        private static CritiqueFinding Finding(string dimension, string severity, string message, double score)
        {
            return new CritiqueFinding
            {
                Dimension = dimension,
                Severity = severity,
                Message = message,
                Score = Math.Round(score, 4)
            };
        }
    }
}
