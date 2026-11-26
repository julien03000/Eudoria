// Automation tests of the low-level ticket pieces: strict Base64 URL-safe, strict UTF-8, strict JSON,
// ES256 on the engine's OpenSSL. Category: Eudoria.EntryTickets.Jwt.

#include "EntryTickets/EudoriaEs256.h"
#include "EntryTickets/EudoriaJwt.h"
#include "EudoriaTicketTestVectors.h"
#include "Misc/AutomationTest.h"

#if WITH_DEV_AUTOMATION_TESTS && EUDORIA_WITH_TICKET_VERIFIER

namespace EudoriaJwtTests
{
	constexpr EAutomationTestFlags JwtTestFlags = EAutomationTestFlags::EditorContext | EAutomationTestFlags::ProductFilter;

	TArray<uint8> Bytes(std::initializer_list<uint8> Values) { return TArray<uint8>(Values); }
}

using namespace EudoriaJwtTests;

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaJwtBase64Test, "Eudoria.EntryTickets.Jwt.Base64UrlIsStrict", JwtTestFlags)
bool FEudoriaJwtBase64Test::RunTest(const FString&)
{
	TArray<uint8> Out;
	TestTrue(TEXT("QQ"), EudoriaJwt::DecodeBase64Url(TEXT("QQ"), Out) && Out == Bytes({65}));
	TestTrue(TEXT("QUI"), EudoriaJwt::DecodeBase64Url(TEXT("QUI"), Out) && Out == Bytes({65, 66}));
	TestTrue(TEXT("QUJD"), EudoriaJwt::DecodeBase64Url(TEXT("QUJD"), Out) && Out == Bytes({65, 66, 67}));
	TestTrue(TEXT("empty"), EudoriaJwt::DecodeBase64Url(TEXT(""), Out) && Out.IsEmpty());
	TestTrue(TEXT("URL-safe characters"), EudoriaJwt::DecodeBase64Url(TEXT("-_8"), Out) && Out == Bytes({0xFB, 0xFF}));

	// Same refusals as the .NET verifier.
	for (const TCHAR* Refused : {TEXT("QR"), TEXT("QUJ"), TEXT("Q"), TEXT("QQ=="), TEXT("QQ="), TEXT("Q+"), TEXT("Q/"), TEXT("Q Q"), TEXT("QUJDR")})
	{
		TestFalse(FString::Printf(TEXT("refused: '%s'"), Refused), EudoriaJwt::DecodeBase64Url(Refused, Out));
	}

	const TArray<uint8> Sample = Bytes({0x00, 0xFB, 0xFF, 0x10, 0x80});
	TestTrue(TEXT("round trip"), EudoriaJwt::DecodeBase64Url(EudoriaJwt::EncodeBase64Url(Sample), Out) && Out == Sample);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaJwtUtf8Test, "Eudoria.EntryTickets.Jwt.Utf8IsStrict", JwtTestFlags)
