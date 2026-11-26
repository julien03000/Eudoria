#pragma once

#include "CoreMinimal.h"
#include "EudoriaHttpTransport.h"

/**
 * World entry tickets, verified by a world server alone (ADR-0007; contract core-v1, tag "worlds").
 * Same rules, same order and same rejection codes as the .NET reference verifier
 * (core/tests/Eudoria.ThirdParty.WorldVerifier); the shared test vectors check both.
 *
 * Only built where server code is (EUDORIA_WITH_TICKET_VERIFIER: editor and dedicated server on Win64/Linux).
 * At step 3, the dedicated server plugs FEudoriaWorldTicketVerifier into its entry gate (PreLogin).
 * A ticket is never written to a log, and never put in a connection URL.
 */
#if EUDORIA_WITH_TICKET_VERIFIER

/** Why a ticket was refused (snake_case codes shared with the .NET verifier and the test vectors). */
namespace EudoriaTicketRejection
{
	inline const TCHAR* const Malformed = TEXT("ticket_malformed");
	inline const TCHAR* const AlgorithmNotAllowed = TEXT("alg_not_allowed");
	inline const TCHAR* const TypeNotAllowed = TEXT("typ_not_allowed");
	inline const TCHAR* const KeyUnknown = TEXT("kid_unknown");
	inline const TCHAR* const SignatureInvalid = TEXT("signature_invalid");
	inline const TCHAR* const IssuerInvalid = TEXT("iss_invalid");
	inline const TCHAR* const AudienceInvalid = TEXT("aud_invalid");
	inline const TCHAR* const ClaimsInvalid = TEXT("claims_invalid");
	inline const TCHAR* const Expired = TEXT("ticket_expired");
	inline const TCHAR* const IssuedInTheFuture = TEXT("ticket_from_future");
	inline const TCHAR* const Replayed = TEXT("ticket_replayed");
	inline const TCHAR* const ReplayCacheFull = TEXT("replay_cache_full");
	inline const TCHAR* const KeysUnavailable = TEXT("keys_unavailable");
}

/** An ES256 public key of the core: P-256 point coordinates, 32 bytes each. */
struct FEudoriaTicketPublicKey
{
	TArray<uint8> X;
	TArray<uint8> Y;
};

/** The core's published ticket keys, by kid. */
using FEudoriaTicketKeys = TMap<FString, FEudoriaTicketPublicKey>;

/** A ticket whose every rule passed (before the single-use check). */
struct FEudoriaCheckedTicket
{
	/** The account's UUID, lower case (the same as GET /me "id"). */
	FString AccountId;
	FString Jti;
	int64 ExpiresAtUnixSeconds = 0;
};

/** Either a checked ticket, or the rejection code. */
struct FEudoriaTicketCheck
{
	TOptional<FEudoriaCheckedTicket> Ticket;
	/** Empty when the checks passed so far. */
	FString Rejection;
	/** The ticket's kid, once its header was read. */
	FString KeyId;

	static FEudoriaTicketCheck Refused(const TCHAR* Code)
	{
		FEudoriaTicketCheck Check;
		Check.Rejection = Code;
		return Check;
	}
};

/** The outcome of a verification: who the player is, or why the ticket is refused. */
struct FEudoriaTicketVerdict
{
	FString AccountId;
	FString Rejection;

	bool IsAccepted() const { return !AccountId.IsEmpty(); }

	/** "accepted" or the rejection code (the "expected" value of the shared test vectors). */
	FString ToString() const { return IsAccepted() ? FString(TEXT("accepted")) : Rejection; }
};

/** The rules, as pure functions: no network, no memory, no clock of their own. */
namespace EudoriaEntryTicketRules
{
	inline const TCHAR* const ExpectedAlgorithm = TEXT("ES256");
	inline const TCHAR* const ExpectedType = TEXT("eudoria-entry+jwt");
	inline const TCHAR* const ExpectedIssuer = TEXT("eudoria-core");
	inline const TCHAR* const AudiencePrefix = TEXT("eudoria-world:");

	/** Allowed clock difference between the core and the world (owner decision: 15 s). */
	constexpr int64 ClockToleranceSeconds = 15;
	/** Far above a real ticket (about 450 characters). */
	constexpr int32 MaxTicketLength = 4096;
	constexpr int32 MaxJtiLength = 128;

	/** Structure and header (alg, typ, kid). On success, Rejection is empty and KeyId is set. */
	EUDORIACORE_API FEudoriaTicketCheck ReadHeader(const FString& Ticket);

	/**
	 * Every rule, in this order: structure, alg, typ, key by kid, ES256 signature, iss, aud (one string),
	 * sub/iat/exp/jti well-formed with exp after iat, not expired, not from the future.
	 */
	EUDORIACORE_API FEudoriaTicketCheck Check(const FString& Ticket, const FString& WorldId, int64 NowUnixSeconds, const FEudoriaTicketKeys& Keys);

