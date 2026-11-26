// Automation tests of the client side of entry tickets: FEudoriaSession::RequestEntryTicket on a fake core.
// Category: Eudoria.Core.EntryTicketRequest.

#include "EudoriaSession.h"
#include "EudoriaTestSupport.h"
#include "Misc/AutomationTest.h"

#if WITH_DEV_AUTOMATION_TESTS

namespace EudoriaEntryTicketRequestTests
{
	using EudoriaTestSupport::FFakeTransport;
	using EudoriaTestSupport::FLogCapture;

	constexpr EAutomationTestFlags RequestTestFlags = EAutomationTestFlags::EditorContext | EAutomationTestFlags::ProductFilter;

	const TCHAR* TicketPath = TEXT("/worlds/sandbox/entry-tickets");
	const TCHAR* FirstAccess = TEXT("first.access.token");
	const TCHAR* SecondAccess = TEXT("second.access.token");
	const TCHAR* FakeTicket = TEXT("eyJhbGciOiJFUzI1NiJ9.eyJzdWIiOiJ4In0.c2lnbmF0dXJlLXNlY3JldA");

	FString Tokens(const TCHAR* Access, const TCHAR* Refresh)
	{
		return FString::Printf(TEXT(R"({"accessToken":"%s","tokenType":"Bearer","expiresIn":900,"refreshToken":"%s","refreshExpiresIn":604800})"), Access, Refresh);
	}

	FString TicketJson() { return FString::Printf(TEXT(R"({"ticket":"%s","expiresIn":60})"), FakeTicket); }

	TSharedRef<FEudoriaSession> LoggedIn(const TSharedRef<FFakeTransport>& Transport)
	{
		TSharedRef<FEudoriaSession> Session = MakeShared<FEudoriaSession>(Transport);
		Session->Login(TEXT("ana@example.com"), TEXT("a-passphrase-for-tests"), [](const FEudoriaApiStatus&) {});
		Transport->Answer(TEXT("/auth/login"), FEudoriaHttpResponse::Make(200, Tokens(FirstAccess, TEXT("refresh-one"))));
		return Session;
	}

	using FResult = TOptional<TValueOrError<FEudoriaEntryTicket, FEudoriaApiError>>;

	FEudoriaSession::FEntryTicketCallback Record(FResult& Out)
	{
		return [&Out](const TValueOrError<FEudoriaEntryTicket, FEudoriaApiError>& Result) { Out.Emplace(Result); };
	}
}