bool FEudoriaJwtUtf8Test::RunTest(const FString&)
{
	FString Text;
	TestTrue(TEXT("é"), EudoriaJwt::DecodeUtf8(Bytes({'a', 0xC3, 0xA9}), Text) && Text == FString(TEXT("aé")));
	TestTrue(TEXT("U+10FFFF"), EudoriaJwt::DecodeUtf8(Bytes({0xF4, 0x8F, 0xBF, 0xBF}), Text));
	TestFalse(TEXT("0xC3 0x28"), EudoriaJwt::DecodeUtf8(Bytes({0xC3, 0x28}), Text));
	TestFalse(TEXT("overlong /"), EudoriaJwt::DecodeUtf8(Bytes({0xC0, 0xAF}), Text));
	TestFalse(TEXT("overlong 3 bytes"), EudoriaJwt::DecodeUtf8(Bytes({0xE0, 0x80, 0xAF}), Text));
	TestFalse(TEXT("surrogate"), EudoriaJwt::DecodeUtf8(Bytes({0xED, 0xA0, 0x80}), Text));
	TestFalse(TEXT("above U+10FFFF"), EudoriaJwt::DecodeUtf8(Bytes({0xF4, 0x90, 0x80, 0x80}), Text));
	TestFalse(TEXT("truncated"), EudoriaJwt::DecodeUtf8(Bytes({0xE2, 0x82}), Text));
	TestFalse(TEXT("lone continuation"), EudoriaJwt::DecodeUtf8(Bytes({0x80}), Text));
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaJwtJsonTest, "Eudoria.EntryTickets.Jwt.JsonObjectIsStrict", JwtTestFlags)
bool FEudoriaJwtJsonTest::RunTest(const FString&)
{
	using EudoriaJwt::FJsonMember;
	TMap<FString, FJsonMember> Members;

	TestTrue(TEXT("object"), EudoriaJwt::ReadObject(TEXT(R"({"a":"x","n":60,"f":60.5,"l":["y"],"o":{"k":1},"b":true})"), Members));
	TestEqual(TEXT("six members"), Members.Num(), 6);
	TestTrue(TEXT("string"), Members.Contains(TEXT("a")) && Members[TEXT("a")].Kind == FJsonMember::EKind::String && Members[TEXT("a")].Text == TEXT("x"));
	TestTrue(TEXT("integer text"), Members.Contains(TEXT("n")) && Members[TEXT("n")].Kind == FJsonMember::EKind::Number && Members[TEXT("n")].Text == TEXT("60"));
	TestTrue(TEXT("fraction text kept"), Members.Contains(TEXT("f")) && Members[TEXT("f")].Text == TEXT("60.5"));
	TestTrue(TEXT("array is other"), Members.Contains(TEXT("l")) && Members[TEXT("l")].Kind == FJsonMember::EKind::Other);
	TestTrue(TEXT("boolean is other"), Members.Contains(TEXT("b")) && Members[TEXT("b")].Kind == FJsonMember::EKind::Other);

	TestFalse(TEXT("duplicate member"), EudoriaJwt::ReadObject(TEXT(R"({"a":"x","a":"y"})"), Members));
	TestFalse(TEXT("duplicate nested member"), EudoriaJwt::ReadObject(TEXT(R"({"o":{"k":1,"k":2}})"), Members));
	TestFalse(TEXT("array root"), EudoriaJwt::ReadObject(TEXT("[1,2]"), Members));
	TestFalse(TEXT("not json"), EudoriaJwt::ReadObject(TEXT("not json"), Members));
	TestFalse(TEXT("trailing input"), EudoriaJwt::ReadObject(TEXT(R"({"a":1}{"b":2})"), Members));
	TestFalse(TEXT("unfinished"), EudoriaJwt::ReadObject(TEXT(R"({"a":1)"), Members));
	TestFalse(TEXT("too deep"), EudoriaJwt::ReadObject(TEXT(R"({"a":[[[[1]]]]})"), Members));
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaJwtEs256Test, "Eudoria.EntryTickets.Jwt.Es256OnEngineOpenSsl", JwtTestFlags)
bool FEudoriaJwtEs256Test::RunTest(const FString&)
{
	EudoriaTicketTestVectors::FVectors Vectors;
	if (!TestTrue(TEXT("shared test vectors loaded"), EudoriaTicketTestVectors::Load(Vectors)))
	{
		return false;
	}
	const EudoriaTicketTestVectors::FVector* Valid = Vectors.Find(TEXT("valid"));
	if (!TestNotNull(TEXT("vector 'valid'"), Valid))
	{
		return false;
	}

	// The published key and the parts of a ticket signed by it, decoded by hand.
	TArray<uint8> X, Y, Signature;
	TestTrue(TEXT("x"), EudoriaJwt::DecodeBase64Url(Vectors.KeyX, X));
	TestTrue(TEXT("y"), EudoriaJwt::DecodeBase64Url(Vectors.KeyY, Y));
	int32 LastDot = INDEX_NONE;
	Valid->Ticket.FindLastChar(TEXT('.'), LastDot);
	const FTCHARToUTF8 SigningInput(*Valid->Ticket.Left(LastDot));
	const TConstArrayView<uint8> Input(reinterpret_cast<const uint8*>(SigningInput.Get()), SigningInput.Length());
	TestTrue(TEXT("signature"), EudoriaJwt::DecodeBase64Url(Valid->Ticket.RightChop(LastDot + 1), Signature) && Signature.Num() == 64);

	TestTrue(TEXT("valid key"), EudoriaEs256::IsValidPublicKey(X, Y));
	TestTrue(TEXT("valid signature"), EudoriaEs256::Verify(X, Y, Input, Signature));

	TArray<uint8> Swapped;
	Swapped.Append(Signature.GetData() + 32, 32);
	Swapped.Append(Signature.GetData(), 32);
	TestFalse(TEXT("r and s swapped"), EudoriaEs256::Verify(X, Y, Input, Swapped));

	TArray<uint8> Flipped = Signature;
	Flipped[10] ^= 0x01;
	TestFalse(TEXT("one bit changed"), EudoriaEs256::Verify(X, Y, Input, Flipped));
	TestFalse(TEXT("63 bytes"), EudoriaEs256::Verify(X, Y, Input, TConstArrayView<uint8>(Signature.GetData(), 63)));

	TArray<uint8> OtherInput(Input.GetData(), Input.Num());
	OtherInput.Last() ^= 0x01;
	TestFalse(TEXT("other signed data"), EudoriaEs256::Verify(X, Y, OtherInput, Signature));

	TArray<uint8> OffCurveY;
	OffCurveY.SetNumZeroed(32);
	TestFalse(TEXT("point off the curve"), EudoriaEs256::IsValidPublicKey(X, OffCurveY));
	TestFalse(TEXT("short coordinate"), EudoriaEs256::IsValidPublicKey(TConstArrayView<uint8>(X.GetData(), 31), Y));
	return true;
}

#endif // WITH_DEV_AUTOMATION_TESTS && EUDORIA_WITH_TICKET_VERIFIER
