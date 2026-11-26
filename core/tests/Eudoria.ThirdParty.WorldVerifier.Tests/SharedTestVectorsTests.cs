using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Eudoria.ThirdParty.WorldVerifier.Tests;

/// <summary>
/// The shared test vectors (shared/test-vectors/entry-tickets/, tools/dev/new-entry-ticket-vectors.ps1):
/// this verifier must give exactly the expected verdict for each. The C++ verifier (part B) runs the same
/// files: together, they prove both read the format the same way.
/// </summary>
public sealed class SharedTestVectorsTests
{
    private static readonly string Directory = Path.Combine(AppContext.BaseDirectory, "TestVectors");

    public static TheoryData<string> VectorNames() => [.. Vectors().Select(vector => vector["name"]!.GetValue<string>())];

    [Theory]
    [MemberData(nameof(VectorNames))]
    public void EveryVector_GetsItsExpectedVerdict(string name)
    {
        JsonObject document = Document();
        JsonNode vector = Vectors().Single(candidate => candidate["name"]!.GetValue<string>() == name);
        IReadOnlyDictionary<string, ECParameters> keys = EntryTicketKeyStore.ParseJwkSet(File.ReadAllBytes(Path.Combine(Directory, "keys.json")))!;

        TicketCheck check = EntryTicketRules.Check(
            vector["ticket"]!.GetValue<string>(),
            document["world"]!.GetValue<string>(),
            DateTimeOffset.FromUnixTimeSeconds(vector["now"]!.GetValue<long>()),
            keys);

        string verdict = check.Ticket is null ? check.Rejection! : "accepted";
        Assert.Equal(vector["expected"]!.GetValue<string>(), verdict);
        if (check.Ticket is not null)
        {
            Assert.Equal(Guid.Parse(document["accountId"]!.GetValue<string>()), check.Ticket.AccountId);
        }
    }

    [Fact]
    public void Vectors_CoverEveryRejectionThatDoesNotNeedMemoryOrNetwork()
    {
        string[] expected = [.. Vectors().Select(vector => vector["expected"]!.GetValue<string>()).Distinct()];
        string[] statelessCodes = [.. typeof(TicketRejections).GetFields().Select(field => (string)field.GetRawConstantValue()!)
            .Except([TicketRejections.Replayed, TicketRejections.ReplayCacheFull, TicketRejections.KeysUnavailable])];

        Assert.Contains("accepted", expected);
        Assert.All(statelessCodes, code => Assert.Contains(code, expected));
        Assert.Equal(EntryTicketRules.ClockToleranceSeconds, Document()["clockToleranceSeconds"]!.GetValue<long>());
    }

    [Fact]
    public void KeysFile_HoldsPublicKeysOnly()
    {
        JsonNode keys = JsonNode.Parse(File.ReadAllText(Path.Combine(Directory, "keys.json")))!;

        Assert.All(keys["keys"]!.AsArray(), key => Assert.False(key!.AsObject().ContainsKey("d")));
        Assert.Single(EntryTicketKeyStore.ParseJwkSet(File.ReadAllBytes(Path.Combine(Directory, "keys.json")))!);
    }

    private static JsonObject Document() => JsonNode.Parse(File.ReadAllText(Path.Combine(Directory, "vectors.json")))!.AsObject();

    private static IEnumerable<JsonNode> Vectors() => Document()["vectors"]!.AsArray().Select(vector => vector!);
}
