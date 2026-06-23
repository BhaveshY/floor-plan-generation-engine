using System.Collections.Generic;
using FloorPlanGeneration.Geometry;
using FloorPlanGeneration.Schema;

namespace FloorPlanGeneration.Generation
{
    /// <summary>
    /// Scores how well a layout's realized room adjacencies match the owned-data
    /// adjacency priors (architectural-finetuning Phase 3, scoring lever). Two rooms
    /// in the same unit are adjacent when they share a wall segment of positive
    /// length; the score is the mean prior preference over every such pair, in [0,1].
    /// A layout with no interior adjacencies scores the neutral default. Pure and
    /// deterministic; consumed by the variant scorer only when
    /// <see cref="RuleSet.UsePortfolioPriors"/> is set.
    /// </summary>
    public static class AdjacencyScorer
    {
        public static double Score(IEnumerable<UnitLayout> units, PortfolioPriors priors, double tolerance)
        {
            double sum = 0.0;
            int count = 0;
            foreach (UnitLayout unit in units)
            {
                IReadOnlyList<RoomLayout> rooms = unit.Rooms;
                if (rooms == null)
                {
                    continue;
                }

                for (int i = 0; i < rooms.Count; i++)
                {
                    for (int j = i + 1; j < rooms.Count; j++)
                    {
                        if (ShareWall(rooms[i], rooms[j], tolerance))
                        {
                            sum += priors.AdjacencyWeight(rooms[i].RoomType, rooms[j].RoomType);
                            count++;
                        }
                    }
                }
            }

            // No interior adjacencies -> neutral; an unlisted pair returns the prior's default.
            return count == 0 ? priors.AdjacencyWeight(string.Empty, string.Empty) : sum / count;
        }

        private static bool ShareWall(RoomLayout a, RoomLayout b, double tolerance)
        {
            Polygon2 first = ToPolygon(a.Polygon);
            Polygon2 second = ToPolygon(b.Polygon);
            foreach (LineSegment2 edgeA in first.Edges())
            {
                foreach (LineSegment2 edgeB in second.Edges())
                {
                    if (GeometryPredicates.SharedSegmentLength(edgeA, edgeB, tolerance) > tolerance)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static Polygon2 ToPolygon(PolygonInput input)
        {
            if (input == null || input.Points == null)
            {
                return new Polygon2();
            }

            List<Point2> points = new List<Point2>();
            foreach (Point2 point in input.Points)
            {
                if (point != null)
                {
                    points.Add(point.Clone());
                }
            }

            if (points.Count > 1 && points[0].EqualsWithin(points[points.Count - 1], 1e-9))
            {
                points.RemoveAt(points.Count - 1);
            }

            return new Polygon2(input.Id, points);
        }
    }
}
