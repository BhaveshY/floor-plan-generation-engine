using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FloorPlanGeneration;
using FloorPlanGeneration.Cli;
using FloorPlanGeneration.Geometry;
using FloorPlanGeneration.Schema;
using FloorPlanGeneration.Topology;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FloorPlanGeneration.Tests
{
    public sealed class FloorPlanEngineTests
    {
        [Fact]
        public void RectangularInput_ReturnsDeterministicRankedVariants()
        {
            EngineOutput output = new FloorPlanEngine().Generate(RectangularInput(seed: 1234, variantCount: 4));

            Assert.Equal("succeeded", output.Status);
            Assert.NotNull(output.Metadata);
            Assert.Equal("1.2", output.Metadata.SchemaVersion);
            Assert.Equal(1234, output.Metadata.Seed);
            Assert.Equal(4, output.Metadata.GenerationSettings.VariantCount);
            Assert.Equal("balanced", output.Metadata.GenerationSettings.Strictness);
            Assert.Equal("FP::Generated::Units", output.Metadata.Layers["units"]);
            Assert.Equal("FP::Generated::Diagnostics", output.Metadata.Layers["diagnostics"]);
            Assert.Equal(648.0, output.Metadata.Floorplate.GrossArea);
            Assert.Equal(648.0, output.Metadata.Floorplate.UsableArea);
            Assert.Equal(36.0, output.Metadata.Floorplate.Bounds.Width);
            Assert.Equal(18.0, output.Metadata.Floorplate.Bounds.Height);
            Assert.Equal(4, output.Variants.Count);
            Assert.All(output.Variants, v => Assert.True(v.Validation.Passed));
            Assert.All(output.Variants, v => Assert.NotEmpty(v.Units));
            Assert.All(output.Variants.SelectMany(v => v.Units), u =>
            {
                Assert.Equal("FP::Generated::Units", u.Layer);
                Assert.True(u.Bounds.Area > 0.0);
            });
            Assert.All(output.Variants.SelectMany(v => v.Rooms), r =>
            {
                Assert.Equal("FP::Generated::Rooms", r.Layer);
                Assert.True(r.Bounds.Area > 0.0);
            });
            Assert.All(output.Variants.SelectMany(v => v.Corridors), c =>
            {
                Assert.Equal("FP::Generated::Corridors", c.Layer);
                Assert.True(c.Bounds.Area > 0.0);
            });

            List<double> scores = output.Variants.Select(v => v.Metrics.Score).ToList();
            Assert.Equal(scores.OrderByDescending(s => s).ToList(), scores);

            EngineOutput repeated = new FloorPlanEngine().Generate(RectangularInput(seed: 1234, variantCount: 4));
            Assert.Equal(Signatures(output), Signatures(repeated));
        }

        [Fact]
        public void GeneratedElementsExposeStableExternalIdsAndLayerValidationChecks()
        {
            EngineOutput output = new FloorPlanEngine().Generate(RectangularInput(seed: 20260519, variantCount: 3));

            Assert.Equal("succeeded", output.Status);
            Assert.All(output.Variants, variant =>
            {
                string variantPrefix = "fp://rectangular-test/variants/" + variant.VariantId;
                Assert.Equal(variantPrefix, variant.ExternalId);
                Assert.Contains(variant.Validation.Checks, c => c.Name == "stable_external_ids" && c.Passed);
                Assert.Contains(variant.Validation.Checks, c => c.Name == "generated_layers" && c.Passed);

                Assert.All(variant.Units, unit =>
                {
                    Assert.StartsWith(variantPrefix + "/units/", unit.ExternalId, StringComparison.Ordinal);
                    Assert.Equal(LayerNames.GeneratedUnits, unit.Layer);
                });
                Assert.All(variant.Rooms, room =>
                {
                    Assert.StartsWith(variantPrefix + "/rooms/", room.ExternalId, StringComparison.Ordinal);
                    Assert.Equal(LayerNames.GeneratedRooms, room.Layer);
                });
                Assert.All(variant.Corridors, corridor =>
                {
                    Assert.StartsWith(variantPrefix + "/corridors/", corridor.ExternalId, StringComparison.Ordinal);
                    Assert.Equal(LayerNames.GeneratedCorridors, corridor.Layer);
                });
                Assert.All(variant.Walls, wall =>
                {
                    Assert.StartsWith(variantPrefix + "/walls/", wall.ExternalId, StringComparison.Ordinal);
                    Assert.Equal(LayerNames.GeneratedWalls, wall.Layer);
                });
                Assert.All(variant.DoorsOpenings, door =>
                {
                    Assert.StartsWith(variantPrefix + "/doors/", door.ExternalId, StringComparison.Ordinal);
                    Assert.Equal(LayerNames.GeneratedDoors, door.Layer);
                });
                Assert.All(variant.Labels, label =>
                {
                    Assert.StartsWith(variantPrefix + "/labels/", label.ExternalId, StringComparison.Ordinal);
                    Assert.Equal(LayerNames.GeneratedLabels, label.Layer);
                });
                Assert.All(variant.Topology.Hypergraph.Nodes, node =>
                    Assert.StartsWith(variantPrefix + "/topology/hypergraph/nodes/", node.ExternalId, StringComparison.Ordinal));
                Assert.All(variant.Topology.Hypergraph.Hyperedges, edge =>
                    Assert.StartsWith(variantPrefix + "/topology/hypergraph/hyperedges/", edge.ExternalId, StringComparison.Ordinal));
                Assert.All(variant.Topology.Hypergraph.Incidence, incidence =>
                    Assert.StartsWith(variantPrefix + "/topology/hypergraph/incidence/", incidence.ExternalId, StringComparison.Ordinal));
            });

            EngineOutput repeated = new FloorPlanEngine().Generate(RectangularInput(seed: 20260519, variantCount: 3));
            Assert.Equal(
                output.Variants.SelectMany(v => v.Units.Select(u => u.ExternalId)),
                repeated.Variants.SelectMany(v => v.Units.Select(u => u.ExternalId)));
        }

        [Fact]
        public void EngineUnexpectedFailureDiagnostics_DoNotAppendRawExceptionMessages()
        {
            string engine = File.ReadAllText(Path.Combine(RepositoryRoot(), "FloorPlanGeneration", "FloorPlanEngine.cs"));

            Assert.Contains(
                "Floor plan generation failed unexpectedly. Review the input contract and try again.",
                engine,
                StringComparison.Ordinal);
            Assert.Contains(
                "Floor plan validation failed unexpectedly. Review the input contract and try again.",
                engine,
                StringComparison.Ordinal);
            Assert.DoesNotContain("failed unexpectedly: \" + ex.Message", engine, StringComparison.Ordinal);
        }

        [Fact]
        public void GeneratedTopologyIncludesPortableHypergraphContract()
        {
            EngineOutput output = new FloorPlanEngine().Generate(RectangularInput(seed: 20260519, variantCount: 1));
            LayoutVariant variant = Assert.Single(output.Variants);
            FloorPlanHypergraph hypergraph = variant.Topology.Hypergraph;

            Assert.NotNull(hypergraph);
            Assert.Equal("hypergraph-floorplan-1.0", hypergraph.SchemaVersion);
            Assert.Equal("root", hypergraph.Root.Name);
            Assert.False(hypergraph.Root.Final);
            Assert.Contains(hypergraph.Root.Children, child => child.Name == "circulation");
            Assert.Contains(hypergraph.Root.Children, child => child.Name == "units");
            Assert.Contains(variant.Validation.Checks, check => check.Name == "hypergraph_contract" && check.Passed);
            Assert.Empty(HypergraphBuilder.Validate(hypergraph));

            Assert.All(variant.Topology.Nodes, topologyNode =>
                Assert.Contains(hypergraph.Nodes, node => node.Id == topologyNode.Id));
            Assert.All(variant.Walls, wall =>
                Assert.Contains(hypergraph.Nodes, node => node.Id == wall.Id && node.Kind.StartsWith("wall:", StringComparison.Ordinal)));
            Assert.All(variant.DoorsOpenings, door =>
                Assert.Contains(hypergraph.Hyperedges, edge =>
                    edge.Kind == "door" &&
                    edge.Attributes.TryGetValue("doorId", out string doorId) &&
                    doorId == door.Id));

            Assert.Contains(hypergraph.Hyperedges, edge => edge.Kind == "subdivision" && edge.Members.Count > 2);
            Assert.Contains(hypergraph.Hyperedges, edge => edge.Kind == "adjacency");
            Assert.Contains(hypergraph.Hyperedges, edge => edge.Kind == "containment");
            Assert.Contains(hypergraph.Hyperedges, edge => edge.Kind == "facade");
            Assert.Equal(hypergraph.Hyperedges.Sum(edge => edge.Members.Count), hypergraph.Incidence.Count);

            HashSet<string> nodeIds = hypergraph.Nodes.Select(node => node.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.All(hypergraph.Hyperedges.SelectMany(edge => edge.Members), member => Assert.Contains(member.NodeId, nodeIds));
            HashSet<string> memberKeys = hypergraph.Hyperedges
                .SelectMany(edge => edge.Members.Select(member => HypergraphTuple(edge.Id, member.NodeId, member.Role)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.All(hypergraph.Incidence, item => Assert.Contains(HypergraphTuple(item.HyperedgeId, item.NodeId, item.Role), memberKeys));

            foreach (HypergraphDataNode dataNode in HypergraphBuilder.FlattenDataNodes(hypergraph.Root))
            {
                HypergraphNode node = Assert.Single(hypergraph.Nodes, item => item.Id == dataNode.Name);
                Assert.Equal(dataNode.MergeId, node.MergeId);
                Assert.Equal(dataNode.Final, node.Final);
                Assert.Equal(dataNode.Area, node.Area);
                Assert.Equal(dataNode.Angle, node.Angle);
                Assert.Equal(
                    dataNode.Children.Select(child => child.Name).OrderBy(id => id, StringComparer.OrdinalIgnoreCase),
                    node.Children.OrderBy(id => id, StringComparer.OrdinalIgnoreCase));
                Assert.Equal(
                    dataNode.Connected.OrderBy(id => id, StringComparer.OrdinalIgnoreCase),
                    node.Connected.OrderBy(id => id, StringComparer.OrdinalIgnoreCase));
            }

            Assert.Equal(hypergraph.Nodes.Count, hypergraph.Matrices.NodeOrder.Count);
            Assert.Equal(hypergraph.Hyperedges.Count, hypergraph.Matrices.HyperedgeOrder.Count);
            Assert.Equal(
                hypergraph.Nodes.Select(node => node.Id).OrderBy(id => id, StringComparer.OrdinalIgnoreCase),
                hypergraph.Matrices.NodeOrder);
            Assert.Equal(
                hypergraph.Hyperedges.Select(edge => edge.Id).OrderBy(id => id, StringComparer.OrdinalIgnoreCase),
                hypergraph.Matrices.HyperedgeOrder);
            Assert.Equal(hypergraph.Nodes.Count, hypergraph.Matrices.SubdivisionConnectivity.Count);
            Assert.Equal(hypergraph.Nodes.Count, hypergraph.Matrices.AdjacencyConnectivity.Count);
            Assert.Equal(hypergraph.Nodes.Count, hypergraph.Matrices.Area.Count);
            Assert.Equal(hypergraph.Nodes.Count, hypergraph.Matrices.Angle.Count);
            Assert.Equal(hypergraph.Nodes.Count, hypergraph.Matrices.Incidence.Count);
            Assert.All(hypergraph.Matrices.Incidence, row => Assert.Equal(hypergraph.Hyperedges.Count, row.Count));

            HypergraphDataNode connectedRoom = HypergraphBuilder.FlattenDataNodes(hypergraph.Root)
                .First(node => node.Final && node.Connected.Count > 0 && node.MergeId != "corridor");
            Assert.True(connectedRoom.TreeNodeMesh.Area > 0.0);
            Assert.NotEqual(0.0, connectedRoom.TreeNodeMesh.Centroid.Mag);

            string rootJson = JsonSerializer.Serialize(hypergraph.Root, JsonOptions());
            Assert.Contains("\"mergeid\"", rootJson, StringComparison.Ordinal);
            Assert.Contains("\"treeNodeMesh\"", rootJson, StringComparison.Ordinal);
            Assert.Contains("\"Area\"", rootJson, StringComparison.Ordinal);
            Assert.DoesNotContain("\"mergeId\"", rootJson, StringComparison.Ordinal);
        }

        [Fact]
        public void HypergraphValidationRejectsContractDrift()
        {
            EngineOutput output = new FloorPlanEngine().Generate(RectangularInput(seed: 20260519, variantCount: 1));
            FloorPlanHypergraph hypergraph = Assert.Single(output.Variants).Topology.Hypergraph;

            FloorPlanHypergraph unknownMember = CloneHypergraph(hypergraph);
            unknownMember.Hyperedges.First(edge => edge.Members.Count > 0).Members[0].NodeId = "missing-node";
            Assert.Contains(
                HypergraphBuilder.Validate(unknownMember),
                error => error.Contains("references unknown node missing-node", StringComparison.Ordinal));

            FloorPlanHypergraph missingIncidence = CloneHypergraph(hypergraph);
            missingIncidence.Incidence.RemoveAt(0);
            Assert.Contains(
                HypergraphBuilder.Validate(missingIncidence),
                error => error.StartsWith("Missing incidence for hyperedge member", StringComparison.Ordinal));

            FloorPlanHypergraph badOrder = CloneHypergraph(hypergraph);
            string replacedNode = badOrder.Matrices.NodeOrder[0];
            badOrder.Matrices.NodeOrder[0] = "not-a-node";
            List<string> orderErrors = HypergraphBuilder.Validate(badOrder);
            Assert.Contains(orderErrors, error => error.Contains("references unknown node not-a-node", StringComparison.Ordinal));
            Assert.Contains(orderErrors, error => error.Contains("is missing node " + replacedNode, StringComparison.Ordinal));

            FloorPlanHypergraph badMatrix = CloneHypergraph(hypergraph);
            HypergraphIncidence incidence = badMatrix.Incidence[0];
            int row = badMatrix.Matrices.NodeOrder.FindIndex(id => string.Equals(id, incidence.NodeId, StringComparison.OrdinalIgnoreCase));
            int column = badMatrix.Matrices.HyperedgeOrder.FindIndex(id => string.Equals(id, incidence.HyperedgeId, StringComparison.OrdinalIgnoreCase));
            badMatrix.Matrices.Incidence[row][column] = 0.0;
            Assert.Contains(
                HypergraphBuilder.Validate(badMatrix),
                error => error.StartsWith("Matrix incidence does not match incidence record", StringComparison.Ordinal));

            FloorPlanHypergraph badNodeReference = CloneHypergraph(hypergraph);
            HypergraphNode nodeWithChild = badNodeReference.Nodes.First(node => node.Children.Count > 0);
            nodeWithChild.Children[0] = "missing-child";
            Assert.Contains(
                HypergraphBuilder.Validate(badNodeReference),
                error => error.Contains("references unknown child missing-child", StringComparison.Ordinal));

            FloorPlanHypergraph badDataNodeName = CloneHypergraph(hypergraph);
            badDataNodeName.Root.Children[0].Name = "renamed-circulation";
            Assert.Contains(
                HypergraphBuilder.Validate(badDataNodeName),
                error => error.Contains(
                    "DataNode renamed-circulation is missing matching hypergraph node",
                    StringComparison.Ordinal));

            FloorPlanHypergraph badSchema = CloneHypergraph(hypergraph);
            badSchema.SchemaVersion = "wrong";
            Assert.Contains(
                HypergraphBuilder.Validate(badSchema),
                error => error.Contains("schemaVersion must be hypergraph-floorplan-1.0", StringComparison.Ordinal));

            FloorPlanHypergraph badDataNodeMirror = CloneHypergraph(hypergraph);
            HypergraphDataNode mirroredLeaf = HypergraphBuilder.FlattenDataNodes(badDataNodeMirror.Root).First(node => node.Final);
            badDataNodeMirror.Nodes.First(node => node.Id == mirroredLeaf.Name).Area += 1.0;
            Assert.Contains(HypergraphBuilder.Validate(badDataNodeMirror), error => error.Contains("does not mirror DataNode area", StringComparison.Ordinal));

            FloorPlanHypergraph badNodeProjection = CloneHypergraph(hypergraph);
            HypergraphNode projectedNode = badNodeProjection.Nodes.First(node => node.Id == "circulation");
            projectedNode.ParentId = "wrong-parent";
            projectedNode.MergeId = "wrong-merge";
            projectedNode.Final = true;
            List<string> projectionErrors = HypergraphBuilder.Validate(badNodeProjection);
            Assert.Contains(projectionErrors, error => error.Contains("does not mirror DataNode parent", StringComparison.Ordinal));
            Assert.Contains(projectionErrors, error => error.Contains("does not mirror DataNode mergeid", StringComparison.Ordinal));
            Assert.Contains(projectionErrors, error => error.Contains("does not mirror DataNode final flag", StringComparison.Ordinal));

            FloorPlanHypergraph badConnectedProjection = CloneHypergraph(hypergraph);
            HypergraphDataNode connectedDataNode = HypergraphBuilder.FlattenDataNodes(badConnectedProjection.Root).First(node => node.Connected.Count > 0);
            connectedDataNode.Connected.Add("missing-connected");
            badConnectedProjection.Nodes.First(node => node.Id == connectedDataNode.Name).Connected.Add("missing-connected");
            Assert.Contains(
                HypergraphBuilder.Validate(badConnectedProjection),
                error => error.Contains(
                    "DataNode " + connectedDataNode.Name + " references unknown connected node missing-connected",
                    StringComparison.Ordinal));

            FloorPlanHypergraph duplicateMember = CloneHypergraph(hypergraph);
            Hyperedge edgeWithMember = duplicateMember.Hyperedges.First(edge => edge.Members.Count > 0);
            edgeWithMember.Members.Add(new HyperedgeMember { NodeId = edgeWithMember.Members[0].NodeId, Role = edgeWithMember.Members[0].Role });
            Assert.Contains(HypergraphBuilder.Validate(duplicateMember), error => error.Contains("contains duplicate member", StringComparison.Ordinal));

            FloorPlanHypergraph badAreaMatrix = CloneHypergraph(hypergraph);
            Hyperedge subdivision = badAreaMatrix.Hyperedges.First(edge => edge.Kind == "subdivision" && edge.Members.Any(member => member.Role == "child"));
            string parentId = subdivision.Members.First(member => member.Role == "parent").NodeId;
            string childId = subdivision.Members.First(member => member.Role == "child").NodeId;
            int matrixRow = badAreaMatrix.Matrices.NodeOrder.FindIndex(id => string.Equals(id, parentId, StringComparison.OrdinalIgnoreCase));
            int matrixColumn = badAreaMatrix.Matrices.NodeOrder.FindIndex(id => string.Equals(id, childId, StringComparison.OrdinalIgnoreCase));
            badAreaMatrix.Matrices.Area[matrixRow][matrixColumn] += 1.0;
            Assert.Contains(
                HypergraphBuilder.Validate(badAreaMatrix),
                error => error.Contains("Matrix area does not match subdivision child", StringComparison.Ordinal));

            FloorPlanHypergraph badAngleMatrix = CloneHypergraph(hypergraph);
            badAngleMatrix.Matrices.Angle[matrixRow][matrixColumn] += 1.0;
            Assert.Contains(
                HypergraphBuilder.Validate(badAngleMatrix),
                error => error.Contains("Matrix angle does not match subdivision child", StringComparison.Ordinal));

            FloorPlanHypergraph unexpectedAreaMatrix = CloneHypergraph(hypergraph);
            unexpectedAreaMatrix.Matrices.Area[0][0] = 99.0;
            Assert.Contains(
                HypergraphBuilder.Validate(unexpectedAreaMatrix),
                error => error.Contains("Matrix area contains unexpected subdivision value", StringComparison.Ordinal));

            FloorPlanHypergraph badSubdivision = CloneHypergraph(hypergraph);
            Hyperedge badSubdivisionEdge = badSubdivision.Hyperedges.First(edge =>
                edge.Kind == "subdivision" &&
                edge.Members.Any(member => member.Role == "child"));
            string replacementNode = badSubdivision.Nodes.First(node => badSubdivisionEdge.Members.All(member => member.NodeId != node.Id)).Id;
            badSubdivisionEdge.Members.First(member => member.Role == "child").NodeId = replacementNode;
            Assert.Contains(
                HypergraphBuilder.Validate(badSubdivision),
                error => error.Contains(
                    "Subdivision hyperedge " + badSubdivisionEdge.Id + " children contains unexpected",
                    StringComparison.Ordinal));
        }

        [Theory]
        [MemberData(nameof(HypergraphSampleInputs))]
        public void GeneratedHypergraphContractsValidateAcrossSupportedFloorplates(string sampleName, EngineInput input)
        {
            EngineOutput output = new FloorPlanEngine().Generate(input);

            Assert.False(string.IsNullOrWhiteSpace(sampleName));
            Assert.Equal("succeeded", output.Status);
            Assert.NotEmpty(output.Variants);
            Assert.All(output.Variants, variant =>
            {
                Assert.Contains(variant.Validation.Checks, check => check.Name == "hypergraph_contract" && check.Passed);
                Assert.NotNull(variant.Topology.Hypergraph);
                Assert.Empty(HypergraphBuilder.Validate(variant.Topology.Hypergraph));
                Assert.Contains(HypergraphBuilder.FlattenDataNodes(variant.Topology.Hypergraph.Root), node => node.Name == "units");
            });
        }

        [Fact]
        public void DoorOpeningsReferenceHostWallsAtTheirActualLocations()
        {
            EngineOutput output = new FloorPlanEngine().Generate(RectangularInput(seed: 20260519, variantCount: 3));

            Assert.Equal("succeeded", output.Status);
            Assert.All(output.Variants, variant =>
            {
                Assert.Contains(variant.Validation.Checks, c => c.Name == "door_host_wall_exists" && c.Passed);
                Assert.Contains(variant.Validation.Checks, c => c.Name == "door_connects_known_spaces" && c.Passed);
                Dictionary<string, WallLayout> walls = variant.Walls.ToDictionary(w => w.Id, StringComparer.OrdinalIgnoreCase);

                Assert.All(variant.DoorsOpenings, door =>
                {
                    Assert.True(walls.ContainsKey(door.HostWall), "Missing host wall " + door.HostWall + " for " + door.Id);
                    WallLayout host = walls[door.HostWall];
                    Assert.True(
                        GeometryPredicates.OnSegment(host.Centerline.Start, host.Centerline.End, door.Location, 0.01),
                        door.Id + " location is not on " + door.HostWall);
                });
            });
        }

        [Fact]
        public void RoomPartitionWallsDoNotDuplicateUnitExteriorEdges()
        {
            EngineOutput output = new FloorPlanEngine().Generate(RectangularInput(seed: 1441, variantCount: 2));

            Assert.Equal("succeeded", output.Status);
            Assert.All(output.Variants, variant =>
            {
                List<LineSegment2> unitExteriorEdges = variant.Units
                    .SelectMany(u => ToPolygon(u.Polygon).Edges())
                    .ToList();
                List<WallLayout> partitions = variant.Walls
                    .Where(w => string.Equals(w.LayerType, "room_partition", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                List<string> partitionKeys = partitions.Select(w => SegmentKey(w.Centerline)).ToList();

                Assert.Equal(partitionKeys.Count, partitionKeys.Distinct(StringComparer.Ordinal).Count());
                Assert.All(partitions, wall =>
                {
                    LineSegment2 segment = new LineSegment2(wall.Centerline.Start, wall.Centerline.End);
                    Assert.DoesNotContain(unitExteriorEdges, edge => GeometryPredicates.SharedSegmentLength(segment, edge, 0.01) > 0.01);
                });
            });
        }

        [Fact]
        public void MultiUnitTwoBedUnits_RenderTwoBedroomLayouts()
        {
            // Regression: NormalizeUnitType did not recognise the canonical "two_bed"
            // string the unit-mix planner emits, so every two_bed unit fell through to
            // the one_bed branch and silently rendered a single bedroom.
            EngineInput input = JsonSerializer.Deserialize<EngineInput>(
                File.ReadAllText(Path.Combine(RepositoryRoot(), "samples", "floor-plan-generation", "rectangular-core-input.json")),
                JsonOptions());

            EngineOutput output = new FloorPlanEngine().Generate(input);

            Assert.Equal("succeeded", output.Status);
            List<UnitLayout> twoBedUnits = output.Variants
                .SelectMany(v => v.Units)
                .Where(u => string.Equals(u.Type, "two_bed", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.NotEmpty(twoBedUnits);
            Assert.All(
                twoBedUnits,
                unit => Assert.Equal(2, unit.Rooms.Count(r => string.Equals(r.RoomType, "bedroom", StringComparison.OrdinalIgnoreCase))));
        }

        [Fact]
        public void SameSeed_ProducesSameVariantIdsScoresAndLayoutCounts()
        {
            EngineOutput left = new FloorPlanEngine().Generate(RectangularInput(seed: 8128, variantCount: 5));
            EngineOutput right = new FloorPlanEngine().Generate(RectangularInput(seed: 8128, variantCount: 5));

            Assert.Equal(Signatures(left), Signatures(right));
        }

        [Fact]
        public void ValidateInput_ReturnsDryRunMetadataWithoutGeneratingVariants()
        {
            EngineOutput output = new FloorPlanEngine().Validate(RectangularInput(seed: 44, variantCount: 3));

            Assert.Equal("validated", output.Status);
            Assert.Empty(output.Variants);
            Assert.NotNull(output.Metadata);
            Assert.Equal("rectangular-test", output.ProjectId);
            Assert.Equal(44, output.Metadata.Seed);
            Assert.Equal(3, output.Metadata.GenerationSettings.VariantCount);
            Assert.Equal(648.0, output.Metadata.Floorplate.GrossArea);
            Assert.Equal(0.0, output.Metadata.Floorplate.BlockingFixedElementArea);
            Assert.DoesNotContain(output.Diagnostics, d => d.Severity == "error");
            Assert.Contains(output.Diagnostics, d => d.Code == "input.validated" && d.Severity == "info");
        }

        [Fact]
        public void InvalidInputContract_FailsBeforeCandidateGeneration()
        {
            EngineInput input = RectangularInput(seed: 5, variantCount: 2);
            input.GenerationSettings.VariantCount = 0;
            input.GenerationSettings.Strictness = "chaotic";
            input.Program.TargetUnitTypes[0].MaxArea = input.Program.TargetUnitTypes[0].MinArea - 1.0;

            EngineOutput output = new FloorPlanEngine().Generate(input);

            Assert.Equal("failed", output.Status);
            Assert.Empty(output.Variants);
            Assert.Contains(output.Diagnostics, d => d.Code == "input.invalid_variant_count" && d.Severity == "error");
            Assert.Contains(output.Diagnostics, d => d.Code == "input.invalid_strictness" && d.Severity == "error");
            Assert.Contains(output.Diagnostics, d => d.Code == "input.invalid_unit_type_area_range" && d.Severity == "error");
        }

        [Fact]
        public void SelfIntersectingBoundary_ReturnsFailedDiagnosticsInsteadOfFakePlan()
        {
            EngineInput input = RectangularInput(seed: 1, variantCount: 3);
            input.Floorplate.Outer.Id = "bowtie";
            input.Floorplate.Outer.Points = new List<Point2>
            {
                new Point2(0, 0),
                new Point2(10, 10),
                new Point2(0, 10),
                new Point2(10, 0)
            };

            EngineOutput output = new FloorPlanEngine().Generate(input);

            Assert.Equal("failed", output.Status);
            Assert.Empty(output.Variants);
            Assert.Contains(output.Diagnostics, d => d.Code == "geometry.self_intersection" && d.Severity == "error");
        }

        [Fact]
        public void StrictInfeasibleUnitMix_ReportsValidationFailure()
        {
            EngineInput input = RectangularInput(seed: 7, variantCount: 3);
            input.GenerationSettings.Strictness = "strict";
            input.Program.TargetUnitTypes = new List<UnitTypeTarget>
            {
                new UnitTypeTarget
                {
                    Type = "studio",
                    MinArea = 26.0,
                    MaxArea = 48.0,
                    TargetCount = 99,
                    Weight = 1.0
                }
            };

            EngineOutput output = new FloorPlanEngine().Generate(input);

            Assert.Equal("failed", output.Status);
            Assert.NotEmpty(output.Variants);
            Assert.All(output.Variants, v => Assert.False(v.Validation.Passed));
            Assert.Contains(output.Diagnostics, d => d.Code == "validation.strict_unit_mix" && d.Severity == "error");
            Assert.Contains(output.Variants.SelectMany(v => v.Validation.Checks), c => c.Name == "strict_unit_mix" && !c.Passed);
        }

        [Fact]
        public void LShapedInput_SplitsUsableBandsAroundCoreAndRemainsDeterministic()
        {
            EngineOutput output = new FloorPlanEngine().Generate(LShapedInput(seed: 5601, variantCount: 5));

            Assert.Equal("succeeded", output.Status);
            Assert.Equal(5, output.Variants.Count);
            Assert.All(output.Variants, v => Assert.True(v.Validation.Passed));
            Assert.All(output.Variants, v => Assert.Contains(v.Units, u => u.Id.StartsWith("unit-south-", System.StringComparison.OrdinalIgnoreCase)));
            Assert.All(output.Variants, v => Assert.Contains(v.Units, u => u.Id.StartsWith("unit-north-", System.StringComparison.OrdinalIgnoreCase)));
            Assert.All(output.Variants, v => Assert.DoesNotContain(v.Diagnostics, d => d.Code == "generation.unit_bay_rejected"));

            EngineOutput repeated = new FloorPlanEngine().Generate(LShapedInput(seed: 5601, variantCount: 5));
            Assert.Equal(Signatures(output), Signatures(repeated));
        }

        [Fact]
        public void ModeratelyIrregularInput_GeneratesValidDeterministicVariants()
        {
            EngineOutput output = new FloorPlanEngine().Generate(ModeratelyIrregularInput(seed: 9901, variantCount: 4));

            Assert.Equal("succeeded", output.Status);
            Assert.Equal(4, output.Variants.Count);
            Assert.Equal(1588.0, output.Metadata.Floorplate.GrossArea);
            Assert.Equal(36.0, output.Metadata.Floorplate.BlockingFixedElementArea);
            Assert.Equal(1552.0, output.Metadata.Floorplate.UsableArea);
            Assert.All(output.Variants, v => Assert.True(v.Validation.Passed));
            Assert.All(output.Variants, v => Assert.Contains(v.Corridors[0].Connections, c => c == "core-ir1"));
            Assert.All(output.Variants, v => Assert.True(v.Units.Count >= 8));
            Assert.All(output.Variants.SelectMany(v => v.Units), u => Assert.True(u.Bounds.Area > 0.0));

            EngineOutput repeated = new FloorPlanEngine().Generate(ModeratelyIrregularInput(seed: 9901, variantCount: 4));
            Assert.Equal(Signatures(output), Signatures(repeated));
        }

        [Fact]
        public void CliValidateOnly_ReturnsValidatedJsonWithoutVariants()
        {
            string outputPath = Path.Combine(
                Path.GetTempPath(),
                "floor-plan-validate-" + System.Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".json");
            string inputPath = Path.Combine(RepositoryRoot(), "samples", "floor-plan-generation", "rectangular-core-input.json");
            StringWriter stderr = new StringWriter(CultureInfo.InvariantCulture);

            try
            {
                int exitCode = CliApplication.Run(
                    new[] { "--input", inputPath, "--output", outputPath, "--validate-only", "--summary" },
                    TextReader.Null,
                    TextWriter.Null,
                    stderr);

                Assert.Equal(0, exitCode);
                EngineOutput output = JsonSerializer.Deserialize<EngineOutput>(File.ReadAllText(outputPath), JsonOptions());
                Assert.Equal("validated", output.Status);
                Assert.Empty(output.Variants);
                Assert.Equal("rectangular-core-sample", output.ProjectId);
                Assert.NotNull(output.Metadata);
                Assert.Equal(20260519, output.Metadata.Seed);
                Assert.Contains("status=validated", stderr.ToString());
            }
            finally
            {
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
            }
        }

        [Fact]
        public void CliListSamples_PrintsFriendlySampleNamesWithoutReadingInput()
        {
            StringWriter stdout = new StringWriter(CultureInfo.InvariantCulture);

            int exitCode = CliApplication.Run(
                new[] { "--list-samples" },
                new ThrowingTextReader(),
                stdout,
                new StringWriter(CultureInfo.InvariantCulture));

            Assert.Equal(0, exitCode);
            Assert.Contains("rectangular-core", stdout.ToString());
            Assert.Contains("moderately-irregular-core", stdout.ToString());
        }

        [Fact]
        public void CliAiManifest_ReturnsStructuredAutomationContractWithoutReadingInput()
        {
            StringWriter stdout = new StringWriter(CultureInfo.InvariantCulture);

            int exitCode = CliApplication.Run(
                new[] { "--ai-manifest" },
                new ThrowingTextReader(),
                stdout,
                new StringWriter(CultureInfo.InvariantCulture));

            Assert.Equal(0, exitCode);
            using JsonDocument manifest = JsonDocument.Parse(stdout.ToString());
            Assert.Equal("floorplan-gen", manifest.RootElement.GetProperty("name").GetString());
            Assert.Contains(
                manifest.RootElement.GetProperty("samples").EnumerateArray(),
                sample => sample.GetProperty("name").GetString() == "rectangular-core");
            Assert.Equal(
                "https://bhaveshy.github.io/floor-plan-generation-engine/schemas/1.2/floor-plan-engine-input.schema.json",
                manifest.RootElement.GetProperty("schemas").GetProperty("input").GetString());
            Assert.Contains(
                manifest.RootElement.GetProperty("automationNotes").EnumerateArray(),
                note => note.GetString().Contains("--validate-only", StringComparison.Ordinal));
        }

        [Fact]
        public void CliRejectsVariantOverrideOutsideAutomationContract()
        {
            int exitCode = CliApplication.Run(
                new[] { "--sample", "rectangular-core", "--variants", "21" },
                new ThrowingTextReader(),
                new StringWriter(CultureInfo.InvariantCulture),
                new StringWriter(CultureInfo.InvariantCulture));

            Assert.Equal(64, exitCode);
        }

        [Fact]
        public void CliSummaryUsesInvariantCultureForAgentParsing()
        {
            CultureInfo originalCulture = CultureInfo.CurrentCulture;
            CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                CultureInfo.CurrentUICulture = new CultureInfo("de-DE");
                StringWriter stderr = new StringWriter(CultureInfo.InvariantCulture);

                int exitCode = CliApplication.Run(
                    new[] { "--sample", "rectangular-core", "--variants", "1", "--summary" },
                    new ThrowingTextReader(),
                    new StringWriter(CultureInfo.InvariantCulture),
                    stderr);

                Assert.Equal(0, exitCode);
                Assert.Contains("bestScore=", stderr.ToString());
                Assert.DoesNotContain("bestScore=0,", stderr.ToString());
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
                CultureInfo.CurrentUICulture = originalUiCulture;
            }
        }

        [Fact]
        public void CliOutputWriteFailureReturnsMachineReadableFailure()
        {
            string blockingFile = Path.Combine(Path.GetTempPath(), "floor-plan-blocking-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
            File.WriteAllText(blockingFile, "not a directory");
            string outputPath = Path.Combine(blockingFile, "out.json");
            StringWriter stdout = new StringWriter(CultureInfo.InvariantCulture);
            StringWriter stderr = new StringWriter(CultureInfo.InvariantCulture);

            try
            {
                int exitCode = CliApplication.Run(
                    new[] { "--sample", "rectangular-core", "--variants", "1", "--output", outputPath },
                    new ThrowingTextReader(),
                    stdout,
                    stderr);

                Assert.Equal(2, exitCode);
                Assert.Contains("cli.output_write_failed", stderr.ToString());
                Assert.DoesNotContain(blockingFile, stderr.ToString(), StringComparison.OrdinalIgnoreCase);
                EngineOutput output = JsonSerializer.Deserialize<EngineOutput>(stdout.ToString(), JsonOptions());
                Assert.Equal("failed", output.Status);
                Assert.Contains(output.Diagnostics, d => d.Code == "cli.output_write_failed");
                Assert.DoesNotContain(blockingFile, stdout.ToString(), StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                if (File.Exists(blockingFile))
                {
                    File.Delete(blockingFile);
                }
            }
        }

        [Fact]
        public async Task WebApiManifest_DescribesLocalGenerateContract()
        {
            using (WebApplicationFactory<global::Program> factory = new WebApplicationFactory<global::Program>())
            using (HttpClient client = factory.CreateClient())
            using (JsonDocument manifest = JsonDocument.Parse(await client.GetStringAsync("/api/manifest")))
            {
                Assert.Equal("floor-plan-engine-web-api", manifest.RootElement.GetProperty("name").GetString());
                Assert.Contains(
                    manifest.RootElement.GetProperty("endpoints").EnumerateArray(),
                    endpoint => endpoint.GetProperty("path").GetString() == "/api/generate");
                Assert.Equal(20, manifest.RootElement.GetProperty("limits").GetProperty("variantsMax").GetInt32());
                Assert.Equal(256, manifest.RootElement.GetProperty("limits").GetProperty("requestBodyMaxKb").GetInt32());
            }
        }

        [Theory]
        [InlineData("/")]
        [InlineData("/api/health")]
        public async Task WebAppResponses_IncludeBrowserSecurityHeaders(string path)
        {
            using (WebApplicationFactory<global::Program> factory = new WebApplicationFactory<global::Program>())
            using (HttpClient client = factory.CreateClient())
            using (HttpResponseMessage response = await client.GetAsync(path))
            {
                response.EnsureSuccessStatusCode();

                AssertHeader(response, "X-Content-Type-Options", "nosniff");
                AssertHeader(response, "Cross-Origin-Opener-Policy", "same-origin");
                AssertHeader(response, "Cross-Origin-Resource-Policy", "same-origin");
                AssertHeader(response, "Referrer-Policy", "no-referrer");
                AssertHeader(response, "X-Frame-Options", "DENY");
                AssertHeader(response, "X-DNS-Prefetch-Control", "off");
                AssertHeader(response, "X-Download-Options", "noopen");
                AssertHeader(response, "X-Permitted-Cross-Domain-Policies", "none");
                AssertHeader(
                    response,
                    "Permissions-Policy",
                    "camera=(), microphone=(), geolocation=(), clipboard-write=(self)");

                string csp = AssertHeader(response, "Content-Security-Policy");
                Assert.Contains("default-src 'self'", csp, StringComparison.Ordinal);
                Assert.Contains("script-src 'self'", csp, StringComparison.Ordinal);
                Assert.Contains("style-src 'self'", csp, StringComparison.Ordinal);
                Assert.DoesNotContain("'unsafe-inline'", csp, StringComparison.Ordinal);
                Assert.Contains("object-src 'none'", csp, StringComparison.Ordinal);
                Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
            }
        }

        [Fact]
        public async Task WebApiGenerateUnknownRequestProperty_ReturnsJsonError()
        {
            using (WebApplicationFactory<global::Program> factory = new WebApplicationFactory<global::Program>())
            using (HttpClient client = factory.CreateClient())
            using (HttpContent content = new StringContent(
                "{ \"sampleName\": \"rectangular-core\", \"unexpected\": true }",
                System.Text.Encoding.UTF8,
                "application/json"))
            {
                HttpResponseMessage response = await client.PostAsync("/api/generate", content);

                Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
                using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal("invalid_request", body.RootElement.GetProperty("error").GetString());
                string message = body.RootElement.GetProperty("message").GetString();
                Assert.Equal("Request JSON could not be bound. Check property names and the expected body shape.", message);
                Assert.DoesNotContain("unexpected", message, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public async Task WebApiGenerateOversizedRequest_ReturnsPayloadTooLarge()
        {
            using (WebApplicationFactory<global::Program> factory = new WebApplicationFactory<global::Program>())
            using (HttpClient client = factory.CreateClient())
            using (HttpContent content = new StringContent(
                "{ \"sampleName\": \"rectangular-core\", \"padding\": \"" + new string('x', 270_000) + "\" }",
                System.Text.Encoding.UTF8,
                "application/json"))
            {
                HttpResponseMessage response = await client.PostAsync("/api/generate", content);

                Assert.Equal(System.Net.HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
                using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal("payload_too_large", body.RootElement.GetProperty("error").GetString());
                Assert.Contains("256 KB", body.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
            }
        }

        [Fact]
        public async Task WebApiGenerateNonJsonContent_ReturnsUnsupportedMediaType()
        {
            using (WebApplicationFactory<global::Program> factory = new WebApplicationFactory<global::Program>())
            using (HttpClient client = factory.CreateClient())
            using (HttpContent content = new StringContent(
                "{ \"sampleName\": \"rectangular-core\" }",
                System.Text.Encoding.UTF8,
                "text/plain"))
            {
                HttpResponseMessage response = await client.PostAsync("/api/generate", content);

                Assert.Equal(System.Net.HttpStatusCode.UnsupportedMediaType, response.StatusCode);
                using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal("unsupported_media_type", body.RootElement.GetProperty("error").GetString());
                Assert.Contains("application/json", body.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
            }
        }

        [Fact]
        public async Task WebApiGenerateUnknownSample_ReturnsGenericInputError()
        {
            const string sampleName = "missing-sample-reflection-check";
            using (WebApplicationFactory<global::Program> factory = new WebApplicationFactory<global::Program>())
            using (HttpClient client = factory.CreateClient())
            using (HttpContent content = JsonContent.Create(new { sampleName }))
            {
                HttpResponseMessage response = await client.PostAsync("/api/generate", content);

                Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
                using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal("invalid_input", body.RootElement.GetProperty("error").GetString());
                string message = body.RootElement.GetProperty("message").GetString();
                Assert.Equal("Input could not be resolved. Use a known sample or provide a valid input object.", message);
                Assert.DoesNotContain(sampleName, message, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public async Task WebApiSampleUnknownName_ReturnsGenericNotFoundError()
        {
            const string sampleName = "missing-sample-reflection-check";
            using (WebApplicationFactory<global::Program> factory = new WebApplicationFactory<global::Program>())
            using (HttpClient client = factory.CreateClient())
            {
                HttpResponseMessage response = await client.GetAsync("/api/samples/" + sampleName);

                Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
                using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal("sample_not_found", body.RootElement.GetProperty("error").GetString());
                string message = body.RootElement.GetProperty("message").GetString();
                Assert.Equal("Sample could not be found. Use a known bundled sample name.", message);
                Assert.DoesNotContain(sampleName, message, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void WebApiGenerationFailureResponse_DoesNotAppendRawExceptionMessages()
        {
            string program = File.ReadAllText(Path.Combine(RepositoryRoot(), "FloorPlanGeneration.Web", "Program.cs"));

            Assert.Contains("GenerationFailedResult()", program, StringComparison.Ordinal);
            Assert.Contains("The engine could not complete this request. Review diagnostics or adjust the input.", program, StringComparison.Ordinal);
            Assert.DoesNotContain("The engine could not complete this request. \" + ex.Message", program, StringComparison.Ordinal);
        }

        [Fact]
        public async Task WebApiGenerateSample_ReturnsSuccessfulEngineOutput()
        {
            using (WebApplicationFactory<global::Program> factory = new WebApplicationFactory<global::Program>())
            using (HttpClient client = factory.CreateClient())
            {
                HttpResponseMessage response = await client.PostAsJsonAsync("/api/generate", new
                {
                    sampleName = "rectangular-core",
                    variants = 1
                });
                response.EnsureSuccessStatusCode();

                using (JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                {
                    Assert.Equal("succeeded", body.RootElement.GetProperty("status").GetString());
                    Assert.Equal(1, body.RootElement.GetProperty("variantCount").GetInt32());
                    Assert.Equal(1, body.RootElement.GetProperty("validVariantCount").GetInt32());
                    Assert.Equal("succeeded", body.RootElement.GetProperty("output").GetProperty("status").GetString());
                }
            }
        }

        [Fact]
        public async Task WebApiGenerate_DisablesResponseCaching()
        {
            using (WebApplicationFactory<global::Program> factory = new WebApplicationFactory<global::Program>())
            using (HttpClient client = factory.CreateClient())
            {
                using HttpResponseMessage response = await client.PostAsJsonAsync("/api/generate", new
                {
                    sampleName = "rectangular-core",
                    variants = 1
                });

                response.EnsureSuccessStatusCode();

                AssertNoStoreResponseCaching(response);
                AssertHeader(response, "Pragma", "no-cache");
            }
        }

        [Fact]
        public void CliSample_GeneratesOutputWithoutManualInputPath()
        {
            StringWriter stdout = new StringWriter(CultureInfo.InvariantCulture);

            int exitCode = CliApplication.Run(
                new[] { "--sample", "rectangular-core", "--variants", "1" },
                new ThrowingTextReader(),
                stdout,
                new StringWriter(CultureInfo.InvariantCulture));

            Assert.Equal(0, exitCode);
            EngineOutput output = JsonSerializer.Deserialize<EngineOutput>(stdout.ToString(), JsonOptions());
            Assert.Equal("succeeded", output.Status);
            Assert.Equal("rectangular-core-sample", output.ProjectId);
            Assert.Single(output.Variants);
        }

        [Fact]
        public void CliWriteSample_WritesStarterJsonWithoutRunningGeneration()
        {
            StringWriter stdout = new StringWriter(CultureInfo.InvariantCulture);

            int exitCode = CliApplication.Run(
                new[] { "--write-sample", "l-shaped-core" },
                new ThrowingTextReader(),
                stdout,
                new StringWriter(CultureInfo.InvariantCulture));

            Assert.Equal(0, exitCode);
            EngineInput input = JsonSerializer.Deserialize<EngineInput>(stdout.ToString(), JsonOptions());
            Assert.Equal("l-shaped-core-sample", input.Project.Id);
            Assert.NotEmpty(input.Floorplate.Outer.Points);
        }

        [Fact]
        public void ModeratelyIrregularSampleJson_GeneratesValidOutput()
        {
            string outputPath = Path.Combine(
                Path.GetTempPath(),
                "floor-plan-irregular-" + System.Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".json");
            string inputPath = Path.Combine(
                RepositoryRoot(),
                "samples",
                "floor-plan-generation",
                "moderately-irregular-core-input.json");

            try
            {
                int exitCode = CliApplication.Run(
                    new[] { "--input", inputPath, "--output", outputPath },
                    TextReader.Null,
                    TextWriter.Null,
                    new StringWriter(CultureInfo.InvariantCulture));

                Assert.Equal(0, exitCode);
                EngineOutput output = JsonSerializer.Deserialize<EngineOutput>(File.ReadAllText(outputPath), JsonOptions());
                Assert.Equal("succeeded", output.Status);
                Assert.Equal(4, output.Variants.Count);
                Assert.Equal(1588.0, output.Metadata.Floorplate.GrossArea);
                Assert.All(output.Variants, v => Assert.Contains(v.Corridors[0].Connections, c => c == "core-ir1"));
            }
            finally
            {
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
            }
        }

        [Fact]
        public void CliRejectsUnknownJsonProperties()
        {
            string inputJson = "{ \"unknownTopLevel\": true, \"project\": { \"id\": \"bad\" }, \"floorplate\": { \"outer\": { \"points\": [] } } }";
            StringWriter stdout = new StringWriter(CultureInfo.InvariantCulture);

            int exitCode = CliApplication.Run(
                new string[0],
                new StringReader(inputJson),
                stdout,
                new StringWriter(CultureInfo.InvariantCulture));

            Assert.Equal(2, exitCode);
            EngineOutput output = JsonSerializer.Deserialize<EngineOutput>(stdout.ToString(), JsonOptions());
            Assert.Equal("failed", output.Status);
            Assert.Contains(output.Diagnostics, d => d.Code == "cli.invalid_json" && d.Severity == "error");
            Assert.DoesNotContain("unknownTopLevel", stdout.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void CliFailureMessages_DoNotAppendRawExceptionMessages()
        {
            string program = File.ReadAllText(Path.Combine(RepositoryRoot(), "FloorPlanGeneration.Cli", "Program.cs"));

            Assert.DoesNotContain(" + ex.Message", program, StringComparison.Ordinal);
            Assert.DoesNotContain("ex.Message +", program, StringComparison.Ordinal);
        }

        [Fact]
        public void NarrowFloorplate_ReturnsFailedDiagnosticsWithoutVariants()
        {
            EngineInput input = RectangularInput(seed: 11, variantCount: 3);
            input.Project.Id = "narrow-infeasible-test";
            input.Floorplate.Outer.Points = new List<Point2>
            {
                new Point2(0, 0),
                new Point2(12, 0),
                new Point2(12, 4),
                new Point2(0, 4)
            };
            input.FixedElements.Clear();
            input.Rules.MinCorridorWidth = 2.0;
            input.Rules.MinUnitArea = 25.0;

            EngineOutput output = new FloorPlanEngine().Generate(input);

            Assert.Equal("failed", output.Status);
            Assert.Empty(output.Variants);
            Assert.Contains(output.Diagnostics, d => d.Code == "input.floorplate_too_narrow_for_mvp" && d.Severity == "error");
        }

        private static JsonSerializerOptions JsonOptions()
        {
            return new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DictionaryKeyPolicy = JsonNamingPolicy.CamelCase
            };
        }

        private static string AssertHeader(
            HttpResponseMessage response,
            string headerName,
            string expectedValue = null)
        {
            Assert.True(
                response.Headers.TryGetValues(headerName, out IEnumerable<string> values),
                "Missing response header " + headerName + ".");

            string actualValue = Assert.Single(values);
            if (expectedValue != null)
            {
                Assert.Equal(expectedValue, actualValue);
            }

            return actualValue;
        }

        private static void AssertNoStoreResponseCaching(HttpResponseMessage response)
        {
            Assert.NotNull(response.Headers.CacheControl);
            Assert.True(response.Headers.CacheControl.NoStore, "Cache-Control must include no-store.");
            Assert.True(response.Headers.CacheControl.NoCache, "Cache-Control must include no-cache.");
            Assert.True(response.Headers.CacheControl.MustRevalidate, "Cache-Control must include must-revalidate.");
        }

        private static FloorPlanHypergraph CloneHypergraph(FloorPlanHypergraph hypergraph)
        {
            return JsonSerializer.Deserialize<FloorPlanHypergraph>(
                JsonSerializer.Serialize(hypergraph, JsonOptions()),
                JsonOptions());
        }

        private static string HypergraphTuple(string hyperedgeId, string nodeId, string role)
        {
            return (hyperedgeId ?? string.Empty) + "|" + (nodeId ?? string.Empty) + "|" + (role ?? string.Empty);
        }

        public static IEnumerable<object[]> HypergraphSampleInputs()
        {
            yield return new object[] { "rectangular", RectangularInput(seed: 20260519, variantCount: 1) };
            yield return new object[] { "l-shaped", LShapedInput(seed: 20260519, variantCount: 1) };
            yield return new object[] { "moderately-irregular", ModeratelyIrregularInput(seed: 20260519, variantCount: 1) };
        }

        private static string RepositoryRoot()
        {
            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        }

        private static IEnumerable<string> Signatures(EngineOutput output)
        {
            return output.Variants.Select(v => string.Join(
                "|",
                v.VariantId,
                v.Metrics.Score.ToString("0.0000", CultureInfo.InvariantCulture),
                v.Units.Count.ToString(CultureInfo.InvariantCulture),
                v.Rooms.Count.ToString(CultureInfo.InvariantCulture),
                v.Corridors.Count.ToString(CultureInfo.InvariantCulture),
                v.Validation.Passed.ToString()));
        }

        private static Polygon2 ToPolygon(PolygonInput input)
        {
            List<Point2> points = input.Points.Select(p => p.Clone()).ToList();
            if (points.Count > 1 && points[0].EqualsWithin(points[points.Count - 1], 1e-9))
            {
                points.RemoveAt(points.Count - 1);
            }

            return new Polygon2(input.Id, points);
        }

        private static string SegmentKey(LineInput line)
        {
            return SegmentKey(new LineSegment2(line.Start, line.End));
        }

        private static string SegmentKey(LineSegment2 segment)
        {
            string first = PointKey(segment.Start);
            string second = PointKey(segment.End);
            return string.CompareOrdinal(first, second) <= 0 ? first + "|" + second : second + "|" + first;
        }

        private static string PointKey(Point2 point)
        {
            return Math.Round(point.X, 6).ToString("0.######", CultureInfo.InvariantCulture) + "," +
                Math.Round(point.Y, 6).ToString("0.######", CultureInfo.InvariantCulture);
        }

        private sealed class ThrowingTextReader : TextReader
        {
            public override string ReadToEnd()
            {
                throw new InvalidOperationException("This command should not read stdin.");
            }
        }

        private static EngineInput RectangularInput(int seed, int variantCount)
        {
            EngineInput input = new EngineInput();
            input.Project.Id = "rectangular-test";
            input.Project.Name = "Rectangular Test";
            input.Project.Seed = seed;
            input.Project.Tolerance = 0.01;

            input.Floorplate.Outer = new PolygonInput
            {
                Id = "outer",
                Points = new List<Point2>
                {
                    new Point2(0, 0),
                    new Point2(36, 0),
                    new Point2(36, 18),
                    new Point2(0, 18)
                }
            };

            input.Program.TargetUnitTypes = new List<UnitTypeTarget>
            {
                new UnitTypeTarget { Type = "studio", MinArea = 28.0, MaxArea = 58.0, TargetRatio = 0.40, Weight = 1.0 },
                new UnitTypeTarget { Type = "one_bed", MinArea = 48.0, MaxArea = 78.0, TargetRatio = 0.45, Weight = 1.0 },
                new UnitTypeTarget { Type = "two_bed", MinArea = 70.0, MaxArea = 108.0, TargetRatio = 0.15, Weight = 0.7 }
            };

            input.Rules.MinCorridorWidth = 1.8;
            input.Rules.MinRoomWidth = 2.4;
            input.Rules.MinRoomDepth = 2.4;
            input.Rules.MinUnitArea = 25.0;
            input.Rules.RequireDaylightForBedrooms = true;
            input.Rules.RequireDaylightForLiving = true;

            input.GenerationSettings.VariantCount = variantCount;
            input.GenerationSettings.Strictness = "balanced";
            input.GenerationSettings.WeightedVariation = true;
            return input;
        }

        [Fact]
        public void SingleDwellingMode_GeneratesRoomPlanFromScratchWithoutCorridorOrCore()
        {
            EngineInput input = SingleDwellingInput(seed: 4242, variantCount: 4, width: 8.4, depth: 6.4, type: "studio");
            EngineOutput output = new FloorPlanEngine().Generate(input);

            Assert.Equal("succeeded", output.Status);
            Assert.Equal(4, output.Variants.Count);
            Assert.All(output.Variants, v => Assert.True(v.Validation.Passed));
            foreach (LayoutVariant variant in output.Variants)
            {
                Assert.Empty(variant.Corridors);
                UnitLayout unit = Assert.Single(variant.Units);
                Assert.Equal(Math.Round(8.4 * 6.4, 4), unit.Area);

                // The 1RK program: one room, a separate kitchen, a bathroom.
                List<string> types = variant.Rooms.Select(r => r.RoomType).OrderBy(t => t).ToList();
                Assert.Contains("bathroom", types);
                Assert.Contains("kitchen", types);
                Assert.Contains("living_sleeping", types);

                // Watertight: rooms tile the dwelling exactly (per-room area values
                // are individually rounded, so the sum carries up to n*5e-5 noise).
                Assert.InRange(Math.Abs(unit.Area - variant.Rooms.Sum(r => r.Area)), 0.0, 0.001);
                Assert.All(variant.Walls, wall => Assert.True(
                    Math.Abs(wall.Centerline.Start.X - wall.Centerline.End.X) < 0.011 ||
                    Math.Abs(wall.Centerline.Start.Y - wall.Centerline.End.Y) < 0.011));

                // Entry door on the dwelling shell plus interior doors reaching every room.
                DoorOpening entry = variant.DoorsOpenings.FirstOrDefault(d => d.HostWall == "wall-entry-" + unit.Id);
                Assert.NotNull(entry);
                Assert.Contains(unit.Id, entry.ConnectsSpaces);
                HashSet<string> reachable = new HashSet<string>(
                    variant.DoorsOpenings.SelectMany(d => d.ConnectsSpaces),
                    System.StringComparer.OrdinalIgnoreCase);
                Assert.All(variant.Rooms, room => Assert.Contains(room.Id, reachable));
            }

            EngineOutput repeated = new FloorPlanEngine().Generate(SingleDwellingInput(4242, 4, 8.4, 6.4, "studio"));
            Assert.Equal(Signatures(output), Signatures(repeated));
        }

        [Fact]
        public void SingleDwellingMode_TwoBedProgramAndSeedVariety()
        {
            EngineInput input = SingleDwellingInput(seed: 99, variantCount: 4, width: 11.0, depth: 8.5, type: "two_bed");
            EngineOutput output = new FloorPlanEngine().Generate(input);

            Assert.Equal("succeeded", output.Status);
            Assert.All(output.Variants, v => Assert.True(v.Validation.Passed));
            foreach (LayoutVariant variant in output.Variants)
            {
                Assert.Equal(2, variant.Rooms.Count(r => r.RoomType == "bedroom"));
                Assert.Contains(variant.Rooms, r => r.RoomType == "living");
                Assert.All(
                    variant.Rooms.Where(r => r.RoomType == "bedroom" || r.RoomType == "living"),
                    room => Assert.True(room.Daylight, room.Id + " must have daylight."));
            }

            // Different seeds explore different schemes (entry side / splits /
            // mirroring). Coarse signatures stay equal (same counts, same score),
            // so the comparison must be geometric.
            EngineOutput other = new FloorPlanEngine().Generate(SingleDwellingInput(123456, 4, 11.0, 8.5, "two_bed"));
            Assert.NotEqual(DwellingGeometrySignature(output), DwellingGeometrySignature(other));
        }

        [Fact]
        public void SingleDwellingMode_HonorsExplicitBathroomCount()
        {
            EngineInput input = SingleDwellingInput(seed: 7, variantCount: 4, width: 12.0, depth: 9.0, type: "two_bed");
            input.Program.Dwelling.Bathrooms = 2;
            EngineOutput output = new FloorPlanEngine().Generate(input);

            Assert.Equal("succeeded", output.Status);
            Assert.All(output.Variants, v => Assert.True(v.Validation.Passed));
            foreach (LayoutVariant variant in output.Variants)
            {
                // The brief asked for a 2-bed, 2-bath: both wet rooms are present.
                Assert.Equal(2, variant.Rooms.Count(r => r.RoomType == "bathroom"));
                Assert.Equal(2, variant.Rooms.Count(r => r.RoomType == "bedroom"));

                // The extra wet room keeps the plan watertight and fully circulable.
                UnitLayout unit = Assert.Single(variant.Units);
                Assert.InRange(Math.Abs(unit.Area - variant.Rooms.Sum(r => r.Area)), 0.0, 0.001);
                HashSet<string> reachable = new HashSet<string>(
                    variant.DoorsOpenings.SelectMany(d => d.ConnectsSpaces),
                    System.StringComparer.OrdinalIgnoreCase);
                Assert.All(variant.Rooms, room => Assert.Contains(room.Id, reachable));
            }
        }

        [Fact]
        public void SingleDwellingMode_HonorsExtraRoomsFromProgram()
        {
            EngineInput input = SingleDwellingInput(seed: 31, variantCount: 4, width: 14.0, depth: 11.0, type: "two_bed");
            input.Program.Dwelling.Bathrooms = 2;
            input.Program.Dwelling.Study = 1;
            input.Program.Dwelling.Dining = 1;
            input.Program.Dwelling.Store = 1;
            input.Program.Dwelling.Pooja = 1;
            input.Program.Dwelling.Balcony = 1;
            EngineOutput output = new FloorPlanEngine().Generate(input);

            Assert.Equal("succeeded", output.Status);
            Assert.All(output.Variants, v => Assert.True(v.Validation.Passed));
            foreach (LayoutVariant variant in output.Variants)
            {
                List<string> types = variant.Rooms.Select(r => r.RoomType).ToList();
                Assert.Equal(2, types.Count(t => t == "bathroom"));
                Assert.Equal(2, types.Count(t => t == "bedroom"));
                Assert.Contains("study", types);
                Assert.Contains("dining", types);
                Assert.Contains("store", types);
                Assert.Contains("pooja", types);
                Assert.Contains("balcony", types);
                Assert.Contains(variant.Rooms, r => r.RoomType == "living");

                // Daylight rooms (bedrooms, living) keep their facade exposure even
                // with a balcony, and the plan stays watertight and circulable.
                Assert.All(
                    variant.Rooms.Where(r => r.RoomType == "bedroom" || r.RoomType == "living"),
                    room => Assert.True(room.Daylight, room.Id + " must have daylight."));
                UnitLayout unit = Assert.Single(variant.Units);
                Assert.InRange(Math.Abs(unit.Area - variant.Rooms.Sum(r => r.Area)), 0.0, 0.001);
                HashSet<string> reachable = new HashSet<string>(
                    variant.DoorsOpenings.SelectMany(d => d.ConnectsSpaces),
                    System.StringComparer.OrdinalIgnoreCase);
                Assert.All(variant.Rooms, room => Assert.Contains(room.Id, reachable));
            }
        }

        [Fact]
        public void SingleDwellingMode_HonorsExplicitKitchenAndLivingCounts()
        {
            EngineInput input = SingleDwellingInput(seed: 12, variantCount: 4, width: 13.0, depth: 9.5, type: "two_bed");
            input.Program.Dwelling.Kitchens = 2;
            input.Program.Dwelling.Livings = 2;
            EngineOutput output = new FloorPlanEngine().Generate(input);

            Assert.Equal("succeeded", output.Status);
            Assert.All(output.Variants, v => Assert.True(v.Validation.Passed));
            foreach (LayoutVariant variant in output.Variants)
            {
                // Two kitchens (a separate prep kitchen) and two living rooms.
                Assert.Equal(2, variant.Rooms.Count(r => r.RoomType == "kitchen"));
                Assert.Equal(2, variant.Rooms.Count(r => r.RoomType == "living"));
                Assert.Equal(2, variant.Rooms.Count(r => r.RoomType == "bedroom"));

                // Every living room keeps daylight; the plan tiles exactly and is
                // fully circulable.
                Assert.All(
                    variant.Rooms.Where(r => r.RoomType == "living"),
                    room => Assert.True(room.Daylight, room.Id + " must have daylight."));
                UnitLayout unit = Assert.Single(variant.Units);
                Assert.InRange(Math.Abs(unit.Area - variant.Rooms.Sum(r => r.Area)), 0.0, 0.002);
                HashSet<string> reachable = new HashSet<string>(
                    variant.DoorsOpenings.SelectMany(d => d.ConnectsSpaces),
                    System.StringComparer.OrdinalIgnoreCase);
                Assert.All(variant.Rooms, room => Assert.Contains(room.Id, reachable));
            }
        }

        [Fact]
        public void SingleDwellingMode_SnapsDaylightRoomsToGridModule()
        {
            // Plate dimensions are exact 0.6 m multiples (13.2 = 22x, 9.6 = 16x) so a
            // correctly snapped plan keeps every daylight-room edge on the module
            // regardless of which facade the entry maps onto.
            EngineInput input = SingleDwellingInput(seed: 21, variantCount: 4, width: 13.2, depth: 9.6, type: "two_bed");
            input.Rules.GridModule = 0.6;
            EngineOutput output = new FloorPlanEngine().Generate(input);

            Assert.Equal("succeeded", output.Status);
            Assert.All(output.Variants, v => Assert.True(v.Validation.Passed));

            string[] dayTypes = { "bedroom", "living", "living_sleeping", "study", "dining" };
            foreach (LayoutVariant variant in output.Variants)
            {
                foreach (RoomLayout room in variant.Rooms.Where(r => dayTypes.Contains(r.RoomType)))
                {
                    AssertOnGrid(room.Bounds.MinX, 0.6, room.Id + " minX");
                    AssertOnGrid(room.Bounds.MaxX, 0.6, room.Id + " maxX");
                }

                // Snapping moves only interior partitions, never the facade-facing band
                // edge, so every daylight room must keep its daylight after the snap.
                string[] needsDaylight = { "bedroom", "living", "living_sleeping" };
                Assert.All(
                    variant.Rooms.Where(r => needsDaylight.Contains(r.RoomType)),
                    room => Assert.True(room.Daylight, room.Id + " lost daylight after snapping."));

                // Snapping must not break watertight tiling or door reachability.
                UnitLayout unit = Assert.Single(variant.Units);
                Assert.InRange(Math.Abs(unit.Area - variant.Rooms.Sum(r => r.Area)), 0.0, 0.002);
                HashSet<string> reachable = new HashSet<string>(
                    variant.DoorsOpenings.SelectMany(d => d.ConnectsSpaces), System.StringComparer.OrdinalIgnoreCase);
                Assert.All(variant.Rooms, room => Assert.Contains(room.Id, reachable));
            }

            // Snapping stays deterministic for a given seed.
            EngineInput repeat = SingleDwellingInput(seed: 21, variantCount: 4, width: 13.2, depth: 9.6, type: "two_bed");
            repeat.Rules.GridModule = 0.6;
            Assert.Equal(DwellingGeometrySignature(output), DwellingGeometrySignature(new FloorPlanEngine().Generate(repeat)));
        }

        [Fact]
        public void SingleDwellingMode_FurnitureMinimums_GrowMoreRoomsToTheirShortSideMinimum()
        {
            // Furniture minimums are opt-in and best-effort: turning them on must align
            // strictly MORE rooms to their German short-side minimum than the identical
            // run with the flag off (mirrors Phase 0's honest "strictly more" oracle —
            // a drift-absorbing or starved room may still miss its target, so asserting
            // every room meets its minimum would be a false-fail).
            EngineInput off = FurnitureDwellingInput(seed: 31, width: 9.6, depth: 9.0, type: "two_bed", furniture: false);
            EngineInput on = FurnitureDwellingInput(seed: 31, width: 9.6, depth: 9.0, type: "two_bed", furniture: true);

            EngineOutput offOut = new FloorPlanEngine().Generate(off);
            EngineOutput onOut = new FloorPlanEngine().Generate(on);

            Assert.Equal("succeeded", onOut.Status);
            Assert.All(onOut.Variants, v => Assert.True(v.Validation.Passed));

            Assert.True(
                CountRoomsMeetingFurnitureMin(onOut) > CountRoomsMeetingFurnitureMin(offOut),
                "Furniture on met " + CountRoomsMeetingFurnitureMin(onOut) + " short-side minima vs " +
                CountRoomsMeetingFurnitureMin(offOut) + " off: no improvement.");

            // Best-effort must not break watertight tiling, daylight, or reachability.
            foreach (LayoutVariant variant in onOut.Variants)
            {
                UnitLayout unit = Assert.Single(variant.Units);
                Assert.InRange(Math.Abs(unit.Area - variant.Rooms.Sum(r => r.Area)), 0.0, 0.002);
                string[] needsDaylight = { "bedroom", "living", "living_sleeping" };
                Assert.All(
                    variant.Rooms.Where(r => needsDaylight.Contains(r.RoomType)),
                    room => Assert.True(room.Daylight, room.Id + " lost daylight."));
                HashSet<string> reachable = new HashSet<string>(
                    variant.DoorsOpenings.SelectMany(d => d.ConnectsSpaces), System.StringComparer.OrdinalIgnoreCase);
                Assert.All(variant.Rooms, room => Assert.Contains(room.Id, reachable));
            }
        }

        [Fact]
        public void SingleDwellingMode_FurnitureMinimums_ChangeGeometryWhenEnabledAndStayDeterministic()
        {
            // The flag must actually move geometry (proves it is wired in) and stay
            // byte-identical across repeated runs of the same seed (Phase 0 parity).
            EngineInput off = FurnitureDwellingInput(seed: 31, width: 9.6, depth: 9.0, type: "two_bed", furniture: false);
            EngineInput on = FurnitureDwellingInput(seed: 31, width: 9.6, depth: 9.0, type: "two_bed", furniture: true);

            string offSig = DwellingGeometrySignature(new FloorPlanEngine().Generate(off));
            string onSig = DwellingGeometrySignature(new FloorPlanEngine().Generate(on));
            Assert.NotEqual(offSig, onSig);

            EngineInput onRepeat = FurnitureDwellingInput(seed: 31, width: 9.6, depth: 9.0, type: "two_bed", furniture: true);
            Assert.Equal(onSig, DwellingGeometrySignature(new FloorPlanEngine().Generate(onRepeat)));
        }

        [Fact]
        public void MultiUnitMode_FurnitureMinimums_GrowMoreRoomsToTheirShortSideMinimum()
        {
            EngineInput off = RectangularInput(seed: 1234, variantCount: 4);
            off.Rules.ApplyFurnitureMinimums = false;
            EngineInput on = RectangularInput(seed: 1234, variantCount: 4);
            on.Rules.ApplyFurnitureMinimums = true;

            EngineOutput offOut = new FloorPlanEngine().Generate(off);
            EngineOutput onOut = new FloorPlanEngine().Generate(on);

            Assert.Equal("succeeded", onOut.Status);
            Assert.All(onOut.Variants, v => Assert.True(v.Validation.Passed));

            Assert.True(
                CountRoomsMeetingFurnitureMin(onOut) > CountRoomsMeetingFurnitureMin(offOut),
                "Furniture on met " + CountRoomsMeetingFurnitureMin(onOut) + " short-side minima vs " +
                CountRoomsMeetingFurnitureMin(offOut) + " off: no improvement.");

            foreach (LayoutVariant variant in onOut.Variants)
            {
                foreach (UnitLayout unit in variant.Units)
                {
                    double roomSum = variant.Rooms.Where(r => r.UnitId == unit.Id).Sum(r => r.Area);
                    Assert.InRange(Math.Abs(unit.Area - roomSum), 0.0, 0.002);
                }

                string[] needsDaylight = { "bedroom", "living", "living_sleeping" };
                Assert.All(
                    variant.Rooms.Where(r => needsDaylight.Contains(r.RoomType)),
                    room => Assert.True(room.Daylight, room.Id + " lost daylight."));
            }

            EngineInput onRepeat = RectangularInput(seed: 1234, variantCount: 4);
            onRepeat.Rules.ApplyFurnitureMinimums = true;
            Assert.Equal(Signatures(onOut), Signatures(new FloorPlanEngine().Generate(onRepeat)));
        }

        [Fact]
        public void MultiUnitMode_FurnitureMinimumsOff_GeometryIsByteIdenticalToFrozenBaseline()
        {
            // Byte-identity contract: with ApplyFurnitureMinimums off, the multi-unit
            // path must emit the exact full-precision geometry of the frozen baseline —
            // the pre-Phase-1 engine, plus the two_bed unit-type normalization fix that
            // gives canonical "two_bed" units their second bedroom. This pins a hash of
            // every room polygon vertex at round-trip ("R") precision (not the 4-dp
            // signatures), so even a sub-micron boundary drift on the opted-out path
            // fails loudly — a tighter guard than the golden fixtures, covering the
            // whole reachable multi-unit render.
            EngineInput off = RectangularInput(seed: 1234, variantCount: 4);
            off.Rules.ApplyFurnitureMinimums = false;

            EngineOutput output = new FloorPlanEngine().Generate(off);

            Assert.Equal("succeeded", output.Status);
            Assert.Equal(
                "450759EF1EF063369FDDA10BDD7CC154AD7042DCD60B0DAD558BB15BD68E198A",
                FullPrecisionGeometryHash(output));
        }

        // SHA-256 over every room polygon vertex at round-trip ("R") precision, so the
        // hash flips on any sub-micron coordinate change. Used to pin the opted-out
        // multi-unit path to byte-identical historic geometry.
        private static string FullPrecisionGeometryHash(EngineOutput output)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (LayoutVariant variant in output.Variants)
            {
                sb.Append(variant.VariantId).Append('#');
                foreach (RoomLayout room in variant.Rooms)
                {
                    sb.Append(room.Id).Append(':').Append(room.RoomType).Append('[');
                    foreach (Point2 p in room.Polygon.Points)
                    {
                        sb.Append(p.X.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                          .Append(p.Y.ToString("R", CultureInfo.InvariantCulture)).Append(';');
                    }

                    sb.Append(']');
                }
            }

            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(sb.ToString()));
                return Convert.ToHexString(hash);
            }
        }

        [Fact]
        public void SingleDwellingMode_FurnitureMinimums_CapPullsOverLongRoomsTowardTheirAspectLimit()
        {
            // A wide (16 m) but shallow (4.6 m) plate: every day room is already well
            // above its min width, so the min-growth pass is a no-op and the aspect cap
            // is the ONLY thing the flag changes here. The shallow depth makes the long
            // rooms exceed their long:short cap, so turning the flag on must reduce the
            // total aspect overshoot (best-effort: strictly less, not necessarily zero).
            EngineInput off = FurnitureDwellingInput(seed: 7, width: 16.0, depth: 4.6, type: "two_bed", furniture: false);
            EngineInput on = FurnitureDwellingInput(seed: 7, width: 16.0, depth: 4.6, type: "two_bed", furniture: true);

            EngineOutput offOut = new FloorPlanEngine().Generate(off);
            EngineOutput onOut = new FloorPlanEngine().Generate(on);

            Assert.Equal("succeeded", onOut.Status);
            Assert.All(onOut.Variants, v => Assert.True(v.Validation.Passed));

            double offOver = TotalAspectOvershoot(offOut);
            double onOver = TotalAspectOvershoot(onOut);
            Assert.True(onOver < offOver - 1e-6,
                "aspect cap did not bind: on overshoot " + onOver + " vs off " + offOver);
        }

        [Fact]
        public void SingleDwellingMode_FurnitureMinimums_BalconyDayBandStaysValidWithPerRoomAspectCap()
        {
            // Forces the balcony day-band path (balcony > 0), where rooms fronting a
            // balcony are emitted shallower than the rest of the band. The per-room
            // aspect cap must use each room's true depth there yet keep the band
            // watertight, daylit, reachable and deterministic.
            EngineInput input = SingleDwellingInput(seed: 11, variantCount: 4, width: 14.0, depth: 6.4, type: "two_bed");
            input.Program.Dwelling = new DwellingProgram { Bedrooms = 2, Livings = 1, Balcony = 1 };
            input.Rules.ApplyFurnitureMinimums = true;

            EngineOutput output = new FloorPlanEngine().Generate(input);

            Assert.Equal("succeeded", output.Status);
            Assert.All(output.Variants, v => Assert.True(v.Validation.Passed));
            Assert.Contains(output.Variants.SelectMany(v => v.Rooms), r => r.RoomType == "balcony");

            foreach (LayoutVariant variant in output.Variants)
            {
                foreach (UnitLayout unit in variant.Units)
                {
                    double roomSum = variant.Rooms.Where(r => r.UnitId == unit.Id).Sum(r => r.Area);
                    Assert.InRange(Math.Abs(unit.Area - roomSum), 0.0, 0.01);
                }
            }

            EngineInput repeat = SingleDwellingInput(seed: 11, variantCount: 4, width: 14.0, depth: 6.4, type: "two_bed");
            repeat.Program.Dwelling = new DwellingProgram { Bedrooms = 2, Livings = 1, Balcony = 1 };
            repeat.Rules.ApplyFurnitureMinimums = true;
            Assert.Equal(
                string.Join("|", Signatures(output)),
                string.Join("|", Signatures(new FloorPlanEngine().Generate(repeat))));
        }

        // Sum over every typed room of how far its long:short proportion exceeds the
        // German furniture aspect cap for its type (0 when within the cap). A best-effort
        // cap should lower this total versus the uncapped layout.
        private static double TotalAspectOvershoot(EngineOutput output)
        {
            var maxAspect = new Dictionary<string, double>(System.StringComparer.OrdinalIgnoreCase)
            {
                { "bedroom", 1.7 }, { "living", 1.8 }, { "living_sleeping", 2.2 }, { "dining", 1.6 },
                { "study", 2.0 }, { "kitchen", 2.6 }, { "bathroom", 2.0 }, { "utility", 2.0 },
                { "balcony", 2.5 }, { "foyer", 3.0 }, { "pooja", 1.8 }, { "store", 2.2 },
            };
            double total = 0.0;
            foreach (RoomLayout room in output.Variants.SelectMany(v => v.Rooms))
            {
                if (!maxAspect.TryGetValue(room.RoomType, out double cap)) continue;
                double w = room.Dimensions.Width;
                double d = room.Dimensions.Depth;
                if (w <= 0.0 || d <= 0.0) continue;
                double ratio = Math.Max(w, d) / Math.Min(w, d);
                if (ratio > cap) total += ratio - cap;
            }

            return total;
        }

        // Counts rooms whose SHORT side (min of the two plan dimensions, which is the
        // band-redistributed axis regardless of how the entry facade maps it onto X/Y)
        // reaches the German furniture minimum width for its type.
        private static int CountRoomsMeetingFurnitureMin(EngineOutput output)
        {
            var minShortSide = new Dictionary<string, double>(System.StringComparer.OrdinalIgnoreCase)
            {
                { "bedroom", 3.0 }, { "living", 3.4 }, { "living_sleeping", 3.4 }, { "dining", 2.6 },
                { "study", 2.4 }, { "kitchen", 1.8 }, { "bathroom", 1.6 }, { "utility", 1.4 },
                { "foyer", 1.2 }, { "pooja", 0.9 }, { "store", 0.8 },
            };
            int met = 0;
            foreach (RoomLayout room in output.Variants.SelectMany(v => v.Rooms))
            {
                if (!minShortSide.TryGetValue(room.RoomType, out double min)) continue;
                double shortSide = Math.Min(room.Dimensions.Width, room.Dimensions.Depth);
                if (shortSide + 0.002 >= min) met++;
            }

            return met;
        }

        private static EngineInput FurnitureDwellingInput(int seed, double width, double depth, string type, bool furniture)
        {
            EngineInput input = SingleDwellingInput(seed, variantCount: 4, width: width, depth: depth, type: type);
            input.Rules.ApplyFurnitureMinimums = furniture;
            return input;
        }

        [Fact]
        public void MultiUnitMode_SnapsUnitBaysToGridModule()
        {
            // The plate spans exact 0.6 m multiples on both axes (36 = 60x, 18 = 30x),
            // so the unit band runs from one grid line to another. Every interior bay
            // division then lands on the module, leaving the whole bay axis gridded.
            EngineInput input = RectangularInput(seed: 1234, variantCount: 4);
            input.Rules.GridModule = 0.6;
            EngineOutput output = new FloorPlanEngine().Generate(input);

            Assert.Equal("succeeded", output.Status);
            Assert.All(output.Variants, v => Assert.True(v.Validation.Passed));

            foreach (LayoutVariant variant in output.Variants)
            {
                Assert.NotEmpty(variant.Units);
                foreach (UnitLayout unit in variant.Units)
                {
                    // Units tile along one axis (the bay axis); that axis runs the full
                    // grid-aligned plate span, so both of its bounds snap to the module.
                    // The cross axis is bounded by the off-grid corridor, so accepting
                    // either axis being fully gridded keeps the test orientation-agnostic.
                    bool xGridded = IsOnGrid(unit.Bounds.MinX, 0.6) && IsOnGrid(unit.Bounds.MaxX, 0.6);
                    bool yGridded = IsOnGrid(unit.Bounds.MinY, 0.6) && IsOnGrid(unit.Bounds.MaxY, 0.6);
                    Assert.True(
                        xGridded || yGridded,
                        unit.Id + " bay bounds x[" +
                        unit.Bounds.MinX.ToString("0.####", CultureInfo.InvariantCulture) + "," +
                        unit.Bounds.MaxX.ToString("0.####", CultureInfo.InvariantCulture) + "] y[" +
                        unit.Bounds.MinY.ToString("0.####", CultureInfo.InvariantCulture) + "," +
                        unit.Bounds.MaxY.ToString("0.####", CultureInfo.InvariantCulture) + "] are off the 0.6 m grid on both axes.");
                }
            }

            // Snapping the bays stays deterministic for a given seed.
            EngineInput repeat = RectangularInput(seed: 1234, variantCount: 4);
            repeat.Rules.GridModule = 0.6;
            Assert.Equal(Signatures(output), Signatures(new FloorPlanEngine().Generate(repeat)));
        }

        [Fact]
        public void MultiUnitMode_SnapsRoomPartitionsToGridModule()
        {
            // Best-effort snapping: a room partition only moves onto the module when
            // doing so keeps both sides above the minimum room size, so snapping is
            // intentionally skipped inside narrow units (e.g. a 5 m remainder bay).
            // The honest invariant is therefore "turning the grid on aligns strictly
            // more room boundaries than the identical gridless plan", proven alongside
            // watertight tiling, passing validation, and determinism.
            EngineInput input = RectangularInput(seed: 1234, variantCount: 4);
            input.Rules.GridModule = 0.6;
            EngineOutput output = new FloorPlanEngine().Generate(input);

            Assert.Equal("succeeded", output.Status);
            Assert.All(output.Variants, v => Assert.True(v.Validation.Passed));

            foreach (LayoutVariant variant in output.Variants)
            {
                Assert.NotEmpty(variant.Rooms);

                // Watertight: rooms still tile each unit exactly after the snap.
                foreach (UnitLayout unit in variant.Units)
                {
                    double roomSum = variant.Rooms.Where(r => r.UnitId == unit.Id).Sum(r => r.Area);
                    Assert.InRange(Math.Abs(unit.Area - roomSum), 0.0, 0.002);
                }
            }

            int griddedAligned = CountGridAlignedRoomAxes(output, 0.6);
            int gridlessAligned = CountGridAlignedRoomAxes(
                new FloorPlanEngine().Generate(RectangularInput(seed: 1234, variantCount: 4)), 0.6);
            Assert.True(
                griddedAligned > gridlessAligned,
                "Grid snapping aligned " + griddedAligned + " room axes vs " + gridlessAligned + " gridless: no improvement.");

            // Room snapping stays deterministic for a given seed.
            EngineInput repeat = RectangularInput(seed: 1234, variantCount: 4);
            repeat.Rules.GridModule = 0.6;
            Assert.Equal(Signatures(output), Signatures(new FloorPlanEngine().Generate(repeat)));
        }

        // Counts rooms whose bay-axis bound pair (the axis spanning the grid-aligned
        // plate width) lands on the module. The cross axis is bounded by the off-grid
        // corridor, so a single aligned axis is the signal that snapping took effect.
        private static int CountGridAlignedRoomAxes(EngineOutput output, double module)
        {
            int aligned = 0;
            foreach (RoomLayout room in output.Variants.SelectMany(v => v.Rooms))
            {
                bool xGridded = IsOnGrid(room.Bounds.MinX, module) && IsOnGrid(room.Bounds.MaxX, module);
                bool yGridded = IsOnGrid(room.Bounds.MinY, module) && IsOnGrid(room.Bounds.MaxY, module);
                if (xGridded || yGridded)
                {
                    aligned++;
                }
            }

            return aligned;
        }

        [Fact]
        public void SingleDwellingMode_SnapsWetBandPartitionsToGridModule()
        {
            // A multi-service wet band (2 baths, pooja, kitchen) exercises the general
            // wet path, which lays vertical partitions left-to-right. The band runs the
            // full grid-aligned plate width (18 = 30x0.6) and the plate is wide enough
            // that every wet room clears its minimum after snapping, so — like the day
            // band — every wet-room bay edge must land on the module.
            EngineInput input = WetProgramDwellingInput(gridModule: 0.6);
            EngineOutput output = new FloorPlanEngine().Generate(input);

            Assert.Equal("succeeded", output.Status);
            Assert.All(output.Variants, v => Assert.True(v.Validation.Passed));

            string[] wet = { "bathroom", "kitchen", "foyer", "pooja", "store", "utility" };
            foreach (LayoutVariant variant in output.Variants)
            {
                foreach (RoomLayout room in variant.Rooms.Where(r => wet.Contains(r.RoomType)))
                {
                    AssertOnGrid(room.Bounds.MinX, 0.6, room.Id + " minX");
                    AssertOnGrid(room.Bounds.MaxX, 0.6, room.Id + " maxX");
                }

                UnitLayout unit = Assert.Single(variant.Units);
                Assert.InRange(Math.Abs(unit.Area - variant.Rooms.Sum(r => r.Area)), 0.0, 0.002);
            }

            // Deterministic for a given seed.
            Assert.Equal(
                DwellingGeometrySignature(output),
                DwellingGeometrySignature(new FloorPlanEngine().Generate(WetProgramDwellingInput(gridModule: 0.6))));
        }

        private static EngineInput WetProgramDwellingInput(double gridModule)
        {
            EngineInput input = SingleDwellingInput(seed: 7, variantCount: 4, width: 18.0, depth: 9.6, type: "two_bed");
            input.Rules.GridModule = gridModule;
            input.Program.Dwelling = new DwellingProgram { Bathrooms = 2, Kitchens = 1, Pooja = 1 };
            return input;
        }

        private static bool IsOnGrid(double value, double module)
        {
            return Math.Abs(value - (Math.Round(value / module) * module)) <= 0.002;
        }

        private static void AssertOnGrid(double value, double module, string label)
        {
            double remainder = Math.Abs(value - (Math.Round(value / module) * module));
            Assert.True(
                remainder <= 0.002,
                label + " = " + value.ToString("0.####", CultureInfo.InvariantCulture) + " is off the " + module + " m grid.");
        }

        private static string DwellingGeometrySignature(EngineOutput output)
        {
            return string.Join(";", output.Variants.SelectMany(v => v.Rooms).Select(r =>
                r.Id + ":" +
                r.Polygon.Points[0].X.ToString("0.####", CultureInfo.InvariantCulture) + "," +
                r.Polygon.Points[0].Y.ToString("0.####", CultureInfo.InvariantCulture) + "," +
                r.Dimensions.Width.ToString("0.####", CultureInfo.InvariantCulture) + "x" +
                r.Dimensions.Depth.ToString("0.####", CultureInfo.InvariantCulture)));
        }

        private static EngineInput SingleDwellingInput(int seed, int variantCount, double width, double depth, string type)
        {
            EngineInput input = RectangularInput(seed, variantCount);
            input.Project.Id = "single-dwelling-test";
            input.Project.Name = "Single Dwelling Test";
            input.Floorplate.Outer = new PolygonInput
            {
                Id = "dwelling-outer",
                Points = new List<Point2>
                {
                    new Point2(0, 0),
                    new Point2(width, 0),
                    new Point2(width, depth),
                    new Point2(0, depth)
                }
            };
            input.FixedElements = new List<FixedElementInput>();
            input.Access.VerticalCoreAccess = new List<Point2>();
            input.GenerationSettings.LayoutMode = "single_dwelling";
            input.Rules.MinRoomWidth = 1.8;
            input.Rules.MinRoomDepth = 1.8;
            input.Rules.MinUnitArea = 20.0;
            input.Program.TargetUnitTypes = new List<UnitTypeTarget>
            {
                new UnitTypeTarget { Type = type, MinArea = 24.0, MaxArea = 110.0, TargetCount = 1, Weight = 1.0 }
            };
            return input;
        }

        [Fact]
        public void ExclusiveUnitMixIsHonored_ZeroRatioTypesAreDeliberateExclusions()
        {
            // Large plate: bays comfortably fit the demanded type.
            AssertExclusiveTwoBedMixHonored(RectangularInput(seed: 31, variantCount: 4));

            // Shallow-band plate (the AI-brief case that failed live): the ~8 m
            // probe-bay area (8 x 5.25 = 42 m2) undershoots two_bed's candidacy
            // floor (72 x 0.75 = 54 m2) although the full 24 m interval hosts one
            // easily, so the demanded type silently vanished from every bay.
            EngineInput shallow = RectangularInput(seed: 77, variantCount: 4);
            shallow.Project.Id = "exclusive-mix-shallow";
            shallow.Floorplate.Outer = new PolygonInput
            {
                Id = "shallow-outer",
                Points = new List<Point2>
                {
                    new Point2(0, 0),
                    new Point2(24, 0),
                    new Point2(24, 12),
                    new Point2(0, 12)
                }
            };
            shallow.FixedElements = new List<FixedElementInput>();
            shallow.Access.VerticalCoreAccess = new List<Point2>();
            AssertExclusiveTwoBedMixHonored(shallow);
        }

        private static void AssertExclusiveTwoBedMixHonored(EngineInput input)
        {
            input.Program.TargetUnitTypes = new List<UnitTypeTarget>
            {
                new UnitTypeTarget { Type = "studio", MinArea = 32.0, MaxArea = 52.0, TargetRatio = 0.0, Weight = 1.0 },
                new UnitTypeTarget { Type = "one_bed", MinArea = 50.0, MaxArea = 76.0, TargetRatio = 0.0, Weight = 1.0 },
                new UnitTypeTarget { Type = "two_bed", MinArea = 72.0, MaxArea = 105.0, TargetRatio = 1.0, Weight = 1.0 }
            };

            EngineOutput output = new FloorPlanEngine().Generate(input);

            Assert.Equal("succeeded", output.Status);
            foreach (LayoutVariant variant in output.Variants)
            {
                int twoBed = variant.Units.Count(u => string.Equals(u.Type, "two_bed", System.StringComparison.OrdinalIgnoreCase));
                int other = variant.Units.Count - twoBed;
                Assert.True(
                    twoBed > other,
                    input.Project.Id + " " + variant.VariantId + " placed " + twoBed + " two_bed vs " + other +
                    " excluded-type units under a 100% two_bed mix.");
            }
        }

        private static EngineInput LShapedInput(int seed, int variantCount)
        {
            EngineInput input = RectangularInput(seed, variantCount);
            input.Project.Id = "l-shaped-test";
            input.Project.Name = "L-Shaped Test";
            input.Floorplate.Outer = new PolygonInput
            {
                Id = "l-shaped-outer",
                Points = new List<Point2>
                {
                    new Point2(0, 0),
                    new Point2(44, 0),
                    new Point2(44, 18),
                    new Point2(28, 18),
                    new Point2(28, 30),
                    new Point2(0, 30)
                }
            };

            input.FixedElements = new List<FixedElementInput>
            {
                new FixedElementInput
                {
                    Id = "core-l1",
                    Type = "core",
                    BlocksGeneration = true,
                    Polygon = new PolygonInput
                    {
                        Id = "core-l1",
                        Points = new List<Point2>
                        {
                            new Point2(18, 8),
                            new Point2(24, 8),
                            new Point2(24, 14),
                            new Point2(18, 14)
                        }
                    }
                }
            };

            input.Access.VerticalCoreAccess = new List<Point2> { new Point2(21, 14) };
            input.GenerationSettings.WeightedVariation = true;
            return input;
        }

        private static EngineInput ModeratelyIrregularInput(int seed, int variantCount)
        {
            EngineInput input = RectangularInput(seed, variantCount);
            input.Project.Id = "moderately-irregular-test";
            input.Project.Name = "Moderately Irregular Test";
            input.Floorplate.Outer = new PolygonInput
            {
                Id = "moderately-irregular-outer",
                Points = new List<Point2>
                {
                    new Point2(0, 0),
                    new Point2(52, 0),
                    new Point2(52, 16),
                    new Point2(46, 16),
                    new Point2(46, 28),
                    new Point2(34, 28),
                    new Point2(34, 34),
                    new Point2(0, 34)
                }
            };

            input.FixedElements = new List<FixedElementInput>
            {
                new FixedElementInput
                {
                    Id = "core-ir1",
                    Type = "core",
                    BlocksGeneration = true,
                    Polygon = new PolygonInput
                    {
                        Id = "core-ir1",
                        Points = new List<Point2>
                        {
                            new Point2(22, 12),
                            new Point2(28, 12),
                            new Point2(28, 18),
                            new Point2(22, 18)
                        }
                    }
                }
            };

            input.Access.VerticalCoreAccess = new List<Point2> { new Point2(22, 18) };
            input.Program.TargetUnitTypes = new List<UnitTypeTarget>
            {
                new UnitTypeTarget { Type = "studio", MinArea = 30.0, MaxArea = 95.0, TargetRatio = 0.30, Weight = 1.0 },
                new UnitTypeTarget { Type = "one_bed", MinArea = 55.0, MaxArea = 150.0, TargetRatio = 0.50, Weight = 1.0 },
                new UnitTypeTarget { Type = "two_bed", MinArea = 80.0, MaxArea = 190.0, TargetRatio = 0.20, Weight = 0.8 }
            };
            return input;
        }
    }
}
