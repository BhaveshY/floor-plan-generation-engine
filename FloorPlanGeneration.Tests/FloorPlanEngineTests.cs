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
