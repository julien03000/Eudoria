#include "EudoriaEntryTickets.h"

#if EUDORIA_WITH_TICKET_VERIFIER

#include "Dom/JsonObject.h"
#include "EudoriaEs256.h"
#include "EudoriaJwt.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"

namespace
{
	using EudoriaJwt::FJsonMember;

	/** The three decoded parts of a ticket. */
	struct FTicketParts
	{
		TArray<uint8> Header;
		TArray<uint8> Payload;
		TArray<uint8> Signature;
		/** Length of "header.payload", the signed text. */
		int32 SigningInputLength = 0;
	};

	/** Exactly three parts separated by '.', each strict Base64 URL-safe; header and payload not empty. */
	bool Split(const FString& Ticket, FTicketParts& Out)
	{
		if (Ticket.Len() > EudoriaEntryTicketRules::MaxTicketLength)
		{
			return false;
		}
		int32 FirstDot = INDEX_NONE;
		int32 LastDot = INDEX_NONE;
		if (!Ticket.FindChar(TEXT('.'), FirstDot) || !Ticket.FindLastChar(TEXT('.'), LastDot) || FirstDot == LastDot)
		{
			return false;
		}
		const FStringView Text(Ticket);
		const FStringView Header = Text.Left(FirstDot);
		const FStringView Payload = Text.Mid(FirstDot + 1, LastDot - FirstDot - 1);
		const FStringView Signature = Text.RightChop(LastDot + 1);
		int32 ExtraDot = INDEX_NONE;
		if (Header.IsEmpty() || Payload.IsEmpty() || Payload.FindChar(TEXT('.'), ExtraDot))
		{
			return false;
		}
		Out.SigningInputLength = LastDot;
		return EudoriaJwt::DecodeBase64Url(Header, Out.Header) && EudoriaJwt::DecodeBase64Url(Payload, Out.Payload)
			&& EudoriaJwt::DecodeBase64Url(Signature, Out.Signature);
	}

	/** A JSON object from UTF-8 bytes, read strictly (valid UTF-8, no duplicate member). */
	bool ReadJsonObject(TConstArrayView<uint8> Bytes, TMap<FString, FJsonMember>& OutMembers)
	{
		FString Json;
		return EudoriaJwt::DecodeUtf8(Bytes, Json) && EudoriaJwt::ReadObject(Json, OutMembers);
	}

	/** The member's value when it is a JSON string; unset otherwise (absent, number, list...). */
	TOptional<FString> StringMember(const TMap<FString, FJsonMember>& Members, const TCHAR* Name)
	{
		const FJsonMember* Member = Members.Find(Name);
		return Member != nullptr && Member->Kind == FJsonMember::EKind::String ? TOptional<FString>(Member->Text) : TOptional<FString>();
	}

	/** A NumericDate written as a whole number: optional '-' then digits, within int64. 1.5, 1e3 or "1" are refused. */
	TOptional<int64> IntegerMember(const TMap<FString, FJsonMember>& Members, const TCHAR* Name)
	{
		const FJsonMember* Member = Members.Find(Name);
		if (Member == nullptr || Member->Kind != FJsonMember::EKind::Number)
		{
			return {};
		}
		const FString& Text = Member->Text;
		const bool bNegative = Text.StartsWith(TEXT("-"));
		const int32 Start = bNegative ? 1 : 0;
		if (Text.Len() == Start)
		{
			return {};
		}
		uint64 Magnitude = 0;
		for (int32 Index = Start; Index < Text.Len(); ++Index)
		{
			if (!FChar::IsDigit(Text[Index]))
			{
				return {};
			}
			const uint64 Digit = static_cast<uint64>(Text[Index] - TEXT('0'));
			if (Magnitude > (static_cast<uint64>(MAX_int64) + (bNegative ? 1 : 0) - Digit) / 10)
			{
				return {}; // does not fit in an int64
			}
			Magnitude = Magnitude * 10 + Digit;
		}
		return bNegative ? static_cast<int64>(0 - Magnitude) : static_cast<int64>(Magnitude);
	}

	/** 8-4-4-4-12 lower-case hexadecimal digits, as the core writes account identifiers. */
	bool IsLowerCaseUuid(const FString& Value)
	{
		if (Value.Len() != 36)
		{
			return false;
		}
		for (int32 Index = 0; Index < 36; ++Index)
		{
			const TCHAR Character = Value[Index];
			const bool bDash = Index == 8 || Index == 13 || Index == 18 || Index == 23;
			const bool bOk = bDash ? Character == TEXT('-')
				: (Character >= TEXT('0') && Character <= TEXT('9')) || (Character >= TEXT('a') && Character <= TEXT('f'));
			if (!bOk)
			{
				return false;
			}
		}
		return true;
	}

