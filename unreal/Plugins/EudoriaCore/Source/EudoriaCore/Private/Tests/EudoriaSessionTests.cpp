// Automation tests of the session, with a fake transport that holds every request until the test answers it.
// This is how "simultaneous" 401s are reproduced deterministically. Category: Eudoria.Core.Session.

#include "EudoriaSession.h"
#include "EudoriaTestSupport.h"
#include "Misc/AutomationTest.h"

#if WITH_DEV_AUTOMATION_TESTS

namespace EudoriaSessionTests
{
	using EudoriaTestSupport::FFakeTransport;
	using EudoriaTestSupport::FLogCapture;

	constexpr EAutomationTestFlags SessionTestFlags = EAutomationTestFlags::EditorContext | EAutomationTestFlags::ProductFilter;

	const TCHAR* Password = TEXT("S3cret-passphrase-for-tests");
	const TCHAR* Access1 = TEXT("access.one.token");
	const TCHAR* Refresh1 = TEXT("refresh-one-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
	const TCHAR* Access2 = TEXT("access.two.token");
	const TCHAR* Refresh2 = TEXT("refresh-two-BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB");
	const TCHAR* ProfileJson = TEXT(R"({"id":"0193f5a2-7c1e-7b3a-9d4e-2f6a8b0c1d2e","pseudo":"Ana_42","createdAt":"2026-11-24T19:30:00+00:00"})");

	FString TokenJson(const TCHAR* Access, const TCHAR* Refresh)
	{
		return FString::Printf(TEXT(R"({"accessToken":"%s","tokenType":"Bearer","expiresIn":900,"refreshToken":"%s","refreshExpiresIn":604800})"), Access, Refresh);
	}

	struct FProfileOutcome
	{
		bool bCalled = false;
		bool bSuccess = false;
		EEudoriaApiErrorKind Kind = EEudoriaApiErrorKind::UnexpectedResponse;
		FString Pseudo;
	};

	FEudoriaSession::FProfileCallback Record(FProfileOutcome& Outcome)
	{
		return [&Outcome](const TValueOrError<FEudoriaProfile, FEudoriaApiError>& Result)
		{
			Outcome.bCalled = true;
			Outcome.bSuccess = Result.HasValue();
			if (Result.HasValue())
			{
				Outcome.Pseudo = Result.GetValue().Pseudo;
			}
			else
			{
				Outcome.Kind = Result.GetError().Kind;
			}
		};
	}

	/** A logged-in session (Access1 / Refresh1) on a fresh fake transport. */
	TSharedRef<FEudoriaSession> MakeLoggedIn(const TSharedRef<FFakeTransport>& Transport)
	{
		TSharedRef<FEudoriaSession> Session = MakeShared<FEudoriaSession>(Transport);
		Session->Login(TEXT("ana@example.com"), Password, [](const FEudoriaApiStatus&) {});
		Transport->Answer(TEXT("/auth/login"), FEudoriaHttpResponse::Make(200, TokenJson(Access1, Refresh1)));
		return Session;
	}

}

using namespace EudoriaSessionTests;

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaSessionLoginTest, "Eudoria.Core.Session.LoginAndProfile", SessionTestFlags)
bool FEudoriaSessionLoginTest::RunTest(const FString&)
{
	const TSharedRef<FFakeTransport> Transport = MakeShared<FFakeTransport>();
	const TSharedRef<FEudoriaSession> Session = MakeLoggedIn(Transport);
	TestTrue(TEXT("logged in"), Session->IsLoggedIn());

	FProfileOutcome Outcome;
	Session->FetchProfile(Record(Outcome));
	const FEudoriaHttpRequest Me = Transport->Answer(TEXT("/me"), FEudoriaHttpResponse::Make(200, ProfileJson));
	TestEqual(TEXT("bearer token sent"), Me.BearerToken, FString(Access1));
	TestTrue(TEXT("profile received"), Outcome.bCalled && Outcome.bSuccess);
	TestEqual(TEXT("pseudo"), Outcome.Pseudo, FString(TEXT("Ana_42")));
	TestEqual(TEXT("no renewal needed"), Transport->CountSent(TEXT("/auth/refresh")), 0);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaSessionSingleRenewalTest, "Eudoria.Core.Session.TwoSimultaneous401sRenewOnce", SessionTestFlags)
