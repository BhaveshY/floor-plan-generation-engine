using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using FloorPlanGeneration.Geometry;
using FloorPlanGeneration.Schema;

namespace FloorPlanGeneration.Operations
{
    public sealed class PlanOperationRequest
    {
        public PlanOperationRequest()
        {
            Input = new EngineInput();
            Operations = new List<PlanOperation>();
            ValidateOnly = false;
        }

        public EngineInput Input { get; set; }
        public List<PlanOperation> Operations { get; set; }
        public bool ValidateOnly { get; set; }
    }

    public sealed class PlanOperation
    {
        public PlanOperation()
        {
            Id = string.Empty;
            Kind = string.Empty;
            TargetId = string.Empty;
            TargetKind = string.Empty;
            UnitType = string.Empty;
            LockedReason = string.Empty;
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public string Id { get; set; }
        public string Kind { get; set; }
        public string TargetId { get; set; }
        public string TargetKind { get; set; }
        public string UnitType { get; set; }
        public double? Value { get; set; }
        public double? Width { get; set; }
        public double? Depth { get; set; }
        public double? TargetArea { get; set; }
        public double? MinArea { get; set; }
        public double? MaxArea { get; set; }
        public double? X { get; set; }
        public double? Y { get; set; }
        public bool Locked { get; set; }
        public string LockedReason { get; set; }
        public Dictionary<string, string> Parameters { get; set; }
    }

    public sealed class PlanOperationResult
    {
        public PlanOperationResult()
        {
            Status = "not_started";
            Input = new EngineInput();
            Output = new EngineOutput();
            Operations = new List<PlanOperationReceipt>();
            Diagnostics = new List<Diagnostic>();
        }

        public string Status { get; set; }
        public EngineInput Input { get; set; }
        public EngineOutput Output { get; set; }
        public List<PlanOperationReceipt> Operations { get; set; }
        public List<Diagnostic> Diagnostics { get; set; }
    }

    public sealed class PlanOperationReceipt
    {
        public PlanOperationReceipt()
        {
            Id = string.Empty;
            Kind = string.Empty;
            TargetId = string.Empty;
            Status = "not_started";
            Message = string.Empty;
            GraphIntent = string.Empty;
        }

        public string Id { get; set; }
        public string Kind { get; set; }
        public string TargetId { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }
        public string GraphIntent { get; set; }
    }

    public sealed class PlanOperationEngine
    {
        private const double MinimumPositiveDistance = 0.01;

        public PlanOperationResult Apply(PlanOperationRequest request)
        {
            PlanOperationResult result = new PlanOperationResult();
            if (request == null || request.Input == null)
            {
                return Fail(result, null, "operation.input_missing", "Plan operations require an EngineInput payload.");
            }

            EngineInput original = Clone(request.Input);
            EngineInput working = Clone(request.Input);
            List<PlanOperation> operations = request.Operations ?? new List<PlanOperation>();
            if (operations.Count == 0)
            {
                result.Diagnostics.Add(Diagnostic.Info(
                    "operation.noop",
                    "No operations were supplied; input was regenerated unchanged."));
            }

            for (int i = 0; i < operations.Count; i++)
            {
                PlanOperation operation = operations[i] ?? new PlanOperation();
                PlanOperationReceipt receipt = CreateReceipt(operation, i);
                string error;
                if (!TryApply(working, operation, receipt, out error))
                {
                    receipt.Status = "rejected";
                    receipt.Message = error;
                    result.Operations.Add(receipt);
                    return Fail(
                        result,
                        original,
                        "operation.rejected",
                        "Plan operation was rejected transactionally: " + error);
                }

                receipt.Status = "committed";
                result.Operations.Add(receipt);
            }

            FloorPlanEngine engine = new FloorPlanEngine();
            EngineOutput output = request.ValidateOnly ? engine.Validate(working) : engine.Generate(working);
            result.Input = working;
            result.Output = output;
            result.Status = output.Status;
            result.Diagnostics.AddRange(output.Diagnostics ?? new List<Diagnostic>());
            return result;
        }

