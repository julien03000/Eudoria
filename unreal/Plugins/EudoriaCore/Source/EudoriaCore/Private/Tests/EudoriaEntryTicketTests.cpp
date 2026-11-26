// Automation tests of the entry ticket verifier (plan 1.6 B, ADR-0007): the shared test vectors (same verdict
// as the .NET verifier), single use, key downloads, and no ticket in the logs. Category: Eudoria.EntryTickets.

#include "EudoriaEntryTickets.h"
#include "EudoriaTestSupport.h"
#include "EudoriaTicketTestVectors.h"
#include "Misc/AutomationTest.h"

#if WITH_DEV_AUTOMATION_TESTS && EUDORIA_WITH_TICKET_VERIFIER

namespace EudoriaEntryTicketTests
{
	using EudoriaTestSupport::FFakeTransport;
	using EudoriaTestSupport::FLogCapture;
	using EudoriaTicketTestVectors::FVectors;

	constexpr EAutomationTestFlags TicketTestFlags = EAutomationTestFlags::EditorContext | EAutomationTestFlags::ProductFilter;

	/** A clock the test moves by hand. */
	struct FManualClock
	{
		TSharedRef<int64> Now = MakeShared<int64>(0);

		FEudoriaUnixClock Get() const
		{
			return [Now = Now] { return *Now; };
		}
	};

	/** A world "sandbox" with its key store, memory and verifier, on a fake core. */
	struct FWorld
	{
		TSharedRef<FFakeTransport> Core = MakeShared<FFakeTransport>();
		FManualClock KeyClock;
		FManualClock VerifyClock;
		TSharedRef<FEudoriaTicketKeyStore> Keys = MakeShared<FEudoriaTicketKeyStore>(Core, KeyClock.Get());
		TSharedRef<FEudoriaReplayCache> Replays = MakeShared<FEudoriaReplayCache>();
		TSharedRef<FEudoriaWorldTicketVerifier> Verifier;

		explicit FWorld(const FString& WorldId)
			: Verifier(MakeShared<FEudoriaWorldTicketVerifier>(WorldId, Keys, Replays, VerifyClock.Get()))
		{
		}

		/** Verifies; when a key download is pending, answers it with Response first. */
		FEudoriaTicketVerdict Verify(const FString& Ticket, const TOptional<FEudoriaHttpResponse>& Download = {})
		{
			TOptional<FEudoriaTicketVerdict> Verdict;
			Verifier->Verify(Ticket, [&Verdict](const FEudoriaTicketVerdict& Result) { Verdict = Result; });
			if (!Verdict.IsSet() && Download.IsSet() && Core->CountPending(FEudoriaTicketKeyStore::KeysPath()) > 0)
			{
				Core->Answer(FEudoriaTicketKeyStore::KeysPath(), *Download);
			}
			return Verdict.Get(FEudoriaTicketVerdict{FString(), TEXT("no verdict")});
		}
	};

	FEudoriaHttpResponse KeysResponse(const FVectors& Vectors) { return FEudoriaHttpResponse::Make(200, Vectors.KeysJson); }

	FEudoriaHttpResponse Unreachable() { return FEudoriaHttpResponse(); } // TransportStatus::Failed

	bool LoadVectors(FAutomationTestBase& Test, FVectors& Vectors)
	{
		return Test.TestTrue(TEXT("shared test vectors loaded (shared/test-vectors/entry-tickets)"), EudoriaTicketTestVectors::Load(Vectors));
	}

	/** The core answered with its keys at start-up (time T). */
	void StartWithKeys(FWorld& World, const FVectors& Vectors, int64 T)
	{
		*World.KeyClock.Now = T;
		World.Keys->Refresh([](bool) {});
		World.Core->Answer(FEudoriaTicketKeyStore::KeysPath(), KeysResponse(Vectors));
	}
}