using namespace EudoriaEntryTicketRequestTests;

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaTicketRequestTest, "Eudoria.Core.EntryTicketRequest.ReturnsTheTicket", RequestTestFlags)
bool FEudoriaTicketRequestTest::RunTest(const FString&)
{
	const TSharedRef<FFakeTransport> Transport = MakeShared<FFakeTransport>();
	const TSharedRef<FEudoriaSession> Session = LoggedIn(Transport);

	FResult Result;
	Session->RequestEntryTicket(TEXT("sandbox"), Record(Result));
	const FEudoriaHttpRequest Sent = Transport->Answer(TicketPath, FEudoriaHttpResponse::Make(200, TicketJson()));

	TestEqual(TEXT("POST"), Sent.Verb, FString(TEXT("POST")));
	TestEqual(TEXT("with the access token"), Sent.BearerToken, FString(FirstAccess));
	TestTrue(TEXT("no body"), Sent.JsonBody.IsEmpty());
	if (TestTrue(TEXT("ticket received"), Result.IsSet() && Result->HasValue()))
	{
		TestEqual(TEXT("ticket"), Result->GetValue().Ticket, FString(FakeTicket));
		TestEqual(TEXT("lifetime"), Result->GetValue().ExpiresInSeconds, 60);
	}
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaTicketRequestRenewsTest, "Eudoria.Core.EntryTicketRequest.RenewsOnceOn401", RequestTestFlags)
bool FEudoriaTicketRequestRenewsTest::RunTest(const FString&)
{
	const TSharedRef<FFakeTransport> Transport = MakeShared<FFakeTransport>();
	const TSharedRef<FEudoriaSession> Session = LoggedIn(Transport);

	FResult Result;
	Session->RequestEntryTicket(TEXT("sandbox"), Record(Result));
	Transport->Answer(TicketPath, FEudoriaHttpResponse::Make(401)); // e.g. an access token typed "JWT" before 1.6
	TestEqual(TEXT("one renewal"), Transport->CountSent(TEXT("/auth/refresh")), 1);
	Transport->Answer(TEXT("/auth/refresh"), FEudoriaHttpResponse::Make(200, Tokens(SecondAccess, TEXT("refresh-two"))));
	const FEudoriaHttpRequest Replayed = Transport->Answer(TicketPath, FEudoriaHttpResponse::Make(200, TicketJson()));

	TestEqual(TEXT("replayed with the new token"), Replayed.BearerToken, FString(SecondAccess));
	TestTrue(TEXT("ticket received"), Result.IsSet() && Result->HasValue());
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaTicketRequestErrorsTest, "Eudoria.Core.EntryTicketRequest.Errors", RequestTestFlags)
bool FEudoriaTicketRequestErrorsTest::RunTest(const FString&)
{
	const TSharedRef<FFakeTransport> Transport = MakeShared<FFakeTransport>();
	const TSharedRef<FEudoriaSession> Session = LoggedIn(Transport);

	// Unknown world: 404 world_unknown, with a message for the player.
	FResult Unknown;
	Session->RequestEntryTicket(TEXT("nowhere"), Record(Unknown));
	Transport->Answer(TEXT("/worlds/nowhere/entry-tickets"), FEudoriaHttpResponse::Make(404,
		TEXT(R"({"type":"about:blank","title":"Not Found","status":404,"errors":[{"field":"worldId","code":"world_unknown"}]})")));
	if (TestTrue(TEXT("404 refused"), Unknown.IsSet() && Unknown->HasError()))
	{
		TestTrue(TEXT("NotFound"), Unknown->GetError().Kind == EEudoriaApiErrorKind::NotFound && Unknown->GetError().HasCode(TEXT("world_unknown")));
		TestEqual(TEXT("message"), EudoriaCoreApi::ToUserMessage(Unknown->GetError()).ToString(), FString(TEXT("Ce monde n'existe pas (ou plus).")));
	}

	// Malformed identifiers never reach the network (no value goes unchecked into a URL path).
	const int32 SentBefore = Transport->Sent.Num();
	for (const TCHAR* Bad : {TEXT("Sandbox"), TEXT("../me"), TEXT("a/b"), TEXT("-a"), TEXT(""), TEXT("a?b=c")})
	{
		FResult Refused;
		Session->RequestEntryTicket(Bad, Record(Refused));
		TestTrue(FString::Printf(TEXT("'%s' refused locally"), Bad),
			Refused.IsSet() && Refused->HasError() && Refused->GetError().Kind == EEudoriaApiErrorKind::InvalidInput && Refused->GetError().HasCode(TEXT("world_id_invalid")));
	}
	TestEqual(TEXT("nothing sent"), Transport->Sent.Num(), SentBefore);
	TestTrue(TEXT("64 characters accepted"), EudoriaCoreApi::IsValidWorldId(FString::ChrN(64, TEXT('a'))));
	TestFalse(TEXT("65 characters refused"), EudoriaCoreApi::IsValidWorldId(FString::ChrN(65, TEXT('a'))));

	// Not logged in: the session is over, nothing is sent.
	const TSharedRef<FEudoriaSession> LoggedOut = MakeShared<FEudoriaSession>(Transport);
	FResult NoSession;
	LoggedOut->RequestEntryTicket(TEXT("sandbox"), Record(NoSession));
	TestTrue(TEXT("SessionEnded"), NoSession.IsSet() && NoSession->HasError() && NoSession->GetError().Kind == EEudoriaApiErrorKind::SessionEnded);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaTicketRequestNotLoggedTest, "Eudoria.Core.EntryTicketRequest.TicketNeverLogged", RequestTestFlags)
bool FEudoriaTicketRequestNotLoggedTest::RunTest(const FString&)
{
	FLogCapture Capture;
	{
		const TSharedRef<FFakeTransport> Transport = MakeShared<FFakeTransport>();
		const TSharedRef<FEudoriaSession> Session = LoggedIn(Transport);
		FResult Result;
		Session->RequestEntryTicket(TEXT("sandbox"), Record(Result));
		Transport->Answer(TicketPath, FEudoriaHttpResponse::Make(401));
		Transport->Answer(TEXT("/auth/refresh"), FEudoriaHttpResponse::Make(200, Tokens(SecondAccess, TEXT("refresh-two"))));
		Transport->Answer(TicketPath, FEudoriaHttpResponse::Make(200, TicketJson()));
	}
	const FString Logged = Capture.Get();
	TestTrue(TEXT("the scenario did log something"), Logged.Contains(TEXT("renewed")));
	TArray<FString> Parts;
	FString(FakeTicket).ParseIntoArray(Parts, TEXT("."));
	for (const FString& Part : Parts)
	{
		TestFalse(TEXT("no part of the ticket in the logs"), Logged.Contains(Part));
	}
	return true;
}

#endif // WITH_DEV_AUTOMATION_TESTS
