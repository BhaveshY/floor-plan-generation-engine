using System;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace FloorPlanGeneration.Tests
{
    public sealed class ScriptRegressionTests
    {
        [Theory]
        [InlineData("run-sample.sh")]
        [InlineData("run-web.sh")]
        public void LocalDotnetInstallLogsDoNotPolluteCapturedDotnetPath(string scriptName)
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            using TempWorkspace workspace = TempWorkspace.Create();
            string repoRoot = workspace.Root;
            string scriptsDir = Path.Combine(repoRoot, "scripts");
            string stubsDir = Path.Combine(repoRoot, "stubs");
            Directory.CreateDirectory(scriptsDir);
            Directory.CreateDirectory(stubsDir);

            File.Copy(Path.Combine(RepositoryRoot(), "scripts", scriptName), Path.Combine(scriptsDir, scriptName));
            MakeExecutable(Path.Combine(scriptsDir, scriptName));
            WriteToolStubs(stubsDir, workspace.InvocationLog);

            ProcessStartInfo startInfo = new ProcessStartInfo("/bin/bash", Path.Combine(scriptsDir, scriptName))
            {
                WorkingDirectory = repoRoot,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            startInfo.Environment["PATH"] = stubsDir + ":/usr/bin:/bin:/usr/sbin:/sbin";
            startInfo.Environment["FAKE_DOTNET_INVOCATION"] = workspace.InvocationLog;

            using Process process = Process.Start(startInfo);
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Assert.True(process.ExitCode == 0, $"Expected {scriptName} to exit 0, got {process.ExitCode}.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
            Assert.Contains("dotnet-install: fake noisy stdout", stderr, StringComparison.Ordinal);
            Assert.DoesNotContain("dotnet-install: fake noisy stdout", stdout, StringComparison.Ordinal);

            string invocation = File.ReadAllText(workspace.InvocationLog);
            Assert.Contains("run --project", invocation, StringComparison.Ordinal);
            Assert.DoesNotContain("dotnet-install:", invocation, StringComparison.Ordinal);
        }

        private static void WriteToolStubs(string stubsDir, string invocationLog)
        {
            WriteExecutable(Path.Combine(stubsDir, "curl"),
                "#!/bin/bash\n" +
                "set -euo pipefail\n" +
                "out=''\n" +
                "while [ \"$#\" -gt 0 ]; do\n" +
                "  if [ \"$1\" = '-o' ]; then shift; out=\"$1\"; fi\n" +
                "  shift || true\n" +
                "done\n" +
                "mkdir -p \"$(dirname \"$out\")\"\n" +
                "printf '# fake dotnet installer\\n' > \"$out\"\n");

            WriteExecutable(Path.Combine(stubsDir, "bash"),
                "#!/bin/bash\n" +
                "set -euo pipefail\n" +
                "install_dir=''\n" +
                "while [ \"$#\" -gt 0 ]; do\n" +
                "  if [ \"$1\" = '--install-dir' ]; then shift; install_dir=\"$1\"; fi\n" +
                "  shift || true\n" +
                "done\n" +
                "echo 'dotnet-install: fake noisy stdout'\n" +
                "mkdir -p \"$install_dir\"\n" +
                "cat > \"$install_dir/dotnet\" <<'DOTNET'\n" +
                "#!/bin/bash\n" +
                "set -euo pipefail\n" +
                "if [ \"${1:-}\" = '--list-sdks' ]; then\n" +
                "  echo '8.0.999 [/fake]'\n" +
                "  exit 0\n" +
                "fi\n" +
                "printf '%s\\n' \"$*\" >> \"$FAKE_DOTNET_INVOCATION\"\n" +
                "exit 0\n" +
                "DOTNET\n" +
                "chmod +x \"$install_dir/dotnet\"\n");

            File.WriteAllText(invocationLog, string.Empty);
        }

        private static void WriteExecutable(string path, string contents)
        {
            File.WriteAllText(path, contents);
            MakeExecutable(path);
        }

        private static void MakeExecutable(string path)
        {
            using Process chmod = Process.Start("chmod", "+x " + path);
            chmod.WaitForExit();
            Assert.Equal(0, chmod.ExitCode);
        }

        private static string RepositoryRoot()
        {
            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        }

        private sealed class TempWorkspace : IDisposable
        {
            private TempWorkspace(string root)
            {
                Root = root;
                InvocationLog = Path.Combine(root, "dotnet-invocation.log");
            }

            public string Root { get; }
            public string InvocationLog { get; }

            public static TempWorkspace Create()
            {
                string root = Path.Combine(Path.GetTempPath(), "floor-plan-scripts-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                return new TempWorkspace(root);
            }

            public void Dispose()
            {
                try
                {
                    Directory.Delete(Root, recursive: true);
                }
                catch
                {
                    // Best-effort cleanup for temp files used only by this test.
                }
            }
        }
    }
}
