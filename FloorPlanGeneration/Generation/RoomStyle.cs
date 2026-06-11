using System;

namespace FloorPlanGeneration.Generation
{
    /// <summary>
    /// Per-variant interior styling drawn once from the variant seed so every unit
    /// in a variant follows one coherent design language while different seeds
    /// produce visibly different interiors.
    /// </summary>
    internal sealed class RoomStyle
    {
        public double WetFraction { get; private set; }

        public double WetSplitFraction { get; private set; }

        public double StudioBathFraction { get; private set; }

        public double OneBedFraction { get; private set; }

        public double TwoBedFraction { get; private set; }

        public bool MirrorEvenUnits { get; private set; }

        public static RoomStyle FromRandom(SeededRandom random)
        {
            return new RoomStyle
            {
                WetFraction = 0.32 + (random.NextDouble() * 0.08),
                WetSplitFraction = 0.30 + (random.NextDouble() * 0.10),
                StudioBathFraction = 0.30 + (random.NextDouble() * 0.12),
                OneBedFraction = 0.38 + (random.NextDouble() * 0.10),
                TwoBedFraction = 0.26 + (random.NextDouble() * 0.07),
                MirrorEvenUnits = random.NextDouble() < 0.5
            };
        }

        public static RoomStyle Default()
        {
            return new RoomStyle
            {
                WetFraction = 0.35,
                WetSplitFraction = 0.32,
                StudioBathFraction = 0.36,
                OneBedFraction = 0.42,
                TwoBedFraction = 0.29,
                MirrorEvenUnits = false
            };
        }

        /// <summary>
        /// Alternates handedness along the corridor so neighbouring units pair up
        /// with back-to-back wet rooms, the way real double-loaded plans do.
        /// </summary>
        public bool MirrorUnit(int unitIndex)
        {
            return ((unitIndex % 2) == 0) == MirrorEvenUnits;
        }
    }
}
