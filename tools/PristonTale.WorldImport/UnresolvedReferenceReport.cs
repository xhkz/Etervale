using Dapper;
using PristonTale.Persistence.Abstractions;

namespace PristonTale.WorldImport;

public sealed record UnresolvedPair(string Reference, string Owner, int Rows);

public sealed record UnresolvedKind(string Kind, int Rows, int References, IReadOnlyList<UnresolvedPair> Pairs);

// The references the last import kept unresolved (world_import_unresolved_refs), per kind: rows, distinct references and
// owner/reference pairs, most frequent first. The empty shop slot, recorded as "none", is counted apart from the rest.
public sealed record UnresolvedReferenceReport(int Rows, int EmptyShopSlots, IReadOnlyList<UnresolvedKind> Kinds)
{
    public const string ShopKind = "npc_shop";
    public const string EmptyShopSlot = "none";
    public const int Shown = 10;

    public static async Task<UnresolvedReferenceReport> ReadAsync(IDbConnectionFactory connections)
    {
        await using var connection = await connections.OpenAsync();
        var rows = (await connection.QueryAsync<(string Kind, string Owner, string Reference)>(
            "SELECT kind, owner_key, raw_reference FROM world_import_unresolved_refs")).ToList();
        var kinds = rows.GroupBy(static row => row.Kind, StringComparer.Ordinal)
            .OrderBy(static kind => kind.Key, StringComparer.Ordinal)
            .Select(static kind => new UnresolvedKind(kind.Key, kind.Count(),
                kind.Select(static row => row.Reference).Distinct(StringComparer.Ordinal).Count(),
                kind.GroupBy(static row => (row.Reference, row.Owner))
                    .Select(static pair => new UnresolvedPair(pair.Key.Reference, pair.Key.Owner, pair.Count()))
                    .OrderByDescending(static pair => pair.Rows)
                    .ThenBy(static pair => pair.Reference, StringComparer.Ordinal)
                    .ThenBy(static pair => pair.Owner, StringComparer.Ordinal)
                    .ToList()))
            .ToList();
        var emptySlots = rows.Count(static row => row.Kind == ShopKind && row.Reference == EmptyShopSlot);
        return new UnresolvedReferenceReport(rows.Count, emptySlots, kinds);
    }

    public static void Write(TextWriter output, UnresolvedReferenceReport report, bool all)
    {
        output.WriteLine();
        output.WriteLine($"=== unresolved references: {report.Rows} rows ({report.EmptyShopSlots} empty shop slots, "
            + $"{report.Rows - report.EmptyShopSlots} others) ===");
        foreach (var kind in report.Kinds)
        {
            output.WriteLine();
            output.WriteLine($"{kind.Kind}: {kind.Rows} rows, {kind.References} references, {kind.Pairs.Count} owner/reference pairs");
            foreach (var pair in all ? kind.Pairs : kind.Pairs.Take(Shown))
            {
                output.WriteLine($"  {pair.Reference,-24} {pair.Owner,-32} {pair.Rows,5}");
            }

            if (!all && kind.Pairs.Count > Shown)
            {
                output.WriteLine($"  ... {kind.Pairs.Count - Shown} more (--all lists them)");
            }
        }
    }
}
