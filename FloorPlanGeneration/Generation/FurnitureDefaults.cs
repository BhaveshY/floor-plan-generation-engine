using System;
using System.Collections.Generic;
using FloorPlanGeneration.Schema;

namespace FloorPlanGeneration.Generation
{
    /// <summary>
    /// Furniture-derived per-room-type minimum dimensions for the German
    /// residential context (architectural-finetuning Phase 1). The figures are
    /// the adversarially-verified clear minimums to seat the essential furniture
    /// set plus circulation: Neufert "Bauentwurfslehre", DIN 18040-2 (barrier-free
    /// dwellings, used as the clearance basis rather than the wheelchair target),
    /// VDI 6000 (sanitary/utility) and typical Landesbauordnung habitable-room
    /// sizes. Dimensions are in metres; <c>MaxAspect</c> is the largest long:short
    /// proportion before the room degenerates into a leftover slot.
    ///
    /// This is data the caller opts into — the engine only applies it when
    /// <see cref="RuleSet.ApplyFurnitureMinimums"/> is set, and a caller may
    /// override any single type through <c>Program.RoomTypes</c>.
    /// </summary>
    public static class FurnitureDefaults
    {
        public static List<RoomTypeRule> StandardTable()
        {
            return new List<RoomTypeRule>
            {
                Rule("bedroom", 3.0, 3.6, 11.0, 1.7, daylight: true, wet: false),
                Rule("living", 3.4, 4.2, 16.0, 1.8, daylight: true, wet: false),
                Rule("living_sleeping", 3.4, 6.0, 22.0, 2.2, daylight: true, wet: false),
                Rule("dining", 2.6, 3.0, 8.0, 1.6, daylight: true, wet: false),
                Rule("study", 2.4, 3.3, 8.0, 2.0, daylight: true, wet: false),
                Rule("kitchen", 1.8, 3.0, 6.0, 2.6, daylight: false, wet: true),
                Rule("bathroom", 1.6, 2.1, 3.4, 2.0, daylight: false, wet: true),
                Rule("utility", 1.4, 2.0, 4.0, 2.0, daylight: false, wet: true),
                Rule("balcony", 1.4, 1.4, 3.0, 2.5, daylight: false, wet: false),
                Rule("foyer", 1.2, 1.5, 1.8, 3.0, daylight: false, wet: false),
                Rule("pooja", 0.9, 1.2, 1.1, 1.8, daylight: false, wet: false),
                Rule("store", 0.8, 1.25, 1.0, 2.2, daylight: false, wet: false),
            };
        }

        /// <summary>
        /// Builds a case-insensitive room-type to minimum-width lookup from the
        /// standard table, overlaying any per-type override supplied through the
        /// program brief (<c>Program.RoomTypes</c> with a positive MinWidth).
        /// </summary>
        public static Dictionary<string, double> MinWidthByType(ProgramBrief program)
        {
            Dictionary<string, double> lookup = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (RoomTypeRule rule in StandardTable())
            {
                lookup[rule.Type] = rule.MinWidth;
            }

            if (program != null && program.RoomTypes != null)
            {
                foreach (RoomTypeRule rule in program.RoomTypes)
                {
                    if (!string.IsNullOrWhiteSpace(rule.Type) && rule.MinWidth > 0.0)
                    {
                        lookup[rule.Type] = rule.MinWidth;
                    }
                }
            }

            return lookup;
        }

        /// <summary>
        /// Builds a case-insensitive room-type to maximum-aspect (long:short) lookup
        /// from the standard table, overlaying any per-type override supplied through
        /// the program brief (<c>Program.RoomTypes</c> with a positive MaxAspect). A
        /// type maps to 0 when it has no cap, meaning "unconstrained".
        /// </summary>
        public static Dictionary<string, double> MaxAspectByType(ProgramBrief program)
        {
            Dictionary<string, double> lookup = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (RoomTypeRule rule in StandardTable())
            {
                lookup[rule.Type] = rule.MaxAspect;
            }

            if (program != null && program.RoomTypes != null)
            {
                foreach (RoomTypeRule rule in program.RoomTypes)
                {
                    if (!string.IsNullOrWhiteSpace(rule.Type) && rule.MaxAspect > 0.0)
                    {
                        lookup[rule.Type] = rule.MaxAspect;
                    }
                }
            }

            return lookup;
        }

        private static RoomTypeRule Rule(
            string type, double minWidth, double minDepth, double minArea,
            double maxAspect, bool daylight, bool wet)
        {
            return new RoomTypeRule
            {
                Type = type,
                MinWidth = minWidth,
                MinDepth = minDepth,
                MinArea = minArea,
                MaxAspect = maxAspect,
                RequiresDaylight = daylight,
                IsWet = wet,
            };
        }
    }
}