bool FEudoriaSessionSingleRenewalTest::RunTest(const FString&)
{
	const TSharedRef<FFakeTransport> Transport = MakeShared<FFakeTransport>();
	const TSharedRef<FEudoriaSession> Session = MakeLoggedIn(Transport);

	FProfileOutcome First, Second;
	Session->FetchProfile(Record(First));
	Session->FetchProfile(Record(Second));
	TestEqual(TEXT("two requests in flight"), Transport->CountPending(TEXT("/me")), 2);

	// Both get 401 before any renewal has started: they must share one /auth/refresh.
	Transport->Answer(TEXT("/me"), FEudoriaHttpResponse::Make(401));
	Transport->Answer(TEXT("/me"), FEudoriaHttpResponse::Make(401));
	TestEqual(TEXT("ONE renewal for two 401s"), Transport->CountSent(TEXT("/auth/refresh")), 1);
	TestFalse(TEXT("nobody answered yet"), First.bCalled || Second.bCalled);

	const FEudoriaHttpRequest Refresh = Transport->Answer(TEXT("/auth/refresh"), FEudoriaHttpResponse::Make(200, TokenJson(Access2, Refresh2)));
	TestTrue(TEXT("renewal used the refresh token"), Refresh.JsonBody.Contains(Refresh1));

	TestEqual(TEXT("both replayed"), Transport->CountPending(TEXT("/me")), 2);
	const FEudoriaHttpRequest ReplayA = Transport->Answer(TEXT("/me"), FEudoriaHttpResponse::Make(200, ProfileJson));
	const FEudoriaHttpRequest ReplayB = Transport->Answer(TEXT("/me"), FEudoriaHttpResponse::Make(200, ProfileJson));
	TestEqual(TEXT("replay A uses the new token"), ReplayA.BearerToken, FString(Access2));
	TestEqual(TEXT("replay B uses the new token"), ReplayB.BearerToken, FString(Access2));
	TestTrue(TEXT("first succeeded"), First.bCalled && First.bSuccess);
	TestTrue(TEXT("second succeeded"), Second.bCalled && Second.bSuccess);
	TestEqual(TEXT("still ONE renewal in total"), Transport->CountSent(TEXT("/auth/refresh")), 1);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaSessionRenewalFailsTest, "Eudoria.Core.Session.TwoSimultaneous401sRenewalFails", SessionTestFlags)
bool FEudoriaSessionRenewalFailsTest::RunTest(const FString&)
{
	const TSharedRef<FFakeTransport> Transport = MakeShared<FFakeTransport>();
	const TSharedRef<FEudoriaSession> Session = MakeLoggedIn(Transport);

	FProfileOutcome First, Second;
	Session->FetchProfile(Record(First));
	Session->FetchProfile(Record(Second));
	Transport->Answer(TEXT("/me"), FEudoriaHttpResponse::Make(401));
	Transport->Answer(TEXT("/me"), FEudoriaHttpResponse::Make(401));
	Transport->Answer(TEXT("/auth/refresh"), FEudoriaHttpResponse::Make(401,
		TEXT(R"({"type":"about:blank","title":"Unauthorized","status":401,"errors":[{"field":"refreshToken","code":"refresh_token_invalid"}]})")));

	TestEqual(TEXT("ONE renewal"), Transport->CountSent(TEXT("/auth/refresh")), 1);
	TestTrue(TEXT("first: session ended"), First.bCalled && !First.bSuccess && First.Kind == EEudoriaApiErrorKind::SessionEnded);
	TestTrue(TEXT("second: session ended"), Second.bCalled && !Second.bSuccess && Second.Kind == EEudoriaApiErrorKind::SessionEnded);
	TestFalse(TEXT("tokens forgotten"), Session->IsLoggedIn());
	TestEqual(TEXT("nothing replayed"), Transport->CountPending(TEXT("/me")), 0);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaSessionStaleTokenTest, "Eudoria.Core.Session.LateStale401ReplaysWithoutRenewal", SessionTestFlags)
