using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using FloorPlanGeneration.Geometry;

namespace FloorPlanGeneration.Topology
{
    public sealed class TopologyGraph
    {
        public TopologyGraph()
        {
            Nodes = new List<SpaceNode>();
            Edges = new List<AdjacencyEdge>();
            Hypergraph = new FloorPlanHypergraph();
        }

        public List<SpaceNode> Nodes { get; set; }
        public List<AdjacencyEdge> Edges { get; set; }
        public FloorPlanHypergraph Hypergraph { get; set; }

        public void AddNode(string id, string kind, string referenceId, string parentId = "")
        {
            Nodes.Add(new SpaceNode
            {
                Id = id,
                Kind = kind,
                ReferenceId = referenceId,
                ParentId = parentId
            });
        }

        public void AddEdge(string from, string to, string kind, string reason = "")
        {
            Edges.Add(new AdjacencyEdge
            {
                From = from,
                To = to,
                Kind = kind,
                Reason = reason
            });
        }
    }

    public sealed class SpaceNode
    {
        public SpaceNode()
        {
            Id = string.Empty;
            ExternalId = string.Empty;
            Kind = string.Empty;
            ReferenceId = string.Empty;
            ParentId = string.Empty;
        }

        public string Id { get; set; }
        public string ExternalId { get; set; }
        public string Kind { get; set; }
        public string ReferenceId { get; set; }
        public string ParentId { get; set; }
    }

    public sealed class AdjacencyEdge
    {
        public AdjacencyEdge()
        {
            ExternalId = string.Empty;
            From = string.Empty;
            To = string.Empty;
            Kind = string.Empty;
            Reason = string.Empty;
        }

        public string ExternalId { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public string Kind { get; set; }
        public string Reason { get; set; }
    }

    public sealed class FloorPlanHypergraph
    {
        public FloorPlanHypergraph()
        {
            SchemaVersion = "hypergraph-floorplan-1.0";
            Source = "BhaveshY/hypergraph portable DataNode contract";
            Root = new HypergraphDataNode();
            Nodes = new List<HypergraphNode>();
            Hyperedges = new List<Hyperedge>();
            Incidence = new List<HypergraphIncidence>();
            Matrices = new HypergraphMatrices();
        }

        public string SchemaVersion { get; set; }
        public string Source { get; set; }
        public HypergraphDataNode Root { get; set; }
        public List<HypergraphNode> Nodes { get; set; }
        public List<Hyperedge> Hyperedges { get; set; }
        public List<HypergraphIncidence> Incidence { get; set; }
        public HypergraphMatrices Matrices { get; set; }
    }

    public sealed class HypergraphDataNode
    {
        public HypergraphDataNode()
        {
            Name = string.Empty;
            MergeId = string.Empty;
            Children = new List<HypergraphDataNode>();
            Connected = new List<string>();
            TreeNodeMesh = new HypergraphTreeNodeMesh();
        }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("area")]
        public double Area { get; set; }

        [JsonPropertyName("angle")]
        public double Angle { get; set; }

        [JsonPropertyName("mergeid")]
        public string MergeId { get; set; }

        [JsonPropertyName("final")]
        public bool Final { get; set; }

        [JsonPropertyName("children")]
        public List<HypergraphDataNode> Children { get; set; }

        [JsonPropertyName("connected")]
        public List<string> Connected { get; set; }

        [JsonPropertyName("treeNodeMesh")]
        public HypergraphTreeNodeMesh TreeNodeMesh { get; set; }
    }

    public sealed class HypergraphTreeNodeMesh
    {
        public HypergraphTreeNodeMesh()
        {
            Centroid = new HypergraphCentroid();
        }

        [JsonPropertyName("Area")]
        public double Area { get; set; }

        [JsonPropertyName("Centroid")]
        public HypergraphCentroid Centroid { get; set; }
    }

    public sealed class HypergraphCentroid
    {
        [JsonPropertyName("X")]
        public double X { get; set; }

        [JsonPropertyName("Y")]
        public double Y { get; set; }

