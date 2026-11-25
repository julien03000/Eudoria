// Integration tests against a running core (tools/dev/run-core.ps1), over real HTTPS.
// Category: Eudoria.Integration. Skipped with an explicit warning when the core does not answer.

#include "EudoriaCoreSettings.h"
#include "EudoriaHttpTransport.h"
#include "EudoriaSession.h"
#include "HttpManager.h"
#include "HttpModule.h"
#include "Misc/AutomationTest.h"
#include "Misc/Guid.h"

#if WITH_DEV_AUTOMATION_TESTS

namespace EudoriaIntegrationTests
{
	constexpr EAutomationTestFlags IntegrationTestFlags = EAutomationTestFlags::EditorContext | EAutomationTestFlags::ProductFilter;

	/** Runs the HTTP module until Done() is true or the timeout expires (the test runs on the game thread). */
	bool WaitFor(TFunctionRef<bool()> Done, double TimeoutSeconds = 15.0)
	{
		const double Deadline = FPlatformTime::Seconds() + TimeoutSeconds;
		while (!Done())
		{
			if (FPlatformTime::Seconds() > Deadline)
			{
				return false;
			}
			FHttpModule::Get().GetHttpManager().Tick(0.02f);
			FPlatformProcess::Sleep(0.02f);
		}
		return true;
	}

	TSharedRef<FEudoriaHttpModuleTransport> MakeTransport()
	{
		const UEudoriaCoreSettings* Settings = GetDefault<UEudoriaCoreSettings>();
		return MakeShared<FEudoriaHttpModuleTransport>(Settings->CoreBaseUrl, Settings->RequestTimeoutSeconds);
	}

	/** Sends one request and waits for its answer. */
	FEudoriaHttpResponse SendAndWait(IEudoriaHttpTransport& Transport, const FEudoriaHttpRequest& Request)
	{
		TOptional<FEudoriaHttpResponse> Response;
		Transport.Send(Request, [&Response](const FEudoriaHttpResponse& Received) { Response = Received; });
		WaitFor([&Response] { return Response.IsSet(); });
		return Response.Get(FEudoriaHttpResponse());
	}

	/** True when the core answers /health/live; otherwise adds a warning so the skip is visible. */
	bool IsCoreRunning(FAutomationTestBase& Test, IEudoriaHttpTransport& Transport)
	{
		const FEudoriaHttpResponse Live = SendAndWait(Transport, FEudoriaHttpRequest{TEXT("GET"), TEXT("/health/live")});
		if (Live.TransportStatus == EEudoriaTransportStatus::Received && Live.StatusCode == 200)
		{
			return true;
		}
		Test.AddWarning(TEXT("SKIPPED: the core does not answer on CoreBaseUrl (start it with tools/dev/run-core.ps1)."));
		return false;
	}

	/** Unique, valid pseudo and e-mail for each run. */
	FString UniqueSuffix()
	{
		return FGuid::NewGuid().ToString(EGuidFormats::Digits).Left(10).ToLower();
	}

	const TCHAR* TestPassword = TEXT("une-phrase-de-passe-pour-les-tests-ue");
}

