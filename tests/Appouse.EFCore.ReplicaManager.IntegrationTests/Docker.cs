using System.Diagnostics;
using Xunit;

namespace Appouse.EFCore.ReplicaManager.IntegrationTests;

/// <summary>
/// Thin wrapper over the docker CLI. Deliberately not a library dependency: these tests only need
/// run, stop, start and rm, and shelling out keeps the package's own dependency graph untouched.
/// </summary>
public static class Docker
{
    private static readonly Lazy<bool> Available = new(() =>
    {
        try
        {
            return Run("info", throwOnError: false).ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    });

    public static bool IsAvailable => Available.Value;

    public static (int ExitCode, string Output) Run(string arguments, bool throwOnError = true)
    {
        var info = new ProcessStartInfo("docker", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start docker.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit(120_000);

        if (throwOnError && process.ExitCode != 0)
        {
            throw new InvalidOperationException($"docker {arguments} failed ({process.ExitCode}): {output}");
        }

        return (process.ExitCode, output.Trim());
    }

    public static void RemoveQuietly(string container) => Run($"rm -f {container}", throwOnError: false);

    public static void Stop(string container) => Run($"stop -t 0 {container}");

    public static void Start(string container) => Run($"start {container}");
}

/// <summary>
/// A <see cref="FactAttribute"/> that skips itself when the docker daemon is not reachable, so the
/// suite stays green on a machine without Docker instead of reporting failures no one can act on.
/// </summary>
public sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute()
    {
        if (!Docker.IsAvailable)
        {
            Skip = "The docker daemon is not reachable; live database tests are skipped.";
        }
    }
}
