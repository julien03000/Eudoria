using Eudoria.Core.Identity.Accounts;
using Eudoria.Core.Identity.Passwords;

namespace Eudoria.Core.Identity.Tests;

public sealed class CommonPasswordsTests
{
    private static readonly CommonPasswords Embedded = new();

    [Fact]
    public void EmbeddedList_IsLoaded_AndOnlyHoldsEntriesTheRulesWouldAccept()
    {
        Assert.InRange(Embedded.Count, 20_000, 100_000);

        using Stream stream = typeof(CommonPasswords).Assembly.GetManifestResourceStream(CommonPasswords.ResourceName)!;
        using StreamReader reader = new(stream);
        string[] lines = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.All(lines, line =>
        {
            Assert.Null(AccountRules.ValidatePassword(line)); // 12 to 128 code points
            Assert.Equal(line.Normalize(System.Text.NormalizationForm.FormKC).ToLowerInvariant(), line);
        });
    }

    [Theory]
    [InlineData("azertyuiop123")]
    [InlineData("AZERTYuiop123")] // letter case ignored
    [InlineData("qwertyuiop123")]
    [InlineData("1234567890abc")]
    public void LeakedPasswords_AreRefused(string password) =>
        Assert.True(Embedded.IsRefused(password, "Ana_42", "ana@example.com"));

    [Theory]
    [InlineData("correct horse battery staple")]
    [InlineData("a-long-enough-passphrase")]
    [InlineData("Velours-Ardoise-Peuplier-71")]
    public void UncommonPasswords_AreAccepted(string password) =>
        Assert.False(Embedded.IsRefused(password, "Ana_42", "ana@example.com"));

    [Theory]
    [InlineData("my-eudoria-password-2026")] // service name
    [InlineData("I love EUDORIA so much")]
    [InlineData("Dragonfly_77-is-my-secret")] // pseudo, letter case ignored
    [InlineData("jean.dupont-secret-key")] // e-mail local part
    public void ContextWords_AreRefused(string password) =>
        Assert.True(Embedded.IsRefused(password, "dragonfly_77", "jean.dupont@example.com"));

    [Fact]
    public void ShortContextWords_AreIgnored()
    {
        // Pseudo "Ana" and e-mail "bob@..." are shorter than 4 characters: "banana" and "bobsleigh" stay allowed.
        Assert.False(Embedded.IsRefused("banana-bobsleigh-split", "Ana", "bob@example.com"));
        Assert.True(Embedded.IsRefused("banana-bobsleigh-split", "Banana", "bob@example.com"));
    }

    [Fact]
    public void List_ComparesTheLowerCasedPassword()
    {
        CommonPasswords list = new(["zebra-crossing-42"]);

        Assert.True(list.IsRefused("ZEBRA-Crossing-42", pseudo: null, email: null));
        Assert.False(list.IsRefused("zebra-crossing-43", pseudo: null, email: null));
    }
}
