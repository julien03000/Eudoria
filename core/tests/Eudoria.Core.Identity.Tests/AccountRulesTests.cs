using Eudoria.Core.Identity.Accounts;

namespace Eudoria.Core.Identity.Tests;

public sealed class AccountRulesTests
{
    [Theory]
    [InlineData("  Ana@Example.COM ", "ana@example.com")]
    [InlineData("ana@example.com", "ana@example.com")]
    public void NormalizeEmail_TrimsAndLowerCases(string input, string expected) =>
        Assert.Equal(expected, AccountRules.NormalizeEmail(input));

    [Theory]
    [InlineData("ana@example.com")]
    [InlineData("ana.b+tag@mail.example.org")]
    [InlineData("a@b.cd")]
    public void ValidateEmail_AcceptsUsualAddresses(string email) =>
        Assert.Null(AccountRules.ValidateEmail(email));

    [Theory]
    [InlineData("", "email_required")]
    [InlineData("ana", "email_invalid")]
    [InlineData("@example.com", "email_invalid")]
    [InlineData("ana@", "email_invalid")]
    [InlineData("ana@localhost", "email_invalid")]
    [InlineData("ana@@example.com", "email_invalid")]
    [InlineData("ana@b@example.com", "email_invalid")]
    [InlineData("ana@.example.com", "email_invalid")]
    [InlineData("ana@example.com.", "email_invalid")]
    [InlineData("ana@example..com", "email_invalid")]
    [InlineData("an a@example.com", "email_invalid")]
    [InlineData("ana\u0000@example.com", "email_invalid")]
    public void ValidateEmail_RejectsMalformedAddresses(string email, string expectedCode) =>
        Assert.Equal(expectedCode, AccountRules.ValidateEmail(email));

    [Fact]
    public void ValidateEmail_Accepts254Characters_AndRejects255()
    {
        string domain = "@example.com";
        Assert.Null(AccountRules.ValidateEmail(new string('a', 254 - domain.Length) + domain));
        Assert.Equal("email_invalid", AccountRules.ValidateEmail(new string('a', 255 - domain.Length) + domain));
    }

    [Theory]
    [InlineData("Ana")]
    [InlineData("Ana_42")]
    [InlineData("a-b_c")]
    [InlineData("Abcdefghijklmnopqrst")] // 20 characters
    public void ValidatePseudo_AcceptsValidPseudos(string pseudo) =>
        Assert.Null(AccountRules.ValidatePseudo(pseudo));

    [Theory]
    [InlineData("", "pseudo_required")]
    [InlineData("Ab", "pseudo_too_short")]
    [InlineData("Abcdefghijklmnopqrstu", "pseudo_too_long")] // 21 characters
    [InlineData("42Ana", "pseudo_must_start_with_letter")]
    [InlineData("_Ana", "pseudo_must_start_with_letter")]
    [InlineData("Ana 42", "pseudo_invalid_characters")]
    [InlineData("Anaïs", "pseudo_invalid_characters")]
    [InlineData("Аna", "pseudo_invalid_characters")] // Cyrillic A: a look-alike of "Ana"
    [InlineData("Ana.42", "pseudo_invalid_characters")]
    [InlineData(" Ana", "pseudo_invalid_characters")]
    public void ValidatePseudo_RejectsInvalidPseudos(string pseudo, string expectedCode) =>
        Assert.Equal(expectedCode, AccountRules.ValidatePseudo(pseudo));

    [Fact]
    public void NormalizePseudo_LowerCases() => Assert.Equal("ana_42", AccountRules.NormalizePseudo("Ana_42"));

    [Fact]
    public void ValidatePassword_Accepts12To128CodePoints_AndRejectsOutside()
    {
        Assert.Equal("password_required", AccountRules.ValidatePassword(string.Empty));
        Assert.Equal("password_too_short", AccountRules.ValidatePassword(new string('x', 11)));
        Assert.Null(AccountRules.ValidatePassword(new string('x', 12)));
        Assert.Null(AccountRules.ValidatePassword(new string('x', 128)));
        Assert.Equal("password_too_long", AccountRules.ValidatePassword(new string('x', 129)));
    }

    [Fact]
    public void ValidatePassword_CountsUnicodeCodePoints_NotUtf16Units()
    {
        // 😀 is one code point but two UTF-16 units: 12 emojis = 12 characters, not 24.
        string twelveEmojis = string.Concat(Enumerable.Repeat("\U0001F600", 12));
        Assert.Null(AccountRules.ValidatePassword(twelveEmojis));
        Assert.Equal("password_too_short", AccountRules.ValidatePassword(string.Concat(Enumerable.Repeat("\U0001F600", 11))));
        Assert.Equal("password_too_long", AccountRules.ValidatePassword(string.Concat(Enumerable.Repeat("\U0001F600", 129))));
    }

    [Fact]
    public void ValidatePassword_AllowsSpacesAndAnyCharacter_WithoutCompositionRules() =>
        Assert.Null(AccountRules.ValidatePassword("only lowercase words with spaces"));

    [Theory]
    [InlineData("café", "café")] // e + combining acute (macOS-style) = é (Windows-style)
    [InlineData("ＡＢＣ１２３", "ABC123")] // full-width letters and digits
    [InlineData("ﬁ", "fi")] // the "ﬁ" ligature
    [InlineData("x y", "x y")] // non-breaking space typed on some keyboards
    public void NormalizePassword_GivesTheSameTextForEquivalentInputs(string typed, string expected) =>
        Assert.Equal(expected, AccountRules.NormalizePassword(typed));

    [Fact]
    public void NormalizePassword_ReturnsNull_ForInvalidUnicode() =>
        Assert.Null(AccountRules.NormalizePassword("abc\uD800def")); // lone surrogate

    [Fact]
    public void PasswordLength_IsCheckedAfterNormalization()
    {
        // 6 "ﬃ" ligatures are 6 characters typed, but 18 once normalized ("ffi" each): long enough.
        string typed = new('ﬃ', 6);
        Assert.Equal(6, typed.Length);
        string normalized = AccountRules.NormalizePassword(typed)!;
        Assert.Null(AccountRules.ValidatePassword(normalized));
    }
}