        private static PlanOperationResult Fail(
            PlanOperationResult result,
            EngineInput input,
            string code,
            string message)
        {
            result.Status = "failed";
            result.Input = input ?? new EngineInput();
            result.Output = new EngineOutput
            {
                ProjectId = result.Input.Project != null ? result.Input.Project.Id : "project",
                Status = "failed",
                Diagnostics = new List<Diagnostic> { Diagnostic.Error(code, message) }
            };
            result.Diagnostics.Add(Diagnostic.Error(code, message));
            return result;
        }

        private static bool TryApply(
            EngineInput input,
            PlanOperation operation,
            PlanOperationReceipt receipt,
            out string error)
        {
            EnsureInput(input);
            string kind = NormalizeKind(operation.Kind);
            switch (kind)
            {
                case "setcorridorwidth":
                    return TrySetCorridorWidth(input, operation, receipt, out error);
                case "setroomminimum":
                    return TrySetRoomMinimum(input, operation, receipt, out error);
                case "resizeunittarget":
                    return TryResizeUnitTarget(input, operation, receipt, out error);
                case "adjustunitmixtarget":
                    return TryAdjustUnitMixTarget(input, operation, receipt, out error);
                case "resizefloorplate":
                    return TryResizeFloorplate(input, operation, receipt, out error);
                case "movefixedelement":
                    return TryMoveFixedElement(input, operation, receipt, out error);
                case "resizefixedelement":
                    return TryResizeFixedElement(input, operation, receipt, out error);
                case "lockelement":
                    return TryLockElement(operation, receipt, out error);
                default:
                    error = "Unsupported plan operation kind '" + (operation.Kind ?? string.Empty) + "'.";
                    return false;
            }
        }

        private static bool TrySetCorridorWidth(
            EngineInput input,
            PlanOperation operation,
            PlanOperationReceipt receipt,
            out string error)
        {
            double width = operation.Width ?? operation.Value ?? 0.0;
            if (!IsPositiveFinite(width))
            {
                error = "setCorridorWidth requires a positive width or value.";
                return false;
            }

            input.Rules.MinCorridorWidth = Round(width);
            receipt.Message = "Updated minimum corridor width to " + Format(width) + ".";
            receipt.GraphIntent = "Resize circulation node and rebalance adjacent space bands.";
            error = string.Empty;
            return true;
        }

        private static bool TrySetRoomMinimum(
            EngineInput input,
            PlanOperation operation,
            PlanOperationReceipt receipt,
            out string error)
        {
            double width = operation.Width ?? operation.Value ?? 0.0;
            double depth = operation.Depth ?? operation.Value ?? 0.0;
            if (!IsPositiveFinite(width) || !IsPositiveFinite(depth))
            {
                error = "setRoomMinimum requires positive width/depth or value.";
                return false;
            }

            input.Rules.MinRoomWidth = Round(width);
            input.Rules.MinRoomDepth = Round(depth);
            receipt.Message = "Updated room minimum to " + Format(width) + " x " + Format(depth) + ".";
            receipt.GraphIntent = "Project room leaves to satisfy minimum dimension constraints.";
            error = string.Empty;
            return true;
        }

