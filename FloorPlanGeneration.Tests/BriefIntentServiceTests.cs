using System;
using System.Collections.Generic;
using System.IO;
using FloorPlanGeneration.Web;
using Xunit;

namespace FloorPlanGeneration.Tests
{
    public sealed class BriefIntentServiceTests
    {
        [Fact]
        public void PickLatestVersionedCli_SelectsHighestVersionDirectoryThatHasTheExecutable()
        {
            string root = Path.Combine(Path.GetTempPath(), "fp-cli-" + Guid.NewGuid().ToString("N"));
            try
            {
                // Numeric ordering, not lexical: 2.10.0 must beat 2.1.187.
                CreateStubExe(root, "2.1.9", "claude.exe");
                CreateStubExe(root, "2.1.187", "claude.exe");
                CreateStubExe(root, "2.10.0", "claude.exe");
                Directory.CreateDirectory(Path.Combine(root, "2.99.0")); // version dir, no exe -> ignored
                CreateStubExe(root, "not-a-version", "claude.exe");      // non-semver loses to any version

                string picked = BriefIntentService.PickLatestVersionedCli(root, "claude.exe");

                Assert.Equal(Path.Combine(root, "2.10.0", "claude.exe"), picked);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Fact]
        public void PickLatestVersionedCli_ReturnsNullWhenNoExecutableOrBaseMissing()
        {
            string root = Path.Combine(Path.GetTempPath(), "fp-cli-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "1.0.0")); // version dir but no exe
            try
            {
                Assert.Null(BriefIntentService.PickLatestVersionedCli(root, "claude.exe"));
                Assert.Null(BriefIntentService.PickLatestVersionedCli(Path.Combine(root, "missing"), "claude.exe"));
                Assert.Null(BriefIntentService.PickLatestVersionedCli(null, "claude.exe"));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private static void CreateStubExe(string root, string version, string name)
        {
            string dir = Path.Combine(root, version);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, name), "stub");
        }

        [Fact]
        public void ExtractJsonObject_FindsFirstBalancedObjectInFreeText()
        {
            string text = "Sure! Here is the JSON:\n```json\n{ \"width\": 24, \"mix\": { \"studio\": 70 } }\n```\nDone.";
            string json = BriefIntentService.ExtractJsonObject(text);
            Assert.Equal("{ \"width\": 24, \"mix\": { \"studio\": 70 } }", json);
        }

        [Fact]
        public void ExtractJsonObject_IsStringAware()
        {
            string text = "{ \"understood\": [\"brace } inside\", \"escape \\\" quote\"] } trailing";
            string json = BriefIntentService.ExtractJsonObject(text);
            Assert.Equal("{ \"understood\": [\"brace } inside\", \"escape \\\" quote\"] }", json);
        }

        [Fact]
        public void ExtractJsonObject_ReturnsNullWhenNoObjectExists()
        {
            Assert.Null(BriefIntentService.ExtractJsonObject("no json here"));
            Assert.Null(BriefIntentService.ExtractJsonObject("{ unbalanced"));
            Assert.Null(BriefIntentService.ExtractJsonObject(null));
        }

        [Fact]
        public void Sanitize_ClampsValuesToBuildableRanges()
        {
            BriefIntent raw = new BriefIntent
            {
                Width = 4000.0,
                Depth = 1.0,
                Corridor = 9.0,
                MinUnit = 500.0,
                Variants = 99,
                Strictness = "BALANCED",
                Template = "L_SHAPED",
                Mix = new Dictionary<string, double> { { "Studio", 250.0 }, { "1-bed", 30.0 }, { "penthouse", 20.0 } }
            };

            BriefIntent intent = BriefIntentService.Sanitize(raw);

            Assert.Equal(200.0, intent.Width);
            Assert.Equal(8.0, intent.Depth);
            Assert.Equal(2.6, intent.Corridor);
            Assert.Equal(50.0, intent.MinUnit);
            Assert.Equal(20, intent.Variants);
            Assert.Equal("balanced", intent.Strictness);
            Assert.Equal("l-shaped-core", intent.Template);
            Assert.Equal(100.0, intent.Mix["studio"]);
            Assert.Equal(30.0, intent.Mix["one_bed"]);
            Assert.False(intent.Mix.ContainsKey("penthouse"));
        }

        [Fact]
        public void Sanitize_DropsUnknownEnumsAndEmptyCollections()
        {
            BriefIntent raw = new BriefIntent
            {
                Strictness = "chaotic",
                Template = "hexagonal",
                Mix = new Dictionary<string, double> { { "loft", 100.0 } },
                Understood = new List<string> { "   ", "" }
            };

            BriefIntent intent = BriefIntentService.Sanitize(raw);

            Assert.Null(intent.Strictness);
            Assert.Null(intent.Template);
            Assert.Null(intent.Mix);
            Assert.Null(intent.Understood);
        }

        [Fact]
        public void Sanitize_TruncatesAndLimitsUnderstoodLabels()
        {
            List<string> labels = new List<string>();
            for (int i = 0; i < 12; i++)
            {
                labels.Add("label-" + i + "-" + new string('x', 100));
            }

            BriefIntent intent = BriefIntentService.Sanitize(new BriefIntent { Understood = labels });

            Assert.Equal(8, intent.Understood.Count);
            Assert.All(intent.Understood, label => Assert.True(label.Length <= 60));
        }

        [Fact]
        public void Sanitize_NullInputYieldsEmptyIntent()
        {
            BriefIntent intent = BriefIntentService.Sanitize(null);
            Assert.Null(intent.Width);
            Assert.Null(intent.Mix);
            Assert.Null(intent.Template);
        }
    }
}
