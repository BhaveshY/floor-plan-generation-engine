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

            foreach (HypergraphNode node in nodes)
            {
                if (!string.IsNullOrWhiteSpace(node.ParentId) && !nodeIds.Contains(node.ParentId))
                {
                    errors.Add("Hypergraph node " + node.Id + " references unknown parent " + node.ParentId + ".");
                }

                foreach (string childId in node.Children ?? new List<string>())
                {
                    if (!nodeIds.Contains(childId))
                    {
                        errors.Add("Hypergraph node " + node.Id + " references unknown child " + childId + ".");
                    }
                }

                foreach (string connectedId in node.Connected ?? new List<string>())
                {
                    if (!nodeIds.Contains(connectedId))
                    {
                        errors.Add("Hypergraph node " + node.Id + " references unknown connected node " + connectedId + ".");
                    }
                }
            }

            HashSet<string> edgeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> expectedIncidence = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
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

                foreach (HyperedgeMember member in edge.Members ?? new List<HyperedgeMember>())
                {
                    if (string.IsNullOrWhiteSpace(member.NodeId))
                    {
                        errors.Add("Hyperedge " + edge.Id + " contains an empty member node id.");
                        continue;
                    }

                    if (!nodeIds.Contains(member.NodeId))
                    {
                        errors.Add("Hyperedge " + edge.Id + " references unknown node " + member.NodeId + ".");
                    }

                    if (string.IsNullOrWhiteSpace(member.Role))
                    {
                        errors.Add("Hyperedge " + edge.Id + " member " + member.NodeId + " has an empty role.");
                    }

                    Increment(expectedIncidence, IncidenceKey(edge.Id, member.NodeId, member.Role));
                }
            }

            HashSet<string> incidenceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> actualIncidence = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (HypergraphIncidence item in incidence)
            {
                if (string.IsNullOrWhiteSpace(item.Id))
                {
                    errors.Add("Incidence id is empty.");
                }
                else if (!incidenceIds.Add(item.Id))
                {
                    errors.Add("Duplicate incidence id: " + item.Id);
                }

                if (!nodeIds.Contains(item.NodeId))
                {
                    errors.Add("Incidence " + item.Id + " references unknown node " + item.NodeId + ".");
                }

                if (!edgeIds.Contains(item.HyperedgeId))
                {
                    errors.Add("Incidence " + item.Id + " references unknown hyperedge " + item.HyperedgeId + ".");
                }

                Increment(actualIncidence, IncidenceKey(item.HyperedgeId, item.NodeId, item.Role));
            }

            CompareIncidence(expectedIncidence, actualIncidence, errors);

            if (hypergraph.Matrices == null)
            {
                errors.Add("Hypergraph matrices are missing.");
            }
            else
            {
                int nodeCount = hypergraph.Matrices.NodeOrder != null ? hypergraph.Matrices.NodeOrder.Count : 0;
                CheckOrder("NodeOrder", hypergraph.Matrices.NodeOrder, nodeIds, "node", errors);
                CheckSquare(hypergraph.Matrices.SubdivisionConnectivity, nodeCount, "subdivisionConnectivity", errors);
                CheckSquare(hypergraph.Matrices.AdjacencyConnectivity, nodeCount, "adjacencyConnectivity", errors);
                CheckSquare(hypergraph.Matrices.Area, nodeCount, "area", errors);
                CheckSquare(hypergraph.Matrices.Angle, nodeCount, "angle", errors);
                int edgeCount = hypergraph.Matrices.HyperedgeOrder != null ? hypergraph.Matrices.HyperedgeOrder.Count : 0;
                CheckOrder("HyperedgeOrder", hypergraph.Matrices.HyperedgeOrder, edgeIds, "hyperedge", errors);
                CheckRectangular(hypergraph.Matrices.Incidence, nodeCount, edgeCount, "incidence", errors);
                CheckIncidenceMatrix(hypergraph.Matrices, incidence, errors);
                CheckSubdivisionMatrix(hypergraph.Matrices, hyperedges, errors);
                CheckAdjacencyMatrix(hypergraph.Matrices, hyperedges, errors);
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

        private static void CheckOrder(string name, List<string> order, HashSet<string> expected, string label, List<string> errors)
        {
            if (order == null)
            {
                errors.Add("Matrix " + name + " is missing.");
                return;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string id in order)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    errors.Add("Matrix " + name + " contains an empty " + label + " id.");
                    continue;
                }

                if (!seen.Add(id))
                {
                    errors.Add("Matrix " + name + " contains duplicate " + label + " id " + id + ".");
                }

                if (!expected.Contains(id))
                {
                    errors.Add("Matrix " + name + " references unknown " + label + " " + id + ".");
                }
            }

            foreach (string id in expected)
            {
                if (!seen.Contains(id))
                {
                    errors.Add("Matrix " + name + " is missing " + label + " " + id + ".");
                }
            }
        }

        private static void CompareIncidence(Dictionary<string, int> expected, Dictionary<string, int> actual, List<string> errors)
        {
            foreach (KeyValuePair<string, int> item in expected)
            {
                int count;
                actual.TryGetValue(item.Key, out count);
                if (count != item.Value)
                {
                    errors.Add("Missing incidence for hyperedge member " + item.Key + ".");
                }
            }

            foreach (KeyValuePair<string, int> item in actual)
            {
                int count;
                expected.TryGetValue(item.Key, out count);
                if (count != item.Value)
                {
                    errors.Add("Unexpected incidence record " + item.Key + ".");
                }
            }
        }

        private static void CheckIncidenceMatrix(HypergraphMatrices matrices, List<HypergraphIncidence> incidence, List<string> errors)
        {
            if (matrices.NodeOrder == null || matrices.HyperedgeOrder == null || matrices.Incidence == null)
            {
                return;
            }

            if (!TryBuildIndex(matrices.NodeOrder, out Dictionary<string, int> nodeIndex) ||
                !TryBuildIndex(matrices.HyperedgeOrder, out Dictionary<string, int> edgeIndex))
            {
                return;
            }

            Dictionary<string, double> expected = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (HypergraphIncidence item in incidence ?? new List<HypergraphIncidence>())
            {
                if (nodeIndex.ContainsKey(item.NodeId) && edgeIndex.ContainsKey(item.HyperedgeId))
                {
                    expected[MatrixKey(item.NodeId, item.HyperedgeId)] = item.Weight == 0.0 ? 1.0 : item.Weight;
                }
            }

            foreach (KeyValuePair<string, double> item in expected)
            {
                string[] parts = item.Key.Split('|');
                double actual = MatrixValue(matrices.Incidence, nodeIndex[parts[0]], edgeIndex[parts[1]]);
                if (!NearlyEqual(actual, item.Value))
                {
                    errors.Add("Matrix incidence does not match incidence record " + item.Key + ".");
                }
            }

            foreach (string nodeId in matrices.NodeOrder)
            {
                foreach (string edgeId in matrices.HyperedgeOrder)
                {
                    double value = MatrixValue(matrices.Incidence, nodeIndex[nodeId], edgeIndex[edgeId]);
                    if (!NearlyEqual(value, 0.0) && !expected.ContainsKey(MatrixKey(nodeId, edgeId)))
                    {
                        errors.Add("Matrix incidence contains unexpected node-edge pair " + MatrixKey(nodeId, edgeId) + ".");
                    }
                }
            }
        }

        private static void CheckSubdivisionMatrix(HypergraphMatrices matrices, List<Hyperedge> hyperedges, List<string> errors)
        {
            if (matrices.NodeOrder == null || matrices.SubdivisionConnectivity == null ||
                !TryBuildIndex(matrices.NodeOrder, out Dictionary<string, int> nodeIndex))
            {
                return;
            }

            HashSet<string> expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Hyperedge edge in hyperedges.Where(e => string.Equals(e.Kind, "subdivision", StringComparison.OrdinalIgnoreCase)))
            {
                string parentId = edge.Members.Where(m => string.Equals(m.Role, "parent", StringComparison.OrdinalIgnoreCase)).Select(m => m.NodeId).FirstOrDefault();
                if (string.IsNullOrWhiteSpace(parentId) || !nodeIndex.ContainsKey(parentId))
                {
                    continue;
                }

                foreach (HyperedgeMember child in edge.Members.Where(m => string.Equals(m.Role, "child", StringComparison.OrdinalIgnoreCase)))
                {
                    if (nodeIndex.ContainsKey(child.NodeId))
                    {
                        expected.Add(MatrixKey(parentId, child.NodeId));
                    }
                }
            }

            CheckBinarySquareMatrix("subdivisionConnectivity", matrices.SubdivisionConnectivity, matrices.NodeOrder, expected, errors);
        }

        private static void CheckAdjacencyMatrix(HypergraphMatrices matrices, List<Hyperedge> hyperedges, List<string> errors)
        {
            if (matrices.NodeOrder == null || matrices.AdjacencyConnectivity == null ||
                !TryBuildIndex(matrices.NodeOrder, out Dictionary<string, int> nodeIndex))
            {
                return;
            }

            HashSet<string> expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Hyperedge edge in hyperedges.Where(e => IsAdjacencyMatrixKind(e.Kind)))
            {
                List<string> members = edge.Members
                    .Select(m => m.NodeId)
                    .Where(id => nodeIndex.ContainsKey(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                for (int i = 0; i < members.Count; i++)
                {
                    for (int j = i + 1; j < members.Count; j++)
                    {
                        expected.Add(MatrixKey(members[i], members[j]));
                        expected.Add(MatrixKey(members[j], members[i]));
                    }
                }
            }

            CheckBinarySquareMatrix("adjacencyConnectivity", matrices.AdjacencyConnectivity, matrices.NodeOrder, expected, errors);
        }

        private static void CheckBinarySquareMatrix(string name, List<List<double>> matrix, List<string> order, HashSet<string> expected, List<string> errors)
        {
            if (!TryBuildIndex(order, out Dictionary<string, int> index))
            {
                return;
            }

            foreach (string key in expected)
            {
                string[] parts = key.Split('|');
                if (!NearlyEqual(MatrixValue(matrix, index[parts[0]], index[parts[1]]), 1.0))
                {
                    errors.Add("Matrix " + name + " is missing relationship " + key + ".");
                }
            }

            foreach (string from in order)
            {
                foreach (string to in order)
                {
                    double value = MatrixValue(matrix, index[from], index[to]);
                    if (!NearlyEqual(value, 0.0) && !expected.Contains(MatrixKey(from, to)))
                    {
                        errors.Add("Matrix " + name + " contains unexpected relationship " + MatrixKey(from, to) + ".");
                    }
                }
            }
        }

        private static bool IsAdjacencyMatrixKind(string kind)
        {
            return string.Equals(kind, "adjacency", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kind, "circulation_access", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kind, "door", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kind, "facade", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryBuildIndex(List<string> order, out Dictionary<string, int> index)
        {
            index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (order == null)
            {
                return false;
            }

            for (int i = 0; i < order.Count; i++)
            {
                string id = order[i];
                if (string.IsNullOrWhiteSpace(id) || index.ContainsKey(id))
                {
                    return false;
                }

                index.Add(id, i);
            }

            return true;
        }

        private static void Increment(Dictionary<string, int> values, string key)
        {
            int count;
            values.TryGetValue(key, out count);
            values[key] = count + 1;
        }

        private static string IncidenceKey(string edgeId, string nodeId, string role)
        {
            return (edgeId ?? string.Empty) + "|" + (nodeId ?? string.Empty) + "|" + (role ?? string.Empty);
        }

        private static string MatrixKey(string nodeId, string edgeId)
        {
            return (nodeId ?? string.Empty) + "|" + (edgeId ?? string.Empty);
        }

        private static double MatrixValue(List<List<double>> matrix, int row, int column)
        {
            if (matrix == null || row < 0 || row >= matrix.Count || matrix[row] == null || column < 0 || column >= matrix[row].Count)
            {
                return 0.0;
            }

            return matrix[row][column];
        }

        private static bool NearlyEqual(double left, double right)
        {
            return Math.Abs(left - right) <= 1e-9;
        }
    }
}