	/** a + b without overflow (clamped). */
	int64 SaturatingAdd(int64 A, int64 B)
	{
		return A > MAX_int64 - B ? MAX_int64 : A + B;
	}

	/** A member that must be a JSON string, if present at all. */
	TOptional<FString> TypedString(const FJsonObject& Object, const TCHAR* Name)
	{
		FString Value;
		return Object.HasTypedField<EJson::String>(Name) && Object.TryGetStringField(Name, Value) ? TOptional<FString>(Value) : TOptional<FString>();
	}

	bool TryReadKey(const FJsonObject& Jwk, FString& OutKeyId, FEudoriaTicketPublicKey& OutKey)
	{
		const TOptional<FString> Kty = TypedString(Jwk, TEXT("kty"));
		const TOptional<FString> Crv = TypedString(Jwk, TEXT("crv"));
		const TOptional<FString> Alg = TypedString(Jwk, TEXT("alg"));
		const TOptional<FString> Use = TypedString(Jwk, TEXT("use"));
		if (Jwk.HasField(TEXT("d")) || Kty.Get(FString()) != TEXT("EC") || Crv.Get(FString()) != TEXT("P-256")
			|| (Jwk.HasField(TEXT("alg")) && Alg.Get(FString()) != TEXT("ES256"))
			|| (Jwk.HasField(TEXT("use")) && Use.Get(FString()) != TEXT("sig")))
		{
			return false;
		}
		const TOptional<FString> KeyId = TypedString(Jwk, TEXT("kid"));
		const TOptional<FString> X = TypedString(Jwk, TEXT("x"));
		const TOptional<FString> Y = TypedString(Jwk, TEXT("y"));
		if (!KeyId.IsSet() || KeyId->IsEmpty() || !X.IsSet() || !Y.IsSet()
			|| !EudoriaJwt::DecodeBase64Url(*X, OutKey.X) || OutKey.X.Num() != 32
			|| !EudoriaJwt::DecodeBase64Url(*Y, OutKey.Y) || OutKey.Y.Num() != 32
			|| !EudoriaEs256::IsValidPublicKey(OutKey.X, OutKey.Y))
		{
			return false;
		}
		OutKeyId = *KeyId;
		return true;
	}
}

namespace EudoriaEntryTicketRules
{
	FEudoriaTicketCheck ReadHeader(const FString& Ticket)
	{
		FTicketParts Parts;
		TMap<FString, FJsonMember> Header;
		if (!Split(Ticket, Parts) || !ReadJsonObject(Parts.Header, Header))
		{
			return FEudoriaTicketCheck::Refused(EudoriaTicketRejection::Malformed);
		}
		for (const TPair<FString, FJsonMember>& Member : Header)
		{
			// Only these three, as strings: "crit", "jku", "jwk", "x5u"... are never honoured, so refused.
			const bool bKnown = Member.Key == TEXT("alg") || Member.Key == TEXT("typ") || Member.Key == TEXT("kid");
			if (!bKnown || Member.Value.Kind != FJsonMember::EKind::String)
			{
				return FEudoriaTicketCheck::Refused(EudoriaTicketRejection::Malformed);
			}
		}
		// Exact, case-sensitive comparisons: the core writes these values.
		const TOptional<FString> Algorithm = StringMember(Header, TEXT("alg"));
		if (!Algorithm.IsSet() || !Algorithm->Equals(ExpectedAlgorithm, ESearchCase::CaseSensitive))
		{
			return FEudoriaTicketCheck::Refused(EudoriaTicketRejection::AlgorithmNotAllowed);
		}
		const TOptional<FString> Type = StringMember(Header, TEXT("typ"));
		if (!Type.IsSet() || !Type->Equals(ExpectedType, ESearchCase::CaseSensitive))
		{
			return FEudoriaTicketCheck::Refused(EudoriaTicketRejection::TypeNotAllowed);
		}
		const FString KeyId = StringMember(Header, TEXT("kid")).Get(FString());
		if (KeyId.IsEmpty())
		{
			return FEudoriaTicketCheck::Refused(EudoriaTicketRejection::Malformed);
		}
		FEudoriaTicketCheck Check;
		Check.KeyId = KeyId;
		return Check;
	}

