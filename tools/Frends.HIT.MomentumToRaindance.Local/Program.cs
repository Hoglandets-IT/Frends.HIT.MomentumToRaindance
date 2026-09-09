using System.Globalization;
using System.Text;
using Frends.HIT.MomentumToRaindance;

const string usage = """
Usage:
  ./generate-raindance.sh OUTPUT_FILE [--json-output FILE] [--last-local-id ID]

Diagnostic preview only: output is NOT reserved in the invoice database. Never deliver preview files to Raindance.

Options:
  --env PATH             Read Momentum JSON configuration from PATH (default: .env)
  --json-output FILE     Also write the prettified Momentum response as UTF-8 JSON
  --last-local-id ID     Fetch nodes after this Momentum local ID (default: 0)
  -h, --help             Show this help
""";

try
{
    var options = ParseArguments(args);
    if (options.ShowHelp)
    {
        Console.WriteLine(usage);
        return 0;
    }

    if (options.OutputPath is null)
    {
        Console.Error.WriteLine("Missing OUTPUT_FILE.\n");
        Console.Error.WriteLine(usage);
        return 2;
    }

    var envPath = Path.GetFullPath(options.EnvPath);
    var outputPath = Path.GetFullPath(options.OutputPath);
    var jsonOutputPath = options.JsonOutputPath is null
        ? null
        : Path.GetFullPath(options.JsonOutputPath);
    ValidateOutputPaths(envPath, outputPath, jsonOutputPath);
    if (!File.Exists(envPath))
    {
        Console.Error.WriteLine($"Configuration file not found: {envPath}");
        return 2;
    }

    var configurationJson = await File.ReadAllTextAsync(envPath);
    var connection = new MomentumConnection
    {
        ConfigurationSource = MomentumConfigurationSource.Json,
        JsonConfiguration = configurationJson
    };

    Console.WriteLine("DIAGNOSTIC PREVIEW — no duplicate protection. Do not deliver this output to Raindance.");
    Console.WriteLine($"Fetching Momentum nodes after local ID {options.LastLocalId}...");
    var fetched = await Main.FetchLedgerNoteAccountings(
        connection,
        new FetchInput { LastLocalId = options.LastLocalId });

    if (jsonOutputPath is not null)
    {
        EnsureParentDirectory(jsonOutputPath);
        await File.WriteAllTextAsync(jsonOutputPath, fetched.ResultFile, new UTF8Encoding(false));
        Console.WriteLine($"Wrote prettified Momentum JSON: {jsonOutputPath}");
    }

    var converted = Main.ConvertCore(
        new ConvertInput { GraphQlResult = fetched.ResultFile, LastLocalId = fetched.LastLocalId });

    if (converted.NodeCount == 0)
    {
        Console.WriteLine($"No new invoice rows. No Raindance file written; local ID remains {converted.LastLocalId}.");
        return 0;
    }

    EnsureParentDirectory(outputPath);
    var outputBytes = converted.ResultFile;
    await File.WriteAllBytesAsync(outputPath, outputBytes);

    Console.WriteLine($"Wrote {converted.NodeCount} node(s), {outputBytes.Length} bytes: {outputPath}");
    Console.WriteLine($"Preview local ID: {converted.LastLocalId}. Do not update a production checkpoint from this diagnostic run.");
    return 0;
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(usage);
    return 2;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Failed: {exception.Message}");
    return 1;
}

static LocalOptions ParseArguments(string[] arguments)
{
    string? outputPath = null;
    string? jsonOutputPath = null;
    var envPath = ".env";
    var lastLocalId = 0;
    var showHelp = false;

    for (var index = 0; index < arguments.Length; index++)
    {
        switch (arguments[index])
        {
            case "-h" or "--help":
                showHelp = true;
                break;
            case "--env":
                envPath = RequiredValue(arguments, ref index, "--env");
                break;
            case "--json-output":
                jsonOutputPath = RequiredValue(arguments, ref index, "--json-output");
                break;
            case "--last-local-id":
                var value = RequiredValue(arguments, ref index, "--last-local-id");
                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out lastLocalId) || lastLocalId < 0)
                {
                    throw new ArgumentException("--last-local-id must be a non-negative integer.");
                }

                break;
            default:
                if (arguments[index].StartsWith("-", StringComparison.Ordinal))
                {
                    throw new ArgumentException($"Unknown option: {arguments[index]}");
                }

                if (outputPath is not null)
                {
                    throw new ArgumentException($"Unexpected argument: {arguments[index]}");
                }

                outputPath = arguments[index];
                break;
        }
    }

    return new LocalOptions(outputPath, jsonOutputPath, envPath, lastLocalId, showHelp);
}

static void EnsureParentDirectory(string path)
{
    var directory = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }
}

static void ValidateOutputPaths(string envPath, string outputPath, string? jsonOutputPath)
{
    // Conservative on case-sensitive systems too: never risk replacing the credentials or
    // replacing a JSON dump with invoice text because of a filename typo.
    var paths = new[] { envPath, outputPath, jsonOutputPath }.OfType<string>().ToArray();
    if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length)
        throw new ArgumentException("Configuration, OUTPUT_FILE and --json-output must refer to different files.");

    foreach (var path in paths)
    {
        for (FileSystemInfo? entry = new FileInfo(path); entry is not null; entry = entry switch
        {
            FileInfo file => file.Directory,
            DirectoryInfo directory => directory.Parent,
            _ => null
        })
        {
            if (entry.LinkTarget is not null)
                throw new ArgumentException("Use direct file paths without symbolic links for configuration and outputs.");
        }
    }
}

static string RequiredValue(string[] arguments, ref int index, string option)
{
    index++;
    if (index >= arguments.Length || string.IsNullOrWhiteSpace(arguments[index]))
    {
        throw new ArgumentException($"{option} requires a value.");
    }

    return arguments[index];
}

internal sealed record LocalOptions(
    string? OutputPath,
    string? JsonOutputPath,
    string EnvPath,
    int LastLocalId,
    bool ShowHelp);
