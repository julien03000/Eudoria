// Automation tests of the pure parsing functions (no network). Category: Eudoria.Core.Api.

#include "EudoriaCoreApi.h"
#include "EudoriaCoreSettings.h"
#include "EudoriaHttpTransport.h"
#include "Misc/AutomationTest.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"

#if WITH_DEV_AUTOMATION_TESTS

namespace EudoriaCoreApiTests
{
	constexpr EAutomationTestFlags Flags = EAutomationTestFlags::EditorContext | EAutomationTestFlags::ProductFilter;

	const TCHAR* ValidTokenPair = TEXT(R"({"accessToken":"aaa.bbb.ccc","tokenType":"Bearer","expiresIn":900,"refreshToken":"r-123","refreshExpiresIn":604800})");

	FString Problem(int32 Status, const TCHAR* FieldsAndCodes)
	{
		return FString::Printf(TEXT(R"({"type":"about:blank","title":"t","status":%d,"errors":[%s]})"), Status, FieldsAndCodes);
	}
}

using namespace EudoriaCoreApiTests;

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaApiSecureUrlTest, "Eudoria.Core.Api.SecureBaseUrl", Flags)
bool FEudoriaApiSecureUrlTest::RunTest(const FString&)
{
	TestTrue(TEXT("https with port and path"), EudoriaCoreApi::IsSecureBaseUrl(TEXT("https://localhost:5443/v1")));
	TestTrue(TEXT("upper-case scheme"), EudoriaCoreApi::IsSecureBaseUrl(TEXT("HTTPS://core.example.com/v1")));
	TestFalse(TEXT("http refused"), EudoriaCoreApi::IsSecureBaseUrl(TEXT("http://localhost:5080/v1")));
	TestFalse(TEXT("no host"), EudoriaCoreApi::IsSecureBaseUrl(TEXT("https:///v1")));
	TestFalse(TEXT("empty"), EudoriaCoreApi::IsSecureBaseUrl(FString()));
	TestFalse(TEXT("other scheme"), EudoriaCoreApi::IsSecureBaseUrl(TEXT("ftp://localhost/v1")));
	TestFalse(TEXT("credentials in URL"), EudoriaCoreApi::IsSecureBaseUrl(TEXT("https://user:pw@localhost/v1")));
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaApiConfiguredUrlTest, "Eudoria.Core.Api.ConfiguredUrlIsSecure", Flags)
bool FEudoriaApiConfiguredUrlTest::RunTest(const FString&)
{
	// Reads the real DefaultGame.ini value: an unquoted "https://..." is cut at "//" by the ini parser.
	const FString Url = GetDefault<UEudoriaCoreSettings>()->CoreBaseUrl;
	TestTrue(FString::Printf(TEXT("CoreBaseUrl '%s' is https"), *Url), EudoriaCoreApi::IsSecureBaseUrl(Url));
	TestTrue(TEXT("CoreBaseUrl carries the API version"), Url.EndsWith(TEXT("/v1")));
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaApiTransportRefusesHttpTest, "Eudoria.Core.Api.TransportRefusesHttp", Flags)
bool FEudoriaApiTransportRefusesHttpTest::RunTest(const FString&)
{
	FEudoriaHttpModuleTransport Transport(TEXT("http://localhost:5080/v1"), 10.0f);
	bool bCalled = false;
	EEudoriaTransportStatus Status = EEudoriaTransportStatus::Received;
	AddExpectedMessage(TEXT("must use https://"), EAutomationExpectedMessageFlags::Contains, 1);
	Transport.Send(FEudoriaHttpRequest{TEXT("GET"), TEXT("/me")}, [&](const FEudoriaHttpResponse& Response)
	{
		bCalled = true;
		Status = Response.TransportStatus;
	});
	TestTrue(TEXT("callback called at once"), bCalled);
	TestTrue(TEXT("nothing sent"), Status == EEudoriaTransportStatus::RefusedInsecureUrl);

	const FEudoriaApiError Error = EudoriaCoreApi::ParseError(FEudoriaHttpResponse{EEudoriaTransportStatus::RefusedInsecureUrl});
	TestTrue(TEXT("InsecureConfiguration"), Error.Kind == EEudoriaApiErrorKind::InsecureConfiguration);
	TestTrue(TEXT("message mentions https"), EudoriaCoreApi::ToUserMessage(Error).ToString().Contains(TEXT("https://")));
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaApiRegistrationTest, "Eudoria.Core.Api.Registration", Flags)
bool FEudoriaApiRegistrationTest::RunTest(const FString&)
{
	TestFalse(TEXT("202 = success"), EudoriaCoreApi::ParseRegistration(FEudoriaHttpResponse::Make(202, TEXT(R"({"status":"accepted"})"))).IsSet());

	const FEudoriaApiStatus Invalid = EudoriaCoreApi::ParseRegistration(FEudoriaHttpResponse::Make(400,
		Problem(400, TEXT(R"({"field":"pseudo","code":"pseudo_too_short"},{"field":"password","code":"password_too_short"})"))));
	if (TestTrue(TEXT("400 is an error"), Invalid.IsSet()))
	{
		TestTrue(TEXT("InvalidInput"), Invalid->Kind == EEudoriaApiErrorKind::InvalidInput);
		TestEqual(TEXT("both errors kept"), Invalid->Errors.Num(), 2);
		TestTrue(TEXT("pseudo_too_short"), Invalid->HasCode(TEXT("pseudo_too_short")));
		TestTrue(TEXT("password_too_short"), Invalid->HasCode(TEXT("password_too_short")));
		TestEqual(TEXT("two message lines"), EudoriaCoreApi::ToUserMessage(*Invalid).ToString(),
			FString(TEXT("Le pseudo doit contenir au moins 3 caractères.\nLe mot de passe doit contenir au moins 12 caractères.")));
	}

	const FEudoriaApiStatus Taken = EudoriaCoreApi::ParseRegistration(FEudoriaHttpResponse::Make(409, Problem(409, TEXT(R"({"field":"pseudo","code":"pseudo_taken"})"))));
	TestTrue(TEXT("409 Conflict"), Taken.IsSet() && Taken->Kind == EEudoriaApiErrorKind::Conflict);
	TestEqual(TEXT("pseudo taken message"), EudoriaCoreApi::ToUserMessage(*Taken).ToString(), FString(TEXT("Ce pseudo est déjà pris.")));

	const FEudoriaApiStatus Unexpected = EudoriaCoreApi::ParseRegistration(FEudoriaHttpResponse::Make(500, TEXT("oops")));
	TestTrue(TEXT("500 unexpected"), Unexpected.IsSet() && Unexpected->Kind == EEudoriaApiErrorKind::UnexpectedResponse);
	TestTrue(TEXT("200 is not the contract's answer"), EudoriaCoreApi::ParseRegistration(FEudoriaHttpResponse::Make(200)).IsSet());
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaApiLimitsTest, "Eudoria.Core.Api.Limits", Flags)
bool FEudoriaApiLimitsTest::RunTest(const FString&)
{
	const FEudoriaApiError TooMany = EudoriaCoreApi::ParseError(FEudoriaHttpResponse::Make(429,
		Problem(429, TEXT(R"({"field":"request","code":"too_many_attempts"})")), TEXT("900")));
	TestTrue(TEXT("429 kind"), TooMany.Kind == EEudoriaApiErrorKind::TooManyRequests);
	TestEqual(TEXT("Retry-After read"), TooMany.RetryAfterSeconds, 900);
	TestEqual(TEXT("minutes"), EudoriaCoreApi::ToUserMessage(TooMany).ToString(), FString(TEXT("Trop de tentatives. Réessaie dans 15 min.")));

	FEudoriaApiError Short = TooMany;
	Short.RetryAfterSeconds = 30;
	TestEqual(TEXT("seconds"), EudoriaCoreApi::ToUserMessage(Short).ToString(), FString(TEXT("Trop de tentatives. Réessaie dans 30 s.")));
	Short.RetryAfterSeconds = 61;
	TestEqual(TEXT("rounded up"), EudoriaCoreApi::ToUserMessage(Short).ToString(), FString(TEXT("Trop de tentatives. Réessaie dans 2 min.")));

	const FEudoriaApiError NoHeader = EudoriaCoreApi::ParseError(FEudoriaHttpResponse::Make(429, FString(), TEXT("soon")));
	TestEqual(TEXT("bad Retry-After ignored"), NoHeader.RetryAfterSeconds, -1);
	TestEqual(TEXT("later"), EudoriaCoreApi::ToUserMessage(NoHeader).ToString(), FString(TEXT("Trop de tentatives. Réessaie plus tard.")));

	const FEudoriaApiError Busy = EudoriaCoreApi::ParseError(FEudoriaHttpResponse::Make(503,
		Problem(503, TEXT(R"({"field":"request","code":"server_busy"})")), TEXT("2")));
	TestTrue(TEXT("503 kind"), Busy.Kind == EEudoriaApiErrorKind::ServerBusy);
	TestEqual(TEXT("503 Retry-After"), Busy.RetryAfterSeconds, 2);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaApiBrokenResponsesTest, "Eudoria.Core.Api.BrokenResponses", Flags)