	FEudoriaTicketCheck Check(const FString& Ticket, const FString& WorldId, int64 NowUnixSeconds, const FEudoriaTicketKeys& Keys)
	{
		FEudoriaTicketCheck Header = ReadHeader(Ticket);
		if (!Header.Rejection.IsEmpty())
		{
			return Header;
		}
		const FEudoriaTicketPublicKey* Key = Keys.Find(Header.KeyId);
		if (Key == nullptr)
		{
			return FEudoriaTicketCheck::Refused(EudoriaTicketRejection::KeyUnknown);
		}

		FTicketParts Parts;
		Split(Ticket, Parts);
		// The ticket is pure ASCII here (Base64 URL-safe and dots): its characters are the signed bytes.
		const FTCHARToUTF8 SigningInput(*Ticket, Parts.SigningInputLength);
		if (!EudoriaEs256::Verify(Key->X, Key->Y,
			TConstArrayView<uint8>(reinterpret_cast<const uint8*>(SigningInput.Get()), SigningInput.Length()), Parts.Signature))
		{
			return FEudoriaTicketCheck::Refused(EudoriaTicketRejection::SignatureInvalid);
		}

		TMap<FString, FJsonMember> Claims;
		if (!ReadJsonObject(Parts.Payload, Claims))
		{
			return FEudoriaTicketCheck::Refused(EudoriaTicketRejection::Malformed);
		}
		const TOptional<FString> Issuer = StringMember(Claims, TEXT("iss"));
		if (!Issuer.IsSet() || !Issuer->Equals(ExpectedIssuer, ESearchCase::CaseSensitive))
		{
			return FEudoriaTicketCheck::Refused(EudoriaTicketRejection::IssuerInvalid);
		}
		// A list is refused, even one holding only our audience: the ticket names one world.
		const TOptional<FString> Audience = StringMember(Claims, TEXT("aud"));
		if (!Audience.IsSet() || !Audience->Equals(FString(AudiencePrefix) + WorldId, ESearchCase::CaseSensitive))
		{
			return FEudoriaTicketCheck::Refused(EudoriaTicketRejection::AudienceInvalid);
		}

		const TOptional<FString> Subject = StringMember(Claims, TEXT("sub"));
		const TOptional<FString> Jti = StringMember(Claims, TEXT("jti"));
		const TOptional<int64> IssuedAt = IntegerMember(Claims, TEXT("iat"));
		const TOptional<int64> ExpiresAt = IntegerMember(Claims, TEXT("exp"));
		if (!Subject.IsSet() || !IsLowerCaseUuid(*Subject) || !Jti.IsSet() || Jti->IsEmpty() || Jti->Len() > MaxJtiLength
			|| !IssuedAt.IsSet() || !ExpiresAt.IsSet() || *ExpiresAt <= *IssuedAt)
		{
			return FEudoriaTicketCheck::Refused(EudoriaTicketRejection::ClaimsInvalid);
		}
		if (NowUnixSeconds > SaturatingAdd(*ExpiresAt, ClockToleranceSeconds))
		{
			return FEudoriaTicketCheck::Refused(EudoriaTicketRejection::Expired);
		}
		if (*IssuedAt > SaturatingAdd(NowUnixSeconds, ClockToleranceSeconds))
		{
			return FEudoriaTicketCheck::Refused(EudoriaTicketRejection::IssuedInTheFuture);
		}

		FEudoriaTicketCheck Accepted;
		Accepted.KeyId = Header.KeyId;
		Accepted.Ticket = FEudoriaCheckedTicket{*Subject, *Jti, *ExpiresAt};
		return Accepted;
	}

	TOptional<FEudoriaTicketKeys> ParseJwkSet(const FString& Json)
	{
		// Same strictness as the tickets (one object, no duplicate member at any depth), then navigation.
		TMap<FString, FJsonMember> Members;
		TSharedPtr<FJsonObject> Document;
		const TArray<TSharedPtr<FJsonValue>>* List = nullptr;
		if (!EudoriaJwt::ReadObject(Json, Members)
			|| !FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Json), Document) || !Document.IsValid()
			|| !Document->HasTypedField<EJson::Array>(TEXT("keys")) || !Document->TryGetArrayField(TEXT("keys"), List))
		{
			return {};
		}

		FEudoriaTicketKeys Keys;
		for (const TSharedPtr<FJsonValue>& Item : *List)
		{
			FString KeyId;
			FEudoriaTicketPublicKey Key;
			if (Item.IsValid() && Item->Type == EJson::Object && TryReadKey(*Item->AsObject(), KeyId, Key) && !Keys.Contains(KeyId))
			{
				Keys.Add(KeyId, MoveTemp(Key)); // a repeated kid keeps its first key
			}
		}
		return Keys;
	}
}

#endif // EUDORIA_WITH_TICKET_VERIFIER