using namespace EudoriaEntryTicketTests;

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaTicketVectorsTest, "Eudoria.EntryTickets.SharedVectors", TicketTestFlags)
bool FEudoriaTicketVectorsTest::RunTest(const FString&)
{
	FVectors Vectors;
	if (!LoadVectors(*this, Vectors))
	{
		return false;
	}
	const TOptional<FEudoriaTicketKeys> Keys = EudoriaEntryTicketRules::ParseJwkSet(Vectors.KeysJson);
	if (!TestTrue(TEXT("keys.json read"), Keys.IsSet() && Keys->Num() == 1))
	{
		return false;
	}
	TestEqual(TEXT("same clock tolerance as the vectors"), EudoriaEntryTicketRules::ClockToleranceSeconds, Vectors.ClockToleranceSeconds);

	for (const EudoriaTicketTestVectors::FVector& Vector : Vectors.Vectors)
	{
		const FEudoriaTicketCheck Check = EudoriaEntryTicketRules::Check(Vector.Ticket, Vectors.World, Vector.Now, *Keys);
		const FString Verdict = Check.Ticket.IsSet() ? FString(TEXT("accepted")) : Check.Rejection;
		TestEqual(FString::Printf(TEXT("vector '%s'"), *Vector.Name), Verdict, Vector.Expected);
		if (Check.Ticket.IsSet())
		{
			TestEqual(FString::Printf(TEXT("vector '%s': account"), *Vector.Name), Check.Ticket->AccountId, Vectors.AccountId);
		}
	}
	AddInfo(FString::Printf(TEXT("%d shared vectors checked."), Vectors.Vectors.Num()));
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaTicketSingleUseTest, "Eudoria.EntryTickets.AcceptedOnceThenReplayed", TicketTestFlags)
bool FEudoriaTicketSingleUseTest::RunTest(const FString&)
{
	FVectors Vectors;
	if (!LoadVectors(*this, Vectors))
	{
		return false;
	}
	const EudoriaTicketTestVectors::FVector& Valid = *Vectors.Find(TEXT("valid"));
	FWorld Sandbox(Vectors.World);
	FWorld Other(TEXT("other-world"));
	StartWithKeys(Sandbox, Vectors, Valid.Now);
	StartWithKeys(Other, Vectors, Valid.Now);
	*Sandbox.VerifyClock.Now = Valid.Now;
	*Other.VerifyClock.Now = Valid.Now;

	const FEudoriaTicketVerdict First = Sandbox.Verify(Valid.Ticket);
	const FEudoriaTicketVerdict Second = Sandbox.Verify(Valid.Ticket);
	const FEudoriaTicketVerdict Elsewhere = Other.Verify(Valid.Ticket);

	TestEqual(TEXT("accepted, with the account"), First.AccountId, Vectors.AccountId);
	TestEqual(TEXT("second use refused"), Second.ToString(), FString(EudoriaTicketRejection::Replayed));
	TestEqual(TEXT("another world refuses it"), Elsewhere.ToString(), FString(EudoriaTicketRejection::AudienceInvalid));
	TestEqual(TEXT("verified offline: one download each, at start-up"), Sandbox.Keys->GetDownloads(), 1);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaReplayCacheTest, "Eudoria.EntryTickets.ReplayMemory", TicketTestFlags)
bool FEudoriaReplayCacheTest::RunTest(const FString&)
{
	using EOutcome = FEudoriaReplayCache::EOutcome;
	const int64 Now = 1795694400;

	FEudoriaReplayCache Replays;
	TestTrue(TEXT("first use"), Replays.TryRemember(TEXT("a"), Now + 75, Now) == EOutcome::FirstUse);
	TestTrue(TEXT("replayed at the last second"), Replays.TryRemember(TEXT("a"), Now + 75, Now + 75) == EOutcome::Replayed);
	TestTrue(TEXT("other jti"), Replays.TryRemember(TEXT("b"), Now + 151, Now + 76) == EOutcome::FirstUse);
	TestEqual(TEXT("'a' forgotten once it can no longer be accepted"), Replays.Num(), 1);

	FEudoriaReplayCache Small(2);
	TestTrue(TEXT("1"), Small.TryRemember(TEXT("1"), Now + 75, Now) == EOutcome::FirstUse);
	TestTrue(TEXT("2"), Small.TryRemember(TEXT("2"), Now + 75, Now) == EOutcome::FirstUse);
	TestTrue(TEXT("full: refused"), Small.TryRemember(TEXT("3"), Now + 75, Now) == EOutcome::Full);
	TestTrue(TEXT("room again later"), Small.TryRemember(TEXT("3"), Now + 151, Now + 76) == EOutcome::FirstUse);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaTicketKeyDownloadsTest, "Eudoria.EntryTickets.KeyDownloadsAreLimited", TicketTestFlags)
