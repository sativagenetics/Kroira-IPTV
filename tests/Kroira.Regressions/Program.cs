using System.Text;

namespace Kroira.Regressions;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var options = RegressionRunnerOptions.Parse(args);
        if (options.ShowHelp)
        {
            WriteHelp();
            return 0;
        }

        var runner = new RegressionRunner();
        var result = await runner.RunAsync(options);

        if (options.ListOnly)
        {
            foreach (var caseId in result.DiscoveredCaseIds)
            {
                Console.WriteLine(caseId);
            }

            return 0;
        }

        if (result.UpdatedBaselineCount > 0)
        {
            Console.WriteLine($"Updated {result.UpdatedBaselineCount} baseline file(s).");
        }

        if (result.PassedCount > 0)
        {
            Console.WriteLine($"Passed {result.PassedCount} case(s).");
        }

        if (result.FailedCount > 0)
        {
            Console.WriteLine($"Failed {result.FailedCount} case(s).");
            return 1;
        }

        return 0;
    }

    private static void WriteHelp()
    {
        Console.WriteLine("KROIRA regression corpus runner");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project tests/Kroira.Regressions -- [--case <id>] [--update] [--list]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --case <id>   Run only one regression case.");
        Console.WriteLine("  --update      Overwrite expected baselines with current output.");
        Console.WriteLine("  --list        List discovered case ids and exit.");
        Console.WriteLine("  --help        Show this help.");
    }
}