        private static bool TryResizeUnitTarget(
            EngineInput input,
            PlanOperation operation,
            PlanOperationReceipt receipt,
            out string error)
        {
            string unitType = !string.IsNullOrWhiteSpace(operation.UnitType)
                ? operation.UnitType
                : operation.TargetId;
            if (string.IsNullOrWhiteSpace(unitType))
            {
                error = "resizeUnitTarget requires unitType or targetId.";
                return false;
            }

            double minArea;
            double maxArea;
            if (operation.TargetArea.HasValue || operation.Value.HasValue)
            {
                double targetArea = operation.TargetArea ?? operation.Value.Value;
                if (!IsPositiveFinite(targetArea))
                {
                    error = "resizeUnitTarget targetArea must be positive.";
                    return false;
                }

                minArea = Math.Max(1.0, Round(targetArea * 0.9));
                maxArea = Math.Max(minArea, Round(targetArea * 1.1));
            }
            else
            {
                minArea = operation.MinArea ?? 0.0;
                maxArea = operation.MaxArea ?? 0.0;
                if (!IsPositiveFinite(minArea) || !IsPositiveFinite(maxArea) || maxArea < minArea)
                {
                    error = "resizeUnitTarget requires targetArea or a valid minArea/maxArea range.";
                    return false;
                }
            }

            UnitTypeTarget target = EnsureUnitTarget(input, unitType);
            target.MinArea = minArea;
            target.MaxArea = maxArea;
            receipt.TargetId = unitType;
            receipt.Message = "Updated " + unitType + " target area to " + Format(minArea) + "-" + Format(maxArea) + ".";
            receipt.GraphIntent = "Resize unit program node and redistribute room leaves inside matching units.";
            error = string.Empty;
            return true;
        }

        private static bool TryAdjustUnitMixTarget(
            EngineInput input,
            PlanOperation operation,
            PlanOperationReceipt receipt,
            out string error)
        {
            string unitType = !string.IsNullOrWhiteSpace(operation.UnitType)
                ? operation.UnitType
                : operation.TargetId;
            if (string.IsNullOrWhiteSpace(unitType))
            {
                error = "adjustUnitMixTarget requires unitType or targetId.";
                return false;
            }

            double delta = operation.Value ?? 0.0;
            if (double.IsNaN(delta) || double.IsInfinity(delta))
            {
                error = "adjustUnitMixTarget value must be finite.";
                return false;
            }

            UnitTypeTarget target = EnsureUnitTarget(input, unitType);
            target.TargetRatio = Clamp(Round(target.TargetRatio + delta), 0.0, 1.0);
            NormalizeTargetRatios(input.Program.TargetUnitTypes);
            receipt.TargetId = unitType;
            receipt.Message = "Adjusted " + unitType + " target mix by " + Format(delta) + ".";
            receipt.GraphIntent = "Reweight unit program node and rebalance generated unit candidates.";
            error = string.Empty;
            return true;
        }

        private static bool TryResizeFloorplate(
            EngineInput input,
            PlanOperation operation,
            PlanOperationReceipt receipt,
            out string error)
        {
            Bounds2 bounds = BoundsOf(input.Floorplate.Outer.Points);
            if (bounds == null || bounds.Area <= 0.0)
            {
                error = "resizeFloorplate requires a valid outer boundary.";
                return false;
            }

            double width = operation.Width ?? 0.0;
            double depth = operation.Depth ?? 0.0;
            if (!IsPositiveFinite(width) || !IsPositiveFinite(depth))
            {
                error = "resizeFloorplate requires positive width and depth.";
                return false;
            }

            input.Floorplate.Outer.Points = ScalePointsToBox(input.Floorplate.Outer.Points, bounds, width, depth);
            foreach (PolygonInput hole in input.Floorplate.Holes ?? new List<PolygonInput>())
            {
                if (hole != null && hole.Points != null && hole.Points.Count > 0)
                {
                    hole.Points = ScalePointsToBox(hole.Points, bounds, width, depth);
                }
            }

            foreach (FixedElementInput fixedElement in input.FixedElements ?? new List<FixedElementInput>())
            {
                if (fixedElement != null && fixedElement.Polygon != null && fixedElement.Polygon.Points.Count > 0)
                {
                    fixedElement.Polygon.Points = ScalePointsToBox(fixedElement.Polygon.Points, bounds, width, depth);
                }
            }

            receipt.Message = "Resized floorplate to " + Format(width) + " x " + Format(depth) + ".";
            receipt.GraphIntent = "Resize root boundary and project child constraints into the new bounds.";
            error = string.Empty;
            return true;
        }

