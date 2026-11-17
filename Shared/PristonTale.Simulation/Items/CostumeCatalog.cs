using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PristonTale.Simulation.Items;

public enum CostumeGender
{
    Any,
    Male,
    Female
}

// The items worn in the wardrobe (PlayerCharacter.Costume), from Data/Costumes.json: who may wear each one and the body
// it draws for each armour row (CharacterVisualSet.ArmorTableIndex, 0 Mechanician to 10 Martial Artist). A costume
// without a body for a row keeps that class's armour body; it is still worn and still adds its roll.
public sealed class CostumeCatalog
{
    public const int Version = 1;
    public const int ArmorRows = 11;
    private const string Resource = "PristonTale.Simulation.Items.Data.Costumes.json";

    // Before Shipped: static fields initialize in order.
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly FrozenDictionary<string, Costume> _costumes;

    private CostumeCatalog(FrozenDictionary<string, Costume> costumes) => _costumes = costumes;

    public static CostumeCatalog Shipped { get; } = Load();

    public int Count => _costumes.Count;

    public bool Contains(string key) => key is not null && _costumes.ContainsKey(key);

    public bool Allows(string key, bool female) =>
        key is not null && _costumes.TryGetValue(key, out var costume)
        && (costume.Gender == CostumeGender.Any || costume.Gender == CostumeGender.Female == female);

    // The wardrobe's rule: a single copy of a costume for the wearer's gender.
    public bool Wears(string key, int count, bool female) => count == 1 && Allows(key, female);

    // The asset-relative body (char/....inx) the costume draws for the armour row, if it has one.
    public bool TryBody(string key, int armorRow, out string body)
    {
        body = "";
        return key is not null && _costumes.TryGetValue(key, out var costume) && costume.Bodies.TryGetValue(armorRow, out body);
    }

    public IEnumerable<string> Bodies() => _costumes.Values.SelectMany(static costume => costume.Bodies.Values).Distinct(StringComparer.OrdinalIgnoreCase);

    public static CostumeCatalog FromJson(string json) => From(JsonSerializer.Deserialize<Manifest>(json, Options));

    private static CostumeCatalog Load()
    {
        using var stream = typeof(CostumeCatalog).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidDataException($"Missing embedded resource {Resource}.");
        return From(JsonSerializer.Deserialize<Manifest>(stream, Options));
    }

    private static CostumeCatalog From(Manifest manifest)
    {
        if (manifest is null || manifest.Version != Version)
        {
            throw new InvalidDataException($"The costume list must be version {Version}.");
        }

        var costumes = new Dictionary<string, Costume>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in manifest.Costumes ?? [])
        {
            if (string.IsNullOrWhiteSpace(entry.ItemKey) || !Enum.IsDefined(entry.Gender))
            {
                throw new InvalidDataException($"Costume '{entry.ItemKey}' has no item key or an unknown gender.");
            }

            var bodies = new Dictionary<int, string>();
            foreach (var (row, path) in entry.Bodies ?? [])
            {
                if (row is < 0 or >= ArmorRows || !SafeBody(path))
                {
                    throw new InvalidDataException($"Costume '{entry.ItemKey}' has an invalid body for armour row {row}: '{path}'.");
                }

                bodies.Add(row, path.Replace('\\', '/'));
            }

            if (!costumes.TryAdd(entry.ItemKey, new Costume(entry.Gender, bodies.ToFrozenDictionary())))
            {
                throw new InvalidDataException($"Costume '{entry.ItemKey}' is listed twice.");
            }
        }

        return new CostumeCatalog(costumes.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));
    }

    // An asset-relative character model: under char/, an .inx, no drive, empty, '.' or '..' segment.
    private static bool SafeBody(string path)
    {
        var body = path?.Replace('\\', '/');
        return !string.IsNullOrWhiteSpace(body) && body.StartsWith("char/", StringComparison.OrdinalIgnoreCase)
            && body.EndsWith(".inx", StringComparison.OrdinalIgnoreCase) && !body.Contains(':')
            && Array.TrueForAll(body.Split('/'), static segment => segment is not ("" or "." or ".."));
    }

    private sealed record Costume(CostumeGender Gender, FrozenDictionary<int, string> Bodies);

    private sealed record Manifest(int Version, Entry[] Costumes);

    private sealed record Entry(string ItemKey, CostumeGender Gender, Dictionary<int, string> Bodies);
}
