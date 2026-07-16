using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>A GitIsolationService whose RunBuildProcessAsync is stubbed, so
/// RunCiGateAsync can be unit-tested without a real build toolchain. The
/// ExitCode property controls what the fake build returns.</summary>
internal sealed class StubCiGateService : GitIsolationService
{
    public int ExitCode { get; set; }
    public string BuildOutput { get; set; } = "";

    public StubCiGateService(GitService git) : base(git) { }

    protected override Task<(int ExitCode, string Output)> RunBuildProcessAsync(
        BuildCommand cmd, string workingDirectory, CancellationToken ct)
        => Task.FromResult((ExitCode, BuildOutput));
}

public class CiGateTests
{
    [Fact]
    public async Task RunCiGate_UnknownTask_ReturnsFailure()
    {
        var git = new FakeGitService();
        var svc = new StubCiGateService(git);

        var (success, output) = await svc.RunCiGateAsync("nonexistent", CancellationToken.None);

        Assert.False(success);
        Assert.Equal("unknown isolated task", output);
    }

    [Fact]
    public async Task RunCiGate_NoBuildSystem_ReturnsTrueWithSkipMessage()
    {
        // Create a temp directory that exists but has no project files
        var tempDir = Path.Combine(Path.GetTempPath(), "ailocal-ci-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var git = new FakeGitService();
            var svc = new StubCiGateService(git) { ExitCode = 0 };
            svc.BuildOutput = "should not be called";

            var task = await svc.CreateAsync(tempDir, "test", null, CancellationToken.None);
            Assert.NotNull(task);

            // Create the worktree directory so DetectBuildCommand finds it,
            // but leave it empty — no .sln, .csproj, package.json, etc.
            Directory.CreateDirectory(task!.WorktreePath);

            var (success, output) = await svc.RunCiGateAsync(task.TaskId, CancellationToken.None);

            Assert.True(success);
            Assert.Equal("no build system detected - skipping gate", output);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task RunCiGate_WithDotNetProject_BuildSucceeds()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ailocal-ci-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var git = new FakeGitService();
            var svc = new StubCiGateService(git) { ExitCode = 0, BuildOutput = "Build succeeded." };

            var task = await svc.CreateAsync(tempDir, "test", null, CancellationToken.None);
            Assert.NotNull(task);

            // Create the worktree directory and a minimal .csproj so
            // DetectBuildCommand picks it up as a .NET project.
            Directory.CreateDirectory(task!.WorktreePath);
            File.WriteAllText(
                Path.Combine(task.WorktreePath, "test.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

            var (success, output) = await svc.RunCiGateAsync(task.TaskId, CancellationToken.None);

            Assert.True(success);
            Assert.Equal("Build succeeded.", output);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task RunCiGate_WithDotNetProject_BuildFails()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ailocal-ci-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var git = new FakeGitService();
            var svc = new StubCiGateService(git) { ExitCode = 1, BuildOutput = "Build FAILED.\nerror CS1001: Identifier expected" };

            var task = await svc.CreateAsync(tempDir, "test", null, CancellationToken.None);
            Assert.NotNull(task);

            Directory.CreateDirectory(task!.WorktreePath);
            File.WriteAllText(
                Path.Combine(task.WorktreePath, "test.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            // Deliberately broken .cs file would go here, but the stub
            // controls the exit code so we don't need a real compiler.

            var (success, output) = await svc.RunCiGateAsync(task.TaskId, CancellationToken.None);

            Assert.False(success);
            Assert.Equal("Build FAILED.\nerror CS1001: Identifier expected", output);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task RunCiGate_WithNpmProject_BuildSucceeds()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ailocal-ci-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var git = new FakeGitService();
            var svc = new StubCiGateService(git) { ExitCode = 0, BuildOutput = "> my-app@1.0.0 build\n> tsc\nBuild passed." };

            var task = await svc.CreateAsync(tempDir, "test", null, CancellationToken.None);
            Assert.NotNull(task);

            Directory.CreateDirectory(task!.WorktreePath);
            File.WriteAllText(
                Path.Combine(task.WorktreePath, "package.json"),
                """{"name":"test","scripts":{"build":"tsc"}}""");

            var (success, output) = await svc.RunCiGateAsync(task.TaskId, CancellationToken.None);

            Assert.True(success);
            Assert.Equal("> my-app@1.0.0 build\n> tsc\nBuild passed.", output);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task RunCiGate_WithCargoProject_BuildFails()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ailocal-ci-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var git = new FakeGitService();
            var svc = new StubCiGateService(git) { ExitCode = 101, BuildOutput = "error[E0425]: cannot find value `x` in this scope" };

            var task = await svc.CreateAsync(tempDir, "test", null, CancellationToken.None);
            Assert.NotNull(task);

            Directory.CreateDirectory(task!.WorktreePath);
            File.WriteAllText(
                Path.Combine(task.WorktreePath, "Cargo.toml"),
                "[package]\nname = \"test\"\nversion = \"0.1.0\"\nedition = \"2021\"\n");

            var (success, output) = await svc.RunCiGateAsync(task.TaskId, CancellationToken.None);

            Assert.False(success);
            Assert.Equal("error[E0425]: cannot find value `x` in this scope", output);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}