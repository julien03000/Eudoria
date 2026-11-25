#include "EudoriaSession.h"

#include "EudoriaCore.h"

FEudoriaSession::FEudoriaSession(TSharedRef<IEudoriaHttpTransport> InTransport)
	: Transport(MoveTemp(InTransport))
{
}

void FEudoriaSession::Register(const FString& Email, const FString& Pseudo, const FString& Password, FStatusCallback OnComplete)
{
	FEudoriaHttpRequest Request{TEXT("POST"), TEXT("/accounts"), EudoriaCoreApi::MakeRegisterBody(Email, Pseudo, Password)};
	Transport->Send(Request, [OnComplete = MoveTemp(OnComplete)](const FEudoriaHttpResponse& Response)
	{
		OnComplete(EudoriaCoreApi::ParseRegistration(Response));
	});
}

void FEudoriaSession::Login(const FString& Email, const FString& Password, FStatusCallback OnComplete)
{
	EndSession(); // a new login replaces any previous session, locally

	FEudoriaHttpRequest Request{TEXT("POST"), TEXT("/auth/login"), EudoriaCoreApi::MakeLoginBody(Email, Password)};
	const uint32 LoginGeneration = Generation;
	Transport->Send(Request, [WeakThis = AsWeak(), LoginGeneration, OnComplete = MoveTemp(OnComplete)](const FEudoriaHttpResponse& Response)
	{
		const TSharedPtr<FEudoriaSession> This = WeakThis.Pin();
		if (!This.IsValid())
		{
			return;
		}
		TValueOrError<FEudoriaTokenPair, FEudoriaApiError> Result = EudoriaCoreApi::ParseTokenPair(Response);
		if (Result.HasError())
		{
			OnComplete(Result.StealError());
			return;
		}
		if (LoginGeneration != This->Generation)
		{
			// Logged out (or another login started) while this one was on its way: drop these tokens.
			OnComplete(EudoriaCoreApi::ParseError(MakeSessionOverResponse()));
			return;
		}
		This->Tokens = Result.StealValue();
		++This->Generation;
		UE_LOG(LogEudoriaCore, Log, TEXT("Logged in (tokens kept in memory only)."));
		OnComplete({});
	});
}

void FEudoriaSession::FetchProfile(FProfileCallback OnComplete)
{
	SendAuthorized(FEudoriaHttpRequest{TEXT("GET"), TEXT("/me")}, [OnComplete = MoveTemp(OnComplete)](const FEudoriaHttpResponse& Response)
	{
		TValueOrError<FEudoriaProfile, FEudoriaApiError> Result = EudoriaCoreApi::ParseProfile(Response);
		if (Result.HasError() && Result.GetError().Kind == EEudoriaApiErrorKind::Unauthorized)
		{
			// A 401 that reaches this point survived the renewal rules: the session is over.
			FEudoriaApiError Error = Result.StealError();
			Error.Kind = EEudoriaApiErrorKind::SessionEnded;
			OnComplete(TValueOrError<FEudoriaProfile, FEudoriaApiError>(MakeError(MoveTemp(Error))));
			return;
		}
		OnComplete(Result);
	});
}

void FEudoriaSession::Logout(FStatusCallback OnComplete)
{
	const FString RefreshToken = Tokens.RefreshToken;
	EndSession();
	UE_LOG(LogEudoriaCore, Log, TEXT("Logged out (tokens forgotten)."));
	if (RefreshToken.IsEmpty())
	{
		OnComplete({});
		return;
	}

	FEudoriaHttpRequest Request{TEXT("POST"), TEXT("/auth/logout"), EudoriaCoreApi::MakeRefreshTokenBody(RefreshToken)};
	Transport->Send(Request, [OnComplete = MoveTemp(OnComplete)](const FEudoriaHttpResponse& Response)
	{
		OnComplete(EudoriaCoreApi::ParseLogout(Response));
	});
}

void FEudoriaSession::SendAuthorized(FEudoriaHttpRequest Request, FEudoriaHttpCallback OnComplete)
{
	if (!IsLoggedIn())
	{
		OnComplete(MakeSessionOverResponse());
		return;
	}
	SendWithAccessToken(MoveTemp(Request), MoveTemp(OnComplete), /*bMayRenew*/ true);
}