        private static bool TryMoveFixedElement(
            EngineInput input,
            PlanOperation operation,
            PlanOperationReceipt receipt,
            out string error)
        {
            FixedElementInput fixedElement = FindFixedElement(input, operation.TargetId);
            if (fixedElement == null)
            {
                error = "moveFixedElement targetId was not found.";
                return false;
            }

            Bounds2 bounds = BoundsOf(fixedElement.Polygon.Points);
            if (bounds == null || !operation.X.HasValue || !operation.Y.HasValue)
            {
                error = "moveFixedElement requires targetId, x, and y.";
                return false;
            }

            fixedElement.Polygon.Points = RectanglePoints(operation.X.Value, operation.Y.Value, bounds.Width, bounds.Height);
            receipt.Message = "Moved fixed element " + fixedElement.Id + ".";
            receipt.GraphIntent = "Move fixed constraint node and reroute circulation/rooms around it.";
            error = string.Empty;
            return true;
        }

        private static bool TryResizeFixedElement(
            EngineInput input,
            PlanOperation operation,
            PlanOperationReceipt receipt,
            out string error)
        {
            FixedElementInput fixedElement = FindFixedElement(input, operation.TargetId);
            if (fixedElement == null)
            {
                error = "resizeFixedElement targetId was not found.";
                return false;
            }

            Bounds2 bounds = BoundsOf(fixedElement.Polygon.Points);
            double width = operation.Width ?? 0.0;
            double depth = operation.Depth ?? 0.0;
            if (bounds == null || !IsPositiveFinite(width) || !IsPositiveFinite(depth))
            {
                error = "resizeFixedElement requires targetId, width, and depth.";
                return false;
            }

            fixedElement.Polygon.Points = RectanglePoints(bounds.MinX, bounds.MinY, width, depth);
            receipt.Message = "Resized fixed element " + fixedElement.Id + ".";
            receipt.GraphIntent = "Resize fixed constraint node and project dependent layout constraints.";
            error = string.Empty;
            return true;
        }

        private static bool TryLockElement(
            PlanOperation operation,
            PlanOperationReceipt receipt,
            out string error)
        {
            if (string.IsNullOrWhiteSpace(operation.TargetId))
            {
                error = "lockElement requires targetId.";
                return false;
            }

            receipt.Message = operation.Locked
                ? "Locked " + operation.TargetId + "."
                : "Unlocked " + operation.TargetId + ".";
            receipt.GraphIntent = "Record a graph lock for future solver passes.";
            error = string.Empty;
            return true;
        }

        private static PlanOperationReceipt CreateReceipt(PlanOperation operation, int index)
        {
            string id = !string.IsNullOrWhiteSpace(operation.Id)
                ? operation.Id
                : "operation-" + (index + 1).ToString("00", CultureInfo.InvariantCulture);
            return new PlanOperationReceipt
            {
                Id = id,
                Kind = operation.Kind ?? string.Empty,
                TargetId = operation.TargetId ?? string.Empty,
                Status = "pending"
            };
        }

        private static UnitTypeTarget EnsureUnitTarget(EngineInput input, string unitType)
        {
            UnitTypeTarget target = input.Program.TargetUnitTypes.FirstOrDefault(
                item => string.Equals(item.Type, unitType, StringComparison.OrdinalIgnoreCase));
            if (target != null)
            {
                return target;
            }

            target = new UnitTypeTarget { Type = unitType, MinArea = 30.0, MaxArea = 110.0 };
            input.Program.TargetUnitTypes.Add(target);
            return target;
        }

        private static FixedElementInput FindFixedElement(EngineInput input, string targetId)
        {
            if (string.IsNullOrWhiteSpace(targetId) || input.FixedElements == null)
            {
                return null;
            }

            return input.FixedElements.FirstOrDefault(
                fixedElement => fixedElement != null &&
                    string.Equals(fixedElement.Id, targetId, StringComparison.OrdinalIgnoreCase));
        }