bool FEudoriaTicketKeyDownloadsTest::RunTest(const FString&)
{
	FVectors Vectors;
	if (!LoadVectors(*this, Vectors))
	{
		return false;
	}
	const EudoriaTicketTestVectors::FVector& Valid = *Vectors.Find(TEXT("valid"));
	const EudoriaTicketTestVectors::FVector& UnknownKid = *Vectors.Find(TEXT("kid-unknown"));
	FWorld World(Vectors.World);
	*World.VerifyClock.Now = Valid.Now; // the tickets' time; the key store has its own clock
	const int64 T = 1000000;
	StartWithKeys(World, Vectors, T);

	// Unknown kid right after start-up: no new download (the last one is less than a minute old).
	TestEqual(TEXT("unknown kid at start-up"), World.Verify(UnknownKid.Ticket).ToString(), FString(EudoriaTicketRejection::KeyUnknown));
	TestEqual(TEXT("no download"), World.Keys->GetDownloads(), 1);

	// A minute later: one download, still unknown; then none again for a minute.
	*World.KeyClock.Now = T + 60;
	TestEqual(TEXT("unknown kid after a minute"), World.Verify(UnknownKid.Ticket, KeysResponse(Vectors)).ToString(), FString(EudoriaTicketRejection::KeyUnknown));
	TestEqual(TEXT("unknown kid again"), World.Verify(UnknownKid.Ticket).ToString(), FString(EudoriaTicketRejection::KeyUnknown));
	TestEqual(TEXT("one download for both"), World.Keys->GetDownloads(), 2);

	// Known keys are used offline until they are 5 minutes old.
	*World.KeyClock.Now = T + 299;
	TestTrue(TEXT("known kid, offline"), World.Verify(Valid.Ticket).IsAccepted());
	TestEqual(TEXT("still two downloads"), World.Keys->GetDownloads(), 2);

	// Old keys and an unreachable core: a download is tried, fails, and the known keys keep working.
	*World.KeyClock.Now = T + 60 + 300;
	// A new replay memory: this ticket was already used just above.
	const TSharedRef<FEudoriaWorldTicketVerifier> Fresh =
		MakeShared<FEudoriaWorldTicketVerifier>(Vectors.World, World.Keys, MakeShared<FEudoriaReplayCache>(), World.VerifyClock.Get());
	TOptional<FEudoriaTicketVerdict> WhileCoreDown;
	Fresh->Verify(Valid.Ticket, [&WhileCoreDown](const FEudoriaTicketVerdict& Result) { WhileCoreDown = Result; });
	TestFalse(TEXT("waits for the download"), WhileCoreDown.IsSet());
	World.Core->Answer(FEudoriaTicketKeyStore::KeysPath(), Unreachable());
	TestTrue(TEXT("accepted with the keys already known"), WhileCoreDown.IsSet() && WhileCoreDown->IsAccepted());
	TestEqual(TEXT("a third download was tried"), World.Keys->GetDownloads(), 3);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaTicketNoKeysTest, "Eudoria.EntryTickets.NoKeysRefusesEverything", TicketTestFlags)