bool FEudoriaSessionStaleTokenTest::RunTest(const FString&)
{
	const TSharedRef<FFakeTransport> Transport = MakeShared<FFakeTransport>();
	const TSharedRef<FEudoriaSession> Session = MakeLoggedIn(Transport);

	FProfileOutcome First, Late;
	Session->FetchProfile(Record(First));
	Session->FetchProfile(Record(Late));
	Transport->Answer(TEXT("/me"), FEudoriaHttpResponse::Make(401));
	Transport->Answer(TEXT("/auth/refresh"), FEudoriaHttpResponse::Make(200, TokenJson(Access2, Refresh2)));
	// The second request, sent with the old token, only now gets its 401: the renewal is already done.
	Transport->Answer(TEXT("/me"), FEudoriaHttpResponse::Make(401));

	TestEqual(TEXT("no second renewal"), Transport->CountSent(TEXT("/auth/refresh")), 1);
	TestEqual(TEXT("two replays waiting"), Transport->CountPending(TEXT("/me")), 2);
	Transport->Answer(TEXT("/me"), FEudoriaHttpResponse::Make(200, ProfileJson));
	const FEudoriaHttpRequest Replay = Transport->Answer(TEXT("/me"), FEudoriaHttpResponse::Make(200, ProfileJson));
	TestEqual(TEXT("late request replayed with the current token"), Replay.BearerToken, FString(Access2));
	TestTrue(TEXT("both succeeded"), First.bSuccess && Late.bSuccess);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaSessionNoLoopTest, "Eudoria.Core.Session.ReplayRefusedAgainEndsSession", SessionTestFlags)
bool FEudoriaSessionNoLoopTest::RunTest(const FString&)
{
	const TSharedRef<FFakeTransport> Transport = MakeShared<FFakeTransport>();
	const TSharedRef<FEudoriaSession> Session = MakeLoggedIn(Transport);

	FProfileOutcome Outcome;
	Session->FetchProfile(Record(Outcome));
	Transport->Answer(TEXT("/me"), FEudoriaHttpResponse::Make(401));
	Transport->Answer(TEXT("/auth/refresh"), FEudoriaHttpResponse::Make(200, TokenJson(Access2, Refresh2)));
	AddExpectedMessage(TEXT("refused again after renewal"), EAutomationExpectedMessageFlags::Contains, 1);
	Transport->Answer(TEXT("/me"), FEudoriaHttpResponse::Make(401)); // the replay is refused too

	TestEqual(TEXT("no second renewal"), Transport->CountSent(TEXT("/auth/refresh")), 1);
	TestTrue(TEXT("session ended"), Outcome.bCalled && Outcome.Kind == EEudoriaApiErrorKind::SessionEnded);
	TestFalse(TEXT("tokens forgotten"), Session->IsLoggedIn());
	TestEqual(TEXT("nothing left"), Transport->Pending.Num(), 0);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaSessionLogoutTest, "Eudoria.Core.Session.LogoutForgetsTokens", SessionTestFlags)
