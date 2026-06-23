using System;
using System.Collections.Generic;

namespace FloorPlanGeneration.Generation
{
    /// <summary>
    /// Owned-data proportion + adjacency priors (architectural-finetuning Phase 3).
    /// A compact statistical summary the engine is gently pulled toward — priors, not
    /// a learned model — applied only when <see cref="Schema.RuleSet.UsePortfolioPriors"/>
    /// is set, so opted-out plans stay byte-identical.
    ///
    /// The <see cref="Default"/> seed values are distilled from published German
    /// residential norms (Neufert, DIN 18040-2, VDI 6000) and are a PLACEHOLDER for
    /// EBA-portfolio statistics — they are not measured from EBA projects. When real
    /// project data becomes available an offline ingestion pass (unit areas by type,
    /// facade-band width shares by type, room-adjacency co-occurrence) regenerates
    /// these defaults; the consuming code does not change. Same spirit as
    /// <see cref="FurnitureDefaults"/>, which carries norm-derived room minimums as a
    /// typed table rather than a parsed file (keeps the engine deterministic and I/O-free).
    /// </summary>
    public sealed class PortfolioPriors
    {
        public const int CurrentVersion = 1;

        private readonly Dictionary<string, double> _unitAreaMeans;
        private readonly Dictionary<string, IReadOnlyList<FacadeShare>> _facadeShares;
        private readonly Dictionary<string, double> _adjacency;
        private readonly double _adjacencyDefault;

        public PortfolioPriors(
            int version,
            Dictionary<string, double> unitAreaMeans,
            Dictionary<string, IReadOnlyList<FacadeShare>> facadeShares,
            Dictionary<string, double> adjacency,
            double adjacencyDefault)
        {
            Version = version;
            _unitAreaMeans = unitAreaMeans ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            _facadeShares = facadeShares ?? new Dictionary<string, IReadOnlyList<FacadeShare>>(StringComparer.OrdinalIgnoreCase);
            _adjacency = adjacency ?? new Dictionary<string, double>(StringComparer.Ordinal);
            _adjacencyDefault = adjacencyDefault;
        }

        public int Version { get; }

        /// <summary>The seed prior table (German-norm-derived placeholder for EBA data).</summary>
        public static PortfolioPriors Default()
        {
            Dictionary<string, double> unitAreaMeans = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                { "studio", 42.0 },
                { "one_bed", 60.0 },
                { "two_bed", 85.0 },
                { "three_bed", 108.0 },
            };

            Dictionary<string, IReadOnlyList<FacadeShare>> facadeShares =
                new Dictionary<string, IReadOnlyList<FacadeShare>>(StringComparer.OrdinalIgnoreCase)
                {
                    {
                        "one_bed",
                        new List<FacadeShare> { new FacadeShare("bedroom", 0.42), new FacadeShare("living", 0.58) }
                    },
                    {
                        "two_bed",
                        new List<FacadeShare>
                        {
                            new FacadeShare("bedroom", 0.27),
                            new FacadeShare("bedroom", 0.27),
                            new FacadeShare("living", 0.46),
                        }
                    },
                };

            Dictionary<string, double> adjacency = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                { PairKey("kitchen", "living"), 0.90 },
                { PairKey("living_sleeping", "kitchen"), 0.85 },
                { PairKey("bathroom", "bedroom"), 0.70 },
                { PairKey("living", "bedroom"), 0.65 },
                { PairKey("bathroom", "kitchen"), 0.65 },
                { PairKey("bedroom", "bedroom"), 0.45 },
                { PairKey("kitchen", "bedroom"), 0.40 },
                { PairKey("living_sleeping", "bathroom"), 0.35 },
                { PairKey("bathroom", "living"), 0.30 },
            };

            return new PortfolioPriors(CurrentVersion, unitAreaMeans, facadeShares, adjacency, 0.50);
        }

        /// <summary>Typical area (m²) for the unit type, or <paramref name="fallback"/> if unknown.</summary>
        public double UnitAreaMean(string unitType, double fallback)
        {
            if (!string.IsNullOrWhiteSpace(unitType) && _unitAreaMeans.TryGetValue(unitType, out double mean))
            {
                return mean;
            }

            return fallback;
        }

        /// <summary>
        /// Ordered typical width shares for the unit type's facade-band rooms; an empty
        /// list when the type is unknown or its facade band is a single room.
        /// </summary>
        public IReadOnlyList<FacadeShare> FacadeShares(string unitType)
        {
            if (!string.IsNullOrWhiteSpace(unitType) && _facadeShares.TryGetValue(unitType, out IReadOnlyList<FacadeShare> shares))
            {
                return shares;
            }

            return Array.Empty<FacadeShare>();
        }

        /// <summary>
        /// Neighbour affinity in [0,1] for the unordered room-type pair, or the neutral
        /// default for an unlisted pair. Order-independent and case-insensitive.
        /// </summary>
        public double AdjacencyWeight(string a, string b)
        {
            if (_adjacency.TryGetValue(PairKey(a, b), out double weight))
            {
                return weight;
            }

            return _adjacencyDefault;
        }

        private static string PairKey(string a, string b)
        {
            string left = (a ?? string.Empty).Trim().ToLowerInvariant();
            string right = (b ?? string.Empty).Trim().ToLowerInvariant();
            return string.CompareOrdinal(left, right) <= 0 ? left + "|" + right : right + "|" + left;
        }
    }

    /// <summary>One facade-band room's typical width share of its unit type (Phase 3 priors).</summary>
    public sealed class FacadeShare
    {
        public FacadeShare(string roomType, double share)
        {
            RoomType = roomType;
            Share = share;
        }

        public string RoomType { get; }

        public double Share { get; }
    }
}
