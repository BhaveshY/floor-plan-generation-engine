using System.Linq;
using FloorPlanGeneration.Generation;
using FloorPlanGeneration.Schema;
using Xunit;

namespace FloorPlanGeneration.Tests
{
    public sealed class FurnitureProportionsSchemaTests
    {
        [Fact]
        public void RuleSet_DefaultsFurnitureMinimumsOff_SoPlansStayByteIdentical()
        {
            // Opt-in, exactly like GridModule: the default must be the historic no-op.
            Assert.False(new RuleSet().ApplyFurnitureMinimums);
            Assert.Equal(0.0, new RoomTypeRule().MaxAspect, 6);
        }

        [Fact]
        public void StandardTable_CarriesTheVerifiedGermanRoomMinima()
        {
            var table = FurnitureDefaults.StandardTable();

            string[] expected =
            {
                "bedroom", "living", "living_sleeping", "dining", "study", "kitchen",
                "bathroom", "utility", "balcony", "foyer", "pooja", "store",
            };
            Assert.Equal(expected.OrderBy(t => t), table.Select(r => r.Type).OrderBy(t => t));

            AssertRow(table, "bedroom", 3.0, 3.6, 11.0, 1.7);
            AssertRow(table, "living", 3.4, 4.2, 16.0, 1.8);
            AssertRow(table, "living_sleeping", 3.4, 6.0, 22.0, 2.2);
            AssertRow(table, "kitchen", 1.8, 3.0, 6.0, 2.6);
            AssertRow(table, "bathroom", 1.6, 2.1, 3.4, 2.0);
            AssertRow(table, "foyer", 1.2, 1.5, 1.8, 3.0);
            AssertRow(table, "store", 0.8, 1.25, 1.0, 2.2);
        }

        private static void AssertRow(
            System.Collections.Generic.List<RoomTypeRule> table,
            string type, double minWidth, double minDepth, double minArea, double maxAspect)
        {
            RoomTypeRule rule = table.Single(r => r.Type == type);
            Assert.Equal(minWidth, rule.MinWidth, 6);
            Assert.Equal(minDepth, rule.MinDepth, 6);
            Assert.Equal(minArea, rule.MinArea, 6);
            Assert.Equal(maxAspect, rule.MaxAspect, 6);
        }
    }
}