void FEudoriaSession::SendWithAccessToken(FEudoriaHttpRequest Request, FEudoriaHttpCallback OnComplete, bool bMayRenew)
{
	Request.BearerToken = Tokens.AccessToken;
	const uint32 SentGeneration = Generation;

	Transport->Send(Request,
		[WeakThis = AsWeak(), Request, SentGeneration, bMayRenew, OnComplete = MoveTemp(OnComplete)](const FEudoriaHttpResponse& Response) mutable
		{
			const TSharedPtr<FEudoriaSession> This = WeakThis.Pin();
			if (!This.IsValid())
			{
				return;
			}
			const bool bIsUnauthorized = Response.TransportStatus == EEudoriaTransportStatus::Received && Response.StatusCode == 401;
			if (!bIsUnauthorized)
			{
				OnComplete(Response);
				return;
			}
			if (SentGeneration != This->Generation || !This->IsLoggedIn())
			{
				// The session this request belonged to is over (logout, failed renewal, new login).
				OnComplete(MakeSessionOverResponse());
				return;
			}
			if (Request.BearerToken != This->Tokens.AccessToken)
			{
				// Sent before a renewal that has finished since: replay with the current token, no new renewal.
				This->SendWithAccessToken(MoveTemp(Request), MoveTemp(OnComplete), /*bMayRenew*/ false);
				return;
			}
			if (!bMayRenew)
			{
				// Already replayed once after a renewal and still refused: stop here (no loop).
				UE_LOG(LogEudoriaCore, Warning, TEXT("%s %s refused again after renewal: session ended."), *Request.Verb, *Request.Path);
				This->EndSession();
				OnComplete(Response);
				return;
			}
			// Wait for the (single) renewal, started now if none is in progress.
			This->WaitingForRenewal.Add(FPendingRequest{MoveTemp(Request), MoveTemp(OnComplete)});
			This->StartRenewal();
		});
}

void FEudoriaSession::StartRenewal()
{
	if (bRenewing)
	{
		return;
	}
	bRenewing = true;
	const uint32 RenewalGeneration = Generation;
	UE_LOG(LogEudoriaCore, Log, TEXT("Access token refused: renewing it (one request for %d waiting)."), WaitingForRenewal.Num());

	FEudoriaHttpRequest Request{TEXT("POST"), TEXT("/auth/refresh"), EudoriaCoreApi::MakeRefreshTokenBody(Tokens.RefreshToken)};
	Transport->Send(Request, [WeakThis = AsWeak(), RenewalGeneration](const FEudoriaHttpResponse& Response)
	{
		if (const TSharedPtr<FEudoriaSession> This = WeakThis.Pin())
		{
			This->FinishRenewal(Response, RenewalGeneration);
		}
	});
}

void FEudoriaSession::FinishRenewal(const FEudoriaHttpResponse& Response, uint32 RenewalGeneration)
{
	if (RenewalGeneration != Generation)
	{
		// The session ended while the renewal was on its way; waiting requests were already failed.
		return;
	}
	bRenewing = false;

	TValueOrError<FEudoriaTokenPair, FEudoriaApiError> Result = EudoriaCoreApi::ParseTokenPair(Response);
	if (Result.HasError())
	{
		UE_LOG(LogEudoriaCore, Warning, TEXT("Renewal refused (status %d): session ended."), Result.GetError().StatusCode);
		EndSession();
		return;
	}

	Tokens = Result.StealValue();
	TArray<FPendingRequest> Waiting = MoveTemp(WaitingForRenewal);
	WaitingForRenewal.Reset();
	UE_LOG(LogEudoriaCore, Log, TEXT("Access token renewed: replaying %d request(s)."), Waiting.Num());
	for (FPendingRequest& Pending : Waiting)
	{
		SendWithAccessToken(MoveTemp(Pending.Request), MoveTemp(Pending.OnComplete), /*bMayRenew*/ false);
	}
}

void FEudoriaSession::EndSession()
{
	Tokens = FEudoriaTokenPair();
	++Generation;
	bRenewing = false;

	TArray<FPendingRequest> Waiting = MoveTemp(WaitingForRenewal);
	WaitingForRenewal.Reset();
	for (FPendingRequest& Pending : Waiting)
	{
		Pending.OnComplete(MakeSessionOverResponse());
	}
}

FEudoriaHttpResponse FEudoriaSession::MakeSessionOverResponse()
{
	return FEudoriaHttpResponse::Make(401);
}
