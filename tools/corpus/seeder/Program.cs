using System.Globalization;
using System.Text.Json;

using Equiv.Corpus.Seeder;

Dictionary<string, string> options = ParseArgs(args);
if (!options.TryGetValue("root", out string? root) || !options.TryGetValue("manifest", out string? manifestPath))
{
    await Console.Error.WriteLineAsync("usage: seeder --root <dir> --manifest <seeds.json> [--count 300] [--seed n] [--changed <identities.json>]").ConfigureAwait(false);
    return 1;
}

int count = options.TryGetValue("count", out string? countText) ? int.Parse(countText, CultureInfo.InvariantCulture) : 300;
int seed = options.TryGetValue("seed", out string? seedText) ? int.Parse(seedText, CultureInfo.InvariantCulture) : Environment.TickCount;

HashSet<string> changed;
if (options.TryGetValue("changed", out string? changedPath))
{
    string changedJson = await File.ReadAllTextAsync(changedPath).ConfigureAwait(false);
    changed = new HashSet<string>(JsonSerializer.Deserialize<string[]>(changedJson) ?? [], StringComparer.Ordinal);
}
else
{
    changed = [];
}

MethodSeeder.SeedResult result = MethodSeeder.Seed(root, count, seed, changed);

JsonSerializerOptions jsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
string manifest = JsonSerializer.Serialize(
    new
    {
        seed,
        requested = result.Requested,
        applied = result.Applied,
        dropped = result.Dropped,
        seeds = result.Seeds.Select(static s => new { s.Id, s.Identity, s.File, s.Line, Operator = s.Operator.ToString() }),
    },
    jsonOptions);
await File.WriteAllTextAsync(manifestPath, manifest).ConfigureAwait(false);

Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"seeded: requested={result.Requested} applied={result.Applied} dropped={result.Dropped} seed={seed}"));
return 0;

static Dictionary<string, string> ParseArgs(string[] args)
{
    Dictionary<string, string> options = new(StringComparer.Ordinal);
    for (int i = 0; i + 1 < args.Length; i += 2)
    {
        options[args[i].TrimStart('-')] = args[i + 1];
    }

    return options;
}
