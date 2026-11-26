// The stateful parts of entry ticket verification: replay memory, key store, verifier (ADR-0007).

#include "EudoriaEntryTickets.h"

#if EUDORIA_WITH_TICKET_VERIFIER

#include "EudoriaCore.h"

FEudoriaUnixClock EudoriaSystemClock()
{
	return [] { return FDateTime::UtcNow().ToUnixTimestamp(); };
}

// ---------- Replay memory ----------

namespace
{
	/** Min-heap order: the entry forgotten first on top. */
	struct FEarlierFirst
	{
		bool operator()(const TPair<int64, FString>& A, const TPair<int64, FString>& B) const { return A.Key < B.Key; }
	};
}

FEudoriaReplayCache::EOutcome FEudoriaReplayCache::TryRemember(const FString& Jti, int64 ForgetAfterUnixSeconds, int64 NowUnixSeconds)
{
	// Forget what can no longer be accepted anyway (expired tickets are refused before coming here).
	while (!ByExpiry.IsEmpty() && ByExpiry.HeapTop().Key < NowUnixSeconds)
	{
		TPair<int64, FString> Oldest;
		ByExpiry.HeapPop(Oldest, FEarlierFirst(), EAllowShrinking::No);
		ForgetAfter.Remove(Oldest.Value);
	}
	if (ForgetAfter.Contains(Jti))
	{
		return EOutcome::Replayed;
	}
	if (ForgetAfter.Num() >= Capacity)
	{
		return EOutcome::Full;
	}
	ForgetAfter.Add(Jti, ForgetAfterUnixSeconds);
	ByExpiry.HeapPush(TPair<int64, FString>(ForgetAfterUnixSeconds, Jti), FEarlierFirst());
	return EOutcome::FirstUse;
}

// ---------- Key store ----------

FEudoriaTicketKeyStore::FEudoriaTicketKeyStore(TSharedRef<IEudoriaHttpTransport> InTransport, FEudoriaUnixClock InClock)
	: Transport(MoveTemp(InTransport))
	, Clock(MoveTemp(InClock))
{
}

void FEudoriaTicketKeyStore::Refresh(TFunction<void(bool)> OnComplete)
{
	Waiting.Add([WeakThis = AsWeak(), OnComplete = MoveTemp(OnComplete)]
	{
		const TSharedPtr<FEudoriaTicketKeyStore> This = WeakThis.Pin();
		OnComplete(This.IsValid() && This->bLastDownloadSucceeded);
	});
	if (!bDownloading)
	{
		Download();
	}
}

void FEudoriaTicketKeyStore::EnsureFresh(const FString& KeyId, TFunction<void()> OnReady)
{
	if (bDownloading)
	{
		Waiting.Add(MoveTemp(OnReady)); // checked again once this download ends
		return;
	}
	if (!NeedsDownload(KeyId))
	{
		OnReady();
		return;
	}
	Waiting.Add(MoveTemp(OnReady));
	Download();
}

bool FEudoriaTicketKeyStore::NeedsDownload(const FString& KeyId) const
{
	const int64 Now = Clock();
	const bool bStale = !LastSuccess.IsSet() || Now - *LastSuccess >= RefreshIntervalSeconds;
	const bool bAllowed = !LastAttempt.IsSet() || Now - *LastAttempt >= MinDownloadIntervalSeconds;
	return bAllowed && (bStale || !Keys.Contains(KeyId));
}

void FEudoriaTicketKeyStore::Download()
{
	bDownloading = true;
	LastAttempt = Clock();
	++Downloads;
	Transport->Send(FEudoriaHttpRequest{TEXT("GET"), KeysPath()}, [WeakThis = AsWeak()](const FEudoriaHttpResponse& Response)
	{
		if (const TSharedPtr<FEudoriaTicketKeyStore> This = WeakThis.Pin())
		{
			This->FinishDownload(Response);
		}
	});
}