        [JsonPropertyName("Z")]
        public double Z { get; set; }

        [JsonPropertyName("Mag")]
        public double Mag { get; set; }
    }

    public sealed class HypergraphNode
    {
        public HypergraphNode()
        {
            Id = string.Empty;
            ExternalId = string.Empty;
            Kind = string.Empty;
            ReferenceId = string.Empty;
            ParentId = string.Empty;
            MergeId = string.Empty;
            Children = new List<string>();
            Connected = new List<string>();
            Centroid = new Point2();
            Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public string Id { get; set; }
        public string ExternalId { get; set; }
        public string Kind { get; set; }
        public string ReferenceId { get; set; }
        public string ParentId { get; set; }
        public string MergeId { get; set; }
        public bool Final { get; set; }
        public int Level { get; set; }
        public double Area { get; set; }
        public double Angle { get; set; }
        public Point2 Centroid { get; set; }
        public List<string> Children { get; set; }
        public List<string> Connected { get; set; }
        public Dictionary<string, string> Attributes { get; set; }
    }

    public sealed class Hyperedge
    {
        public Hyperedge()
        {
            Id = string.Empty;
            ExternalId = string.Empty;
            Kind = string.Empty;
            Members = new List<HyperedgeMember>();
            Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public string Id { get; set; }
        public string ExternalId { get; set; }
        public string Kind { get; set; }
        public List<HyperedgeMember> Members { get; set; }
        public double Weight { get; set; }
        public Dictionary<string, string> Attributes { get; set; }
    }

    public sealed class HyperedgeMember
    {
        public HyperedgeMember()
        {
            NodeId = string.Empty;
            Role = string.Empty;
        }

        public string NodeId { get; set; }
        public string Role { get; set; }
    }

    public sealed class HypergraphIncidence
    {
        public HypergraphIncidence()
        {
            Id = string.Empty;
            ExternalId = string.Empty;
            NodeId = string.Empty;
            HyperedgeId = string.Empty;
            Role = string.Empty;
        }

        public string Id { get; set; }
        public string ExternalId { get; set; }
        public string NodeId { get; set; }
        public string HyperedgeId { get; set; }
        public string Role { get; set; }
        public double Weight { get; set; }
    }

    public sealed class HypergraphMatrices
    {
        public HypergraphMatrices()
        {
            NodeOrder = new List<string>();
            HyperedgeOrder = new List<string>();
            SubdivisionConnectivity = new List<List<double>>();
            AdjacencyConnectivity = new List<List<double>>();
            Area = new List<List<double>>();
            Angle = new List<List<double>>();
            Incidence = new List<List<double>>();
        }

        public List<string> NodeOrder { get; set; }
        public List<string> HyperedgeOrder { get; set; }
        public List<List<double>> SubdivisionConnectivity { get; set; }
        public List<List<double>> AdjacencyConnectivity { get; set; }
        public List<List<double>> Area { get; set; }
        public List<List<double>> Angle { get; set; }
        public List<List<double>> Incidence { get; set; }
    }

    public static class HypergraphBuilder
    {
        public static List<HypergraphDataNode> FlattenDataNodes(HypergraphDataNode root)
        {
            List<HypergraphDataNode> result = new List<HypergraphDataNode>();
            AddDataNode(root, result);
            return result;
        }