bool FEudoriaSessionLogoutTest::RunTest(const FString&)
{
	const TSharedRef<FFakeTransport> Transport = MakeShared<FFakeTransport>();
	const TSharedRef<FEudoriaSession> Session = MakeLoggedIn(Transport);

	bool bLoggedOut = false;
	Session->Logout([&bLoggedOut](const FEudoriaApiStatus& Result) { bLoggedOut = !Result.IsSet(); });
	TestFalse(TEXT("tokens forgotten at once"), Session->IsLoggedIn());
	const FEudoriaHttpRequest Logout = Transport->Answer(TEXT("/auth/logout"), FEudoriaHttpResponse::Make(204));
	TestTrue(TEXT("core told with the refresh token"), Logout.JsonBody.Contains(Refresh1));
	TestTrue(TEXT("logout confirmed"), bLoggedOut);

	FProfileOutcome After;
	Session->FetchProfile(Record(After));
	TestTrue(TEXT("profile refused locally"), After.bCalled && After.Kind == EEudoriaApiErrorKind::SessionEnded);
	TestEqual(TEXT("no request sent without a session"), Transport->CountSent(TEXT("/me")), 0);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaSessionLogoutDuringRenewalTest, "Eudoria.Core.Session.LogoutDuringRenewal", SessionTestFlags)
bool FEudoriaSessionLogoutDuringRenewalTest::RunTest(const FString&)
{
	const TSharedRef<FFakeTransport> Transport = MakeShared<FFakeTransport>();
	const TSharedRef<FEudoriaSession> Session = MakeLoggedIn(Transport);

	FProfileOutcome Outcome;
	Session->FetchProfile(Record(Outcome));
	Transport->Answer(TEXT("/me"), FEudoriaHttpResponse::Make(401));
	Session->Logout([](const FEudoriaApiStatus&) {});
	TestTrue(TEXT("waiting request failed at logout"), Outcome.bCalled && Outcome.Kind == EEudoriaApiErrorKind::SessionEnded);

	// The renewal answer arrives after the logout: it must not bring the session back.
	Transport->Answer(TEXT("/auth/refresh"), FEudoriaHttpResponse::Make(200, TokenJson(Access2, Refresh2)));
	TestFalse(TEXT("still logged out"), Session->IsLoggedIn());
	TestEqual(TEXT("nothing replayed"), Transport->CountPending(TEXT("/me")), 0);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaSessionNoSecretsInLogTest, "Eudoria.Core.Session.NoSecretInLogs", SessionTestFlags)
bool FEudoriaSessionNoSecretsInLogTest::RunTest(const FString&)
{
	FLogCapture Capture;
	{
		const TSharedRef<FFakeTransport> Transport = MakeShared<FFakeTransport>();
		const TSharedRef<FEudoriaSession> Session = MakeShared<FEudoriaSession>(Transport);
		Session->Register(TEXT("ana@example.com"), TEXT("Ana_42"), Password, [](const FEudoriaApiStatus&) {});
		Transport->Answer(TEXT("/accounts"), FEudoriaHttpResponse::Make(202, TEXT(R"({"status":"accepted"})")));
		Session->Login(TEXT("ana@example.com"), Password, [](const FEudoriaApiStatus&) {});
		Transport->Answer(TEXT("/auth/login"), FEudoriaHttpResponse::Make(200, TokenJson(Access1, Refresh1)));
		FProfileOutcome Outcome;
		Session->FetchProfile(Record(Outcome));
		Transport->Answer(TEXT("/me"), FEudoriaHttpResponse::Make(401));
		Transport->Answer(TEXT("/auth/refresh"), FEudoriaHttpResponse::Make(200, TokenJson(Access2, Refresh2)));
		Transport->Answer(TEXT("/me"), FEudoriaHttpResponse::Make(200, ProfileJson));
		Session->Logout([](const FEudoriaApiStatus&) {});
		Transport->Answer(TEXT("/auth/logout"), FEudoriaHttpResponse::Make(204));
	}
	const FString Logged = Capture.Get();
	TestTrue(TEXT("the scenario did log something"), Logged.Contains(TEXT("renewed")));
	for (const TCHAR* Secret : {Password, Access1, Refresh1, Access2, Refresh2})
	{
		TestFalse(FString::Printf(TEXT("secret #%d absent from the logs"), FCString::Strlen(Secret)), Logged.Contains(Secret));
	}
	return true;
}

#endif // WITH_DEV_AUTOMATION_TESTS