void FEudoriaTicketKeyStore::FinishDownload(const FEudoriaHttpResponse& Response)
{
	bDownloading = false;
	bLastDownloadSucceeded = false;
	if (Response.TransportStatus == EEudoriaTransportStatus::Received && Response.StatusCode == 200 && Response.Body.Len() <= MaxDocumentLength)
	{
		TOptional<FEudoriaTicketKeys> Parsed = EudoriaEntryTicketRules::ParseJwkSet(Response.Body);
		if (Parsed.IsSet() && !Parsed->IsEmpty())
		{
			Keys = MoveTemp(*Parsed);
			LastSuccess = Clock();
			bLastDownloadSucceeded = true;
		}
	}
	if (bLastDownloadSucceeded)
	{
		UE_LOG(LogEudoriaCore, Log, TEXT("Entry ticket keys downloaded: %d key(s)."), Keys.Num());
	}
	else
	{
		// The keys already known are kept: tickets stay verifiable while the core is unreachable.
		UE_LOG(LogEudoriaCore, Warning, TEXT("Entry ticket keys could not be downloaded (status %d); keeping %d known key(s)."),
			Response.StatusCode, Keys.Num());
	}

	TArray<TFunction<void()>> Ready = MoveTemp(Waiting);
	Waiting.Reset();
	for (TFunction<void()>& Callback : Ready)
	{
		Callback();
	}
}

// ---------- Verifier ----------

FEudoriaWorldTicketVerifier::FEudoriaWorldTicketVerifier(FString InWorldId, TSharedRef<FEudoriaTicketKeyStore> InKeys,
	TSharedRef<FEudoriaReplayCache> InReplays, FEudoriaUnixClock InClock)
	: WorldId(MoveTemp(InWorldId))
	, Keys(MoveTemp(InKeys))
	, Replays(MoveTemp(InReplays))
	, Clock(MoveTemp(InClock))
{
}

void FEudoriaWorldTicketVerifier::Verify(const FString& Ticket, FVerdictCallback OnVerdict)
{
	const FEudoriaTicketCheck Header = EudoriaEntryTicketRules::ReadHeader(Ticket);
	if (!Header.Rejection.IsEmpty())
	{
		UE_LOG(LogEudoriaCore, Log, TEXT("Entry ticket refused: %s."), *Header.Rejection);
		OnVerdict(FEudoriaTicketVerdict{FString(), Header.Rejection});
		return;
	}
	// Usually at once: a download only happens for old keys or an unknown kid (at most once a minute).
	Keys->EnsureFresh(Header.KeyId, [WeakThis = AsWeak(), Ticket, OnVerdict = MoveTemp(OnVerdict)]
	{
		if (const TSharedPtr<FEudoriaWorldTicketVerifier> This = WeakThis.Pin())
		{
			OnVerdict(This->Decide(Ticket));
		}
	});
}

FEudoriaTicketVerdict FEudoriaWorldTicketVerifier::Decide(const FString& Ticket)
{
	FEudoriaTicketVerdict Verdict;
	if (Keys->GetKeys().IsEmpty())
	{
		Verdict.Rejection = EudoriaTicketRejection::KeysUnavailable;
	}
	else
	{
		const int64 Now = Clock();
		const FEudoriaTicketCheck Check = EudoriaEntryTicketRules::Check(Ticket, WorldId, Now, Keys->GetKeys());
		if (!Check.Ticket.IsSet())
		{
			Verdict.Rejection = Check.Rejection;
		}
		else
		{
			// Last: only a ticket that passed every other rule uses up its jti.
			switch (Replays->TryRemember(Check.Ticket->Jti, Check.Ticket->ExpiresAtUnixSeconds + EudoriaEntryTicketRules::ClockToleranceSeconds, Now))
			{
			case FEudoriaReplayCache::EOutcome::FirstUse:
				Verdict.AccountId = Check.Ticket->AccountId;
				break;
			case FEudoriaReplayCache::EOutcome::Replayed:
				Verdict.Rejection = EudoriaTicketRejection::Replayed;
				break;
			case FEudoriaReplayCache::EOutcome::Full:
				UE_LOG(LogEudoriaCore, Warning, TEXT("Entry ticket replay memory full (%d entries): refusing new tickets."), Replays->Num());
				Verdict.Rejection = EudoriaTicketRejection::ReplayCacheFull;
				break;
			}
		}
	}

	// The verdict only: never the ticket.
	if (Verdict.IsAccepted())
	{
		UE_LOG(LogEudoriaCore, Log, TEXT("Entry ticket accepted for account %s (world %s)."), *Verdict.AccountId, *WorldId);
	}
	else
	{
		UE_LOG(LogEudoriaCore, Log, TEXT("Entry ticket refused: %s."), *Verdict.Rejection);
	}
	return Verdict;
}

#endif // EUDORIA_WITH_TICKET_VERIFIER