bool FEudoriaTicketNoKeysTest::RunTest(const FString&)
{
	FVectors Vectors;
	if (!LoadVectors(*this, Vectors))
	{
		return false;
	}
	const EudoriaTicketTestVectors::FVector& Valid = *Vectors.Find(TEXT("valid"));
	FWorld World(Vectors.World);
	*World.VerifyClock.Now = Valid.Now;

	TOptional<bool> Started;
	World.Keys->Refresh([&Started](bool bSuccess) { Started = bSuccess; });
	World.Core->Answer(FEudoriaTicketKeyStore::KeysPath(), Unreachable());

	TestTrue(TEXT("start-up download failed"), Started.IsSet() && !*Started);
	TestEqual(TEXT("refused"), World.Verify(Valid.Ticket).ToString(), FString(EudoriaTicketRejection::KeysUnavailable));
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaJwkSetTest, "Eudoria.EntryTickets.JwkSetKeepsOnlyUsablePublicKeys", TicketTestFlags)
bool FEudoriaJwkSetTest::RunTest(const FString&)
{
	FVectors Vectors;
	if (!LoadVectors(*this, Vectors))
	{
		return false;
	}
	const FString Good = FString::Printf(TEXT(R"({"kty":"EC","crv":"P-256","x":"%s","y":"%s","kid":"good","alg":"ES256","use":"sig"})"), *Vectors.KeyX, *Vectors.KeyY);
	const FString Bad[] = {
		Good.Replace(TEXT(R"("kid":"good")"), TEXT(R"("kid":"bad","d":"AAAA")")), // private part
		Good.Replace(TEXT("P-256"), TEXT("P-384")).Replace(TEXT("good"), TEXT("bad")),
		Good.Replace(TEXT("ES256"), TEXT("RS256")).Replace(TEXT("good"), TEXT("bad")),
		Good.Replace(TEXT(R"("use":"sig")"), TEXT(R"("use":"enc")")).Replace(TEXT("good"), TEXT("bad")),
		Good.Replace(*Vectors.KeyY, TEXT("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")).Replace(TEXT("good"), TEXT("bad")), // off the curve
	};
	for (const FString& BadKey : Bad)
	{
		const TOptional<FEudoriaTicketKeys> Keys = EudoriaEntryTicketRules::ParseJwkSet(FString::Printf(TEXT(R"({"keys":[%s,%s]})"), *Good, *BadKey));
		TestTrue(FString::Printf(TEXT("only the good key kept, not: %s"), *BadKey.Left(60)), Keys.IsSet() && Keys->Num() == 1 && Keys->Contains(TEXT("good")));
	}
	TestFalse(TEXT("duplicate member"), EudoriaEntryTicketRules::ParseJwkSet(TEXT(R"({"keys":[],"keys":[]})")).IsSet());
	TestFalse(TEXT("keys not a list"), EudoriaEntryTicketRules::ParseJwkSet(TEXT(R"({"keys":"x"})")).IsSet());
	TestFalse(TEXT("not json"), EudoriaEntryTicketRules::ParseJwkSet(TEXT("not json")).IsSet());
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaTicketNotLoggedTest, "Eudoria.EntryTickets.TicketNeverLogged", TicketTestFlags)
bool FEudoriaTicketNotLoggedTest::RunTest(const FString&)
{
	FVectors Vectors;
	if (!LoadVectors(*this, Vectors))
	{
		return false;
	}
	const EudoriaTicketTestVectors::FVector& Valid = *Vectors.Find(TEXT("valid"));
	TArray<FString> Parts;
	Valid.Ticket.ParseIntoArray(Parts, TEXT("."));

	FLogCapture Capture;
	{
		FWorld World(Vectors.World);
		StartWithKeys(World, Vectors, Valid.Now);
		*World.VerifyClock.Now = Valid.Now;
		World.Verify(Valid.Ticket);
		World.Verify(Valid.Ticket);
		World.Verify(Vectors.Find(TEXT("alg-none"))->Ticket);
	}
	const FString Logged = Capture.Get();
	TestTrue(TEXT("the verdicts are logged"), Logged.Contains(TEXT("ticket_replayed")) && Logged.Contains(TEXT("accepted for account")));
	for (const FString& Part : Parts)
	{
		TestFalse(TEXT("no part of the ticket in the logs"), Logged.Contains(Part));
	}
	return true;
}

#endif // WITH_DEV_AUTOMATION_TESTS && EUDORIA_WITH_TICKET_VERIFIER