	/**
	 * Reads a JWK Set (RFC 7517) and keeps only usable ES256 public keys: kty "EC", crv "P-256", alg "ES256"
	 * and use "sig" when present, a kid, x and y of 32 bytes on the curve, and no private part "d".
	 * Unset when the document itself is unreadable.
	 */
	EUDORIACORE_API TOptional<FEudoriaTicketKeys> ParseJwkSet(const FString& Json);
}

/**
 * Memory of the accepted ticket identifiers ("jti"), each kept until its ticket can no longer be
 * accepted (exp + tolerance), then forgotten. Bounded: when full, new tickets are refused.
 * Not shared between instances and lost on restart (known limit, step 6). Game thread only.
 */
class EUDORIACORE_API FEudoriaReplayCache
{
public:
	enum class EOutcome : uint8
	{
		FirstUse,
		Replayed,
		Full,
	};

	static constexpr int32 DefaultCapacity = 100000;

	explicit FEudoriaReplayCache(int32 InCapacity = DefaultCapacity) : Capacity(InCapacity) {}

	EOutcome TryRemember(const FString& Jti, int64 ForgetAfterUnixSeconds, int64 NowUnixSeconds);

	int32 Num() const { return ForgetAfter.Num(); }

private:
	int32 Capacity;
	TMap<FString, int64> ForgetAfter;
	/** Min-heap by forget date, to forget the oldest first. */
	TArray<TPair<int64, FString>> ByExpiry;
};

/** Current time, in whole seconds since 1970-01-01 UTC. Replaceable by the tests. */
using FEudoriaUnixClock = TFunction<int64()>;

/** The real clock (FDateTime::UtcNow). */
EUDORIACORE_API FEudoriaUnixClock EudoriaSystemClock();

/**
 * The core's public ticket keys, as a world server keeps them: downloaded at start-up (Refresh), again
 * when 5 minutes old or when a ticket names an unknown kid — never more than once a minute, so forged
 * tickets cannot flood the core. When the core cannot be reached, the keys already known are kept.
 * Game thread only (the transport calls back there).
 */
class EUDORIACORE_API FEudoriaTicketKeyStore : public TSharedFromThis<FEudoriaTicketKeyStore>
{
public:
	static constexpr int64 RefreshIntervalSeconds = 300;
	static constexpr int64 MinDownloadIntervalSeconds = 60;
	static constexpr int32 MaxDocumentLength = 64 * 1024;

	/** Path of the key route, relative to the core's base URL (contract: GET /entry-tickets/keys). */
	static const TCHAR* KeysPath() { return TEXT("/entry-tickets/keys"); }

	FEudoriaTicketKeyStore(TSharedRef<IEudoriaHttpTransport> InTransport, FEudoriaUnixClock InClock);

	/** Downloads the keys now (start-up). The callback gets false when nothing usable was received. */
	void Refresh(TFunction<void(bool)> OnComplete);

	/** Downloads again if the keys are old, missing or lack this kid, within the download limit; then calls OnReady. */
	void EnsureFresh(const FString& KeyId, TFunction<void()> OnReady);

	const FEudoriaTicketKeys& GetKeys() const { return Keys; }

	/** Download attempts so far (successful or not). */
	int32 GetDownloads() const { return Downloads; }

private:
	bool NeedsDownload(const FString& KeyId) const;
	void Download();
	void FinishDownload(const FEudoriaHttpResponse& Response);

	TSharedRef<IEudoriaHttpTransport> Transport;
	FEudoriaUnixClock Clock;
	FEudoriaTicketKeys Keys;
	TOptional<int64> LastSuccess;
	TOptional<int64> LastAttempt;
	int32 Downloads = 0;
	bool bDownloading = false;
	bool bLastDownloadSucceeded = false;
	/** Callbacks waiting for the download in progress. */
	TArray<TFunction<void()>> Waiting;
};

/**
 * What a world server does when a player arrives with a ticket: checks it alone (no call to the core
 * except to download its public keys) and accepts each ticket once. The callback is called once, on
 * the game thread — at once when no key download is needed (the usual case).
 */
class EUDORIACORE_API FEudoriaWorldTicketVerifier : public TSharedFromThis<FEudoriaWorldTicketVerifier>
{
public:
	using FVerdictCallback = TFunction<void(const FEudoriaTicketVerdict&)>;

	/** WorldId: this world's identifier; tickets must have the audience "eudoria-world:<WorldId>". */
	FEudoriaWorldTicketVerifier(FString InWorldId, TSharedRef<FEudoriaTicketKeyStore> InKeys, TSharedRef<FEudoriaReplayCache> InReplays, FEudoriaUnixClock InClock);

	void Verify(const FString& Ticket, FVerdictCallback OnVerdict);

private:
	FEudoriaTicketVerdict Decide(const FString& Ticket);

	FString WorldId;
	TSharedRef<FEudoriaTicketKeyStore> Keys;
	TSharedRef<FEudoriaReplayCache> Replays;
	FEudoriaUnixClock Clock;
};

#endif // EUDORIA_WITH_TICKET_VERIFIER