        private static List<Point2> ScalePointsToBox(List<Point2> points, Bounds2 sourceBounds, double width, double depth)
        {
            double scaleX = width / Math.Max(sourceBounds.Width, MinimumPositiveDistance);
            double scaleY = depth / Math.Max(sourceBounds.Height, MinimumPositiveDistance);
            return (points ?? new List<Point2>()).Select(point => new Point2(
                Round(sourceBounds.MinX + ((point.X - sourceBounds.MinX) * scaleX)),
                Round(sourceBounds.MinY + ((point.Y - sourceBounds.MinY) * scaleY)))).ToList();
        }

        private static List<Point2> RectanglePoints(double x, double y, double width, double depth)
        {
            return new List<Point2>
            {
                new Point2(Round(x), Round(y)),
                new Point2(Round(x + width), Round(y)),
                new Point2(Round(x + width), Round(y + depth)),
                new Point2(Round(x), Round(y + depth))
            };
        }

        private static Bounds2 BoundsOf(List<Point2> points)
        {
            if (points == null || points.Count == 0)
            {
                return null;
            }

            double minX = points.Min(point => point.X);
            double minY = points.Min(point => point.Y);
            double maxX = points.Max(point => point.X);
            double maxY = points.Max(point => point.Y);
            return new Bounds2
            {
                MinX = minX,
                MinY = minY,
                MaxX = maxX,
                MaxY = maxY
            };
        }

        private static void EnsureInput(EngineInput input)
        {
            if (input.Project == null) input.Project = new ProjectInfo();
            if (input.Floorplate == null) input.Floorplate = new FloorplateInput();
            if (input.Floorplate.Outer == null) input.Floorplate.Outer = new PolygonInput();
            if (input.Floorplate.Outer.Points == null) input.Floorplate.Outer.Points = new List<Point2>();
            if (input.Floorplate.Holes == null) input.Floorplate.Holes = new List<PolygonInput>();
            if (input.FixedElements == null) input.FixedElements = new List<FixedElementInput>();
            if (input.Access == null) input.Access = new AccessInput();
            if (input.Facade == null) input.Facade = new FacadeInput();
            if (input.Program == null) input.Program = new ProgramBrief();
            if (input.Program.TargetUnitTypes == null) input.Program.TargetUnitTypes = new List<UnitTypeTarget>();
            if (input.Rules == null) input.Rules = new RuleSet();
            if (input.GenerationSettings == null) input.GenerationSettings = new GenerationSettings();
        }

        private static string NormalizeKind(string kind)
        {
            return new string((kind ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        }

        private static bool IsPositiveFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0.0;
        }

        private static double Round(double value)
        {
            return Math.Round(value, 4);
        }

        private static double Clamp(double value, double min, double max)
        {
            return Math.Min(max, Math.Max(min, value));
        }

        private static void NormalizeTargetRatios(List<UnitTypeTarget> targets)
        {
            List<UnitTypeTarget> usableTargets = (targets ?? new List<UnitTypeTarget>())
                .Where(target => target != null && target.TargetRatio > 0.0)
                .ToList();
            double total = usableTargets.Sum(target => target.TargetRatio);
            if (total <= 0.0)
            {
                return;
            }

            foreach (UnitTypeTarget target in usableTargets)
            {
                target.TargetRatio = Round(target.TargetRatio / total);
            }
        }

        private static string Format(double value)
        {
            return Round(value).ToString("0.####", CultureInfo.InvariantCulture);
        }

        private static EngineInput Clone(EngineInput input)
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DictionaryKeyPolicy = JsonNamingPolicy.CamelCase
            };
            return JsonSerializer.Deserialize<EngineInput>(JsonSerializer.Serialize(input, options), options);
        }
    }
}