        public static List<string> Validate(FloorPlanHypergraph hypergraph)
        {
            List<string> errors = new List<string>();
            if (hypergraph == null)
            {
                errors.Add("Hypergraph is missing.");
                return errors;
            }

            if (hypergraph.Root == null || string.IsNullOrWhiteSpace(hypergraph.Root.Name))
            {
                errors.Add("Hypergraph root is missing.");
            }

            List<HypergraphNode> nodes = hypergraph.Nodes ?? new List<HypergraphNode>();
            List<Hyperedge> hyperedges = hypergraph.Hyperedges ?? new List<Hyperedge>();
            List<HypergraphIncidence> incidence = hypergraph.Incidence ?? new List<HypergraphIncidence>();

            HashSet<string> nodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (HypergraphNode node in nodes)
            {
                if (string.IsNullOrWhiteSpace(node.Id))
                {
                    errors.Add("Hypergraph node id is empty.");
                    continue;
                }

                if (!nodeIds.Add(node.Id))
                {
                    errors.Add("Duplicate hypergraph node id: " + node.Id);
                }
            }

            HashSet<string> edgeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Hyperedge edge in hyperedges)
            {
                if (string.IsNullOrWhiteSpace(edge.Id))
                {
                    errors.Add("Hyperedge id is empty.");
                    continue;
                }

                if (!edgeIds.Add(edge.Id))
                {
                    errors.Add("Duplicate hyperedge id: " + edge.Id);
                }

                if (edge.Members == null || edge.Members.Count < 2)
                {
                    errors.Add("Hyperedge " + edge.Id + " must contain at least two members.");
                }
            }

            foreach (HypergraphIncidence item in incidence)
            {
                if (!nodeIds.Contains(item.NodeId))
                {
                    errors.Add("Incidence " + item.Id + " references unknown node " + item.NodeId + ".");
                }

                if (!edgeIds.Contains(item.HyperedgeId))
                {
                    errors.Add("Incidence " + item.Id + " references unknown hyperedge " + item.HyperedgeId + ".");
                }
            }

            if (hypergraph.Matrices == null)
            {
                errors.Add("Hypergraph matrices are missing.");
            }
            else
            {
                int nodeCount = hypergraph.Matrices.NodeOrder != null ? hypergraph.Matrices.NodeOrder.Count : 0;
                CheckSquare(hypergraph.Matrices.SubdivisionConnectivity, nodeCount, "subdivisionConnectivity", errors);
                CheckSquare(hypergraph.Matrices.AdjacencyConnectivity, nodeCount, "adjacencyConnectivity", errors);
                CheckSquare(hypergraph.Matrices.Area, nodeCount, "area", errors);
                CheckSquare(hypergraph.Matrices.Angle, nodeCount, "angle", errors);
                int edgeCount = hypergraph.Matrices.HyperedgeOrder != null ? hypergraph.Matrices.HyperedgeOrder.Count : 0;
                CheckRectangular(hypergraph.Matrices.Incidence, nodeCount, edgeCount, "incidence", errors);
            }

            ValidateDataTree(hypergraph.Root, errors);
            return errors;
        }

        private static void AddDataNode(HypergraphDataNode node, List<HypergraphDataNode> result)
        {
            if (node == null)
            {
                return;
            }

            result.Add(node);
            foreach (HypergraphDataNode child in node.Children ?? new List<HypergraphDataNode>())
            {
                AddDataNode(child, result);
            }
        }

        private static void ValidateDataTree(HypergraphDataNode node, List<string> errors)
        {
            if (node == null)
            {
                return;
            }

            if (node.Final && node.Children != null && node.Children.Count > 0)
            {
                errors.Add("Final DataNode " + node.Name + " must not contain children.");
            }

            if (!node.Final && (node.Children == null || node.Children.Count == 0))
            {
                errors.Add("Subdivision DataNode " + node.Name + " must contain children.");
            }

            foreach (HypergraphDataNode child in node.Children ?? new List<HypergraphDataNode>())
            {
                ValidateDataTree(child, errors);
            }
        }

        private static void CheckSquare(List<List<double>> matrix, int size, string name, List<string> errors)
        {
            CheckRectangular(matrix, size, size, name, errors);
        }

        private static void CheckRectangular(List<List<double>> matrix, int rows, int columns, string name, List<string> errors)
        {
            if (matrix == null || matrix.Count != rows)
            {
                errors.Add("Matrix " + name + " must have " + rows.ToString() + " rows.");
                return;
            }

            for (int i = 0; i < matrix.Count; i++)
            {
                if (matrix[i] == null || matrix[i].Count != columns)
                {
                    errors.Add("Matrix " + name + " row " + i.ToString() + " must have " + columns.ToString() + " columns.");
                }
            }
        }
    }
}