using namespace EudoriaIntegrationTests;

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaIntegrationFullFlowTest, "Eudoria.Integration.RegisterLoginProfileLogout", IntegrationTestFlags)
bool FEudoriaIntegrationFullFlowTest::RunTest(const FString&)
{
	const TSharedRef<FEudoriaHttpModuleTransport> Transport = MakeTransport();
	if (!IsCoreRunning(*this, *Transport))
	{
		return true;
	}

	const FString Suffix = UniqueSuffix();
	const FString Email = FString::Printf(TEXT("ue-%s@example.com"), *Suffix);
	const FString Pseudo = FString::Printf(TEXT("Ue_%s"), *Suffix);
	const TSharedRef<FEudoriaSession> Session = MakeShared<FEudoriaSession>(Transport);

	TOptional<FEudoriaApiStatus> Registered;
	Session->Register(Email, Pseudo, TestPassword, [&](const FEudoriaApiStatus& Result) { Registered = Result; });
	TestTrue(TEXT("registration answered"), WaitFor([&] { return Registered.IsSet(); }));
	TestFalse(TEXT("registration accepted"), Registered.IsSet() && Registered->IsSet());

	TOptional<FEudoriaApiStatus> LoggedIn;
	Session->Login(Email, TestPassword, [&](const FEudoriaApiStatus& Result) { LoggedIn = Result; });
	TestTrue(TEXT("login answered"), WaitFor([&] { return LoggedIn.IsSet(); }));
	TestTrue(TEXT("logged in"), Session->IsLoggedIn());

	bool bProfileDone = false;
	FString ReceivedPseudo;
	Session->FetchProfile([&](const TValueOrError<FEudoriaProfile, FEudoriaApiError>& Result)
	{
		bProfileDone = true;
		if (Result.HasValue())
		{
			ReceivedPseudo = Result.GetValue().Pseudo;
		}
	});
	TestTrue(TEXT("profile answered"), WaitFor([&] { return bProfileDone; }));
	TestEqual(TEXT("pseudo from GET /me"), ReceivedPseudo, Pseudo);

	TOptional<FEudoriaApiStatus> LoggedOut;
	Session->Logout([&](const FEudoriaApiStatus& Result) { LoggedOut = Result; });
	TestTrue(TEXT("logout answered"), WaitFor([&] { return LoggedOut.IsSet(); }));
	TestFalse(TEXT("logout accepted (204)"), LoggedOut.IsSet() && LoggedOut->IsSet());
	TestFalse(TEXT("tokens forgotten"), Session->IsLoggedIn());
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaIntegrationRefreshAfterLogoutTest, "Eudoria.Integration.RefreshRefusedAfterLogout", IntegrationTestFlags)
bool FEudoriaIntegrationRefreshAfterLogoutTest::RunTest(const FString&)
{
	const TSharedRef<FEudoriaHttpModuleTransport> Transport = MakeTransport();
	if (!IsCoreRunning(*this, *Transport))
	{
		return true;
	}

	const FString Suffix = UniqueSuffix();
	const FString Email = FString::Printf(TEXT("ue-%s@example.com"), *Suffix);
	SendAndWait(*Transport, FEudoriaHttpRequest{TEXT("POST"), TEXT("/accounts"),
		EudoriaCoreApi::MakeRegisterBody(Email, FString::Printf(TEXT("Ue_%s"), *Suffix), TestPassword)});

	const auto Pair = EudoriaCoreApi::ParseTokenPair(SendAndWait(*Transport,
		FEudoriaHttpRequest{TEXT("POST"), TEXT("/auth/login"), EudoriaCoreApi::MakeLoginBody(Email, TestPassword)}));
	if (!TestTrue(TEXT("login"), Pair.HasValue()))
	{
		return false;
	}
	const FString RefreshToken = Pair.GetValue().RefreshToken;
	const FEudoriaHttpResponse Logout = SendAndWait(*Transport,
		FEudoriaHttpRequest{TEXT("POST"), TEXT("/auth/logout"), EudoriaCoreApi::MakeRefreshTokenBody(RefreshToken)});
	TestEqual(TEXT("logout 204"), Logout.StatusCode, 204);

	const auto Refused = EudoriaCoreApi::ParseTokenPair(SendAndWait(*Transport,
		FEudoriaHttpRequest{TEXT("POST"), TEXT("/auth/refresh"), EudoriaCoreApi::MakeRefreshTokenBody(RefreshToken)}));
	TestTrue(TEXT("refresh refused after logout"), Refused.HasError() && Refused.GetError().Kind == EEudoriaApiErrorKind::Unauthorized);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaIntegrationErrorsTest, "Eudoria.Integration.PseudoTakenAndWrongPassword", IntegrationTestFlags)
bool FEudoriaIntegrationErrorsTest::RunTest(const FString&)
{
	const TSharedRef<FEudoriaHttpModuleTransport> Transport = MakeTransport();
	if (!IsCoreRunning(*this, *Transport))
	{
		return true;
	}

	const FString Suffix = UniqueSuffix();
	const FString Pseudo = FString::Printf(TEXT("Ue_%s"), *Suffix);
	const FString Email = FString::Printf(TEXT("ue-%s@example.com"), *Suffix);
	const TSharedRef<FEudoriaSession> Session = MakeShared<FEudoriaSession>(Transport);

	TOptional<FEudoriaApiStatus> First, Second;
	Session->Register(Email, Pseudo, TestPassword, [&](const FEudoriaApiStatus& Result) { First = Result; });
	WaitFor([&] { return First.IsSet(); });
	Session->Register(FString::Printf(TEXT("other-%s@example.com"), *Suffix), Pseudo, TestPassword, [&](const FEudoriaApiStatus& Result) { Second = Result; });
	WaitFor([&] { return Second.IsSet(); });
	if (TestTrue(TEXT("second registration refused"), Second.IsSet() && Second->IsSet()))
	{
		TestEqual(TEXT("pseudo taken message"), EudoriaCoreApi::ToUserMessage(Second->GetValue()).ToString(), FString(TEXT("Ce pseudo est déjà pris.")));
	}

	TOptional<FEudoriaApiStatus> Wrong;
	Session->Login(Email, TEXT("pas-le-bon-mot-de-passe-du-tout"), [&](const FEudoriaApiStatus& Result) { Wrong = Result; });
	WaitFor([&] { return Wrong.IsSet(); });
	if (TestTrue(TEXT("wrong password refused"), Wrong.IsSet() && Wrong->IsSet()))
	{
		TestEqual(TEXT("neutral message"), EudoriaCoreApi::ToUserMessage(Wrong->GetValue()).ToString(), FString(TEXT("E-mail ou mot de passe incorrect.")));
	}
	TestFalse(TEXT("not logged in"), Session->IsLoggedIn());
	return true;
}

#endif // WITH_DEV_AUTOMATION_TESTS