bool FEudoriaApiBrokenResponsesTest::RunTest(const FString&)
{
	const FEudoriaApiError BadJson = EudoriaCoreApi::ParseError(FEudoriaHttpResponse::Make(400, TEXT("{not json")));
	TestTrue(TEXT("invalid JSON keeps the status kind"), BadJson.Kind == EEudoriaApiErrorKind::InvalidInput);
	TestEqual(TEXT("no error read"), BadJson.Errors.Num(), 0);
	TestEqual(TEXT("generic message"), EudoriaCoreApi::ToUserMessage(BadJson).ToString(), FString(TEXT("Une erreur est survenue. Réessaie plus tard.")));

	const FEudoriaApiError Empty = EudoriaCoreApi::ParseError(FEudoriaHttpResponse::Make(401));
	TestTrue(TEXT("empty 401"), Empty.Kind == EEudoriaApiErrorKind::Unauthorized && Empty.Errors.IsEmpty());

	const FEudoriaApiError Unknown = EudoriaCoreApi::ParseError(FEudoriaHttpResponse::Make(400, Problem(400, TEXT(R"({"field":"x","code":"brand_new_code"})"))));
	TestEqual(TEXT("unknown code -> generic"), EudoriaCoreApi::ToUserMessage(Unknown).ToString(), FString(TEXT("Une erreur est survenue. Réessaie plus tard.")));

	const FEudoriaApiError Duplicates = EudoriaCoreApi::ParseError(FEudoriaHttpResponse::Make(400,
		Problem(400, TEXT(R"({"field":"email","code":"email_invalid"},{"field":"email","code":"email_invalid"})"))));
	TestEqual(TEXT("duplicates merged"), EudoriaCoreApi::ToUserMessage(Duplicates).ToString(), FString(TEXT("Cette adresse e-mail n'est pas valide.")));

	const FEudoriaApiError Timeout = EudoriaCoreApi::ParseError(FEudoriaHttpResponse{EEudoriaTransportStatus::Failed});
	TestTrue(TEXT("no response = network"), Timeout.Kind == EEudoriaApiErrorKind::NetworkError);
	TestTrue(TEXT("network message"), EudoriaCoreApi::ToUserMessage(Timeout).ToString().StartsWith(TEXT("Impossible de joindre")));
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaApiTokenPairTest, "Eudoria.Core.Api.TokenPair", Flags)
bool FEudoriaApiTokenPairTest::RunTest(const FString&)
{
	const auto Valid = EudoriaCoreApi::ParseTokenPair(FEudoriaHttpResponse::Make(200, ValidTokenPair));
	if (TestTrue(TEXT("valid pair"), Valid.HasValue()))
	{
		TestEqual(TEXT("access"), Valid.GetValue().AccessToken, FString(TEXT("aaa.bbb.ccc")));
		TestEqual(TEXT("refresh"), Valid.GetValue().RefreshToken, FString(TEXT("r-123")));
		TestEqual(TEXT("expiresIn"), Valid.GetValue().ExpiresInSeconds, 900);
	}

	const auto Missing = EudoriaCoreApi::ParseTokenPair(FEudoriaHttpResponse::Make(200, TEXT(R"({"accessToken":"a.b.c","tokenType":"Bearer","expiresIn":900})")));
	TestTrue(TEXT("missing refresh token"), Missing.HasError() && Missing.GetError().Kind == EEudoriaApiErrorKind::UnexpectedResponse);

	const auto WrongType = EudoriaCoreApi::ParseTokenPair(FEudoriaHttpResponse::Make(200, FString(ValidTokenPair).Replace(TEXT("Bearer"), TEXT("Basic"))));
	TestTrue(TEXT("wrong token type"), WrongType.HasError());

	const auto NotJson = EudoriaCoreApi::ParseTokenPair(FEudoriaHttpResponse::Make(200, TEXT("<html>")));
	TestTrue(TEXT("not JSON"), NotJson.HasError() && NotJson.GetError().Kind == EEudoriaApiErrorKind::UnexpectedResponse);

	const auto Wrong = EudoriaCoreApi::ParseTokenPair(FEudoriaHttpResponse::Make(401, Problem(401, TEXT(R"({"field":"credentials","code":"invalid_credentials"})"))));
	if (TestTrue(TEXT("401"), Wrong.HasError()))
	{
		TestTrue(TEXT("Unauthorized"), Wrong.GetError().Kind == EEudoriaApiErrorKind::Unauthorized);
		TestEqual(TEXT("neutral message"), EudoriaCoreApi::ToUserMessage(Wrong.GetError()).ToString(), FString(TEXT("E-mail ou mot de passe incorrect.")));
	}
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaApiProfileTest, "Eudoria.Core.Api.Profile", Flags)
bool FEudoriaApiProfileTest::RunTest(const FString&)
{
	const auto Valid = EudoriaCoreApi::ParseProfile(FEudoriaHttpResponse::Make(200,
		TEXT(R"({"id":"0193f5a2-7c1e-7b3a-9d4e-2f6a8b0c1d2e","pseudo":"Ana_42","createdAt":"2026-11-24T19:30:00+00:00"})")));
	if (TestTrue(TEXT("valid profile"), Valid.HasValue()))
	{
		TestEqual(TEXT("pseudo"), Valid.GetValue().Pseudo, FString(TEXT("Ana_42")));
		TestEqual(TEXT("date"), Valid.GetValue().CreatedAt, FDateTime(2026, 11, 24, 19, 30, 0));
	}
	const auto BadDate = EudoriaCoreApi::ParseProfile(FEudoriaHttpResponse::Make(200, TEXT(R"({"id":"x","pseudo":"Ana_42","createdAt":"yesterday"})")));
	TestTrue(TEXT("bad date"), BadDate.HasError() && BadDate.GetError().Kind == EEudoriaApiErrorKind::UnexpectedResponse);
	const auto NoBody = EudoriaCoreApi::ParseProfile(FEudoriaHttpResponse::Make(401));
	TestTrue(TEXT("401 without body"), NoBody.HasError() && NoBody.GetError().Kind == EEudoriaApiErrorKind::Unauthorized);
	TestFalse(TEXT("logout 204"), EudoriaCoreApi::ParseLogout(FEudoriaHttpResponse::Make(204)).IsSet());
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaApiBodiesTest, "Eudoria.Core.Api.RequestBodies", Flags)
bool FEudoriaApiBodiesTest::RunTest(const FString&)
{
	const FString Tricky = TEXT("quote\" back\\slash é\nline");
	const FString Body = EudoriaCoreApi::MakeLoginBody(TEXT("ana@example.com"), Tricky);
	TSharedPtr<FJsonObject> Parsed;
	const bool bParsed = FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Body), Parsed) && Parsed.IsValid();
	if (TestTrue(TEXT("valid JSON"), bParsed))
	{
		TestEqual(TEXT("password intact"), Parsed->GetStringField(TEXT("password")), Tricky);
		TestEqual(TEXT("email"), Parsed->GetStringField(TEXT("email")), FString(TEXT("ana@example.com")));
		TestEqual(TEXT("only two fields (additionalProperties: false)"), Parsed->Values.Num(), 2);
	}
	return true;
}

#endif // WITH_DEV_AUTOMATION_TESTS
