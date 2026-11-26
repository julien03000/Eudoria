#include "EudoriaCoreApi.h"

#include "Dom/JsonObject.h"
#include "Policies/CondensedJsonPrintPolicy.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"

#define LOCTEXT_NAMESPACE "EudoriaCoreApi"

bool FEudoriaApiError::HasCode(const TCHAR* Code) const
{
	return Errors.ContainsByPredicate([Code](const FEudoriaFieldError& Error) { return Error.Code == Code; });
}

namespace
{
	using FCondensedJsonWriter = TJsonWriter<TCHAR, TCondensedJsonPrintPolicy<TCHAR>>;

	TSharedPtr<FJsonObject> ParseJsonObject(const FString& Body)
	{
		TSharedPtr<FJsonObject> Object;
		if (Body.IsEmpty())
		{
			return nullptr;
		}
		const TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(Body);
		if (!FJsonSerializer::Deserialize(Reader, Object))
		{
			return nullptr;
		}
		return Object;
	}

	/** Retry-After as a number of seconds (RFC 9110 delay-seconds); -1 when absent or not a number. */
	int32 ParseRetryAfter(const FString& Value)
	{
		const FString Trimmed = Value.TrimStartAndEnd();
		if (Trimmed.IsEmpty() || Trimmed.Len() > 9)
		{
			return -1;
		}
		for (const TCHAR Character : Trimmed)
		{
			if (!FChar::IsDigit(Character))
			{
				return -1;
			}
		}
		return FCString::Atoi(*Trimmed);
	}

	FEudoriaApiError MakeApiError(EEudoriaApiErrorKind Kind, int32 StatusCode = 0)
	{
		FEudoriaApiError Error;
		Error.Kind = Kind;
		Error.StatusCode = StatusCode;
		return Error;
	}

	bool TryGetNonEmptyString(const FJsonObject& Object, const TCHAR* Field, FString& OutValue)
	{
		return Object.TryGetStringField(Field, OutValue) && !OutValue.IsEmpty();
	}

	FString WriteJsonObject(TFunctionRef<void(FCondensedJsonWriter&)> Fill)
	{
		FString Output;
		const TSharedRef<FCondensedJsonWriter> Writer = TJsonWriterFactory<TCHAR, TCondensedJsonPrintPolicy<TCHAR>>::Create(&Output);
		Writer->WriteObjectStart();
		Fill(*Writer);
		Writer->WriteObjectEnd();
		Writer->Close();
		return Output;
	}

	FText CodeToMessage(const FString& Code)
	{
		struct FEntry
		{
			const TCHAR* Code;
			FText Message;
		};
		static const FEntry Entries[] = {
			{TEXT("email_required"), LOCTEXT("EmailRequired", "Saisis ton adresse e-mail.")},
			{TEXT("email_invalid"), LOCTEXT("EmailInvalid", "Cette adresse e-mail n'est pas valide.")},
			{TEXT("pseudo_required"), LOCTEXT("PseudoRequired", "Choisis un pseudo.")},
			{TEXT("pseudo_too_short"), LOCTEXT("PseudoTooShort", "Le pseudo doit contenir au moins 3 caractères.")},
			{TEXT("pseudo_too_long"), LOCTEXT("PseudoTooLong", "Le pseudo doit contenir au plus 20 caractères.")},
			{TEXT("pseudo_invalid_characters"), LOCTEXT("PseudoInvalidCharacters", "Le pseudo ne peut contenir que des lettres sans accent, des chiffres, « _ » et « - ».")},
			{TEXT("pseudo_must_start_with_letter"), LOCTEXT("PseudoMustStartWithLetter", "Le pseudo doit commencer par une lettre.")},
			{TEXT("pseudo_taken"), LOCTEXT("PseudoTaken", "Ce pseudo est déjà pris.")},
			{TEXT("password_required"), LOCTEXT("PasswordRequired", "Saisis un mot de passe.")},
			{TEXT("password_too_short"), LOCTEXT("PasswordTooShort", "Le mot de passe doit contenir au moins 12 caractères.")},
			{TEXT("password_too_long"), LOCTEXT("PasswordTooLong", "Le mot de passe doit contenir au plus 128 caractères.")},
			{TEXT("password_too_common"), LOCTEXT("PasswordTooCommon", "Ce mot de passe est trop courant, ou contient ton pseudo, ton e-mail ou « eudoria ». Choisis-en un autre.")},
			{TEXT("password_unchanged"), LOCTEXT("PasswordUnchanged", "Le nouveau mot de passe doit être différent de l'ancien.")},
			{TEXT("invalid_credentials"), LOCTEXT("InvalidCredentials", "E-mail ou mot de passe incorrect.")},
			{TEXT("refresh_token_invalid"), LOCTEXT("RefreshTokenInvalid", "Ta session a expiré. Reconnecte-toi.")},
			{TEXT("world_unknown"), LOCTEXT("WorldUnknown", "Ce monde n'existe pas (ou plus).")},
			{TEXT("world_id_invalid"), LOCTEXT("WorldIdInvalid", "Identifiant de monde invalide.")},
		};
		for (const FEntry& Entry : Entries)
		{
			if (Code == Entry.Code)
			{
				return Entry.Message;
			}
		}
		return FText::GetEmpty();
	}

	FText WaitMessage(int32 RetryAfterSeconds)
	{
		if (RetryAfterSeconds < 0)
		{
			return LOCTEXT("TooManyRequestsLater", "Trop de tentatives. Réessaie plus tard.");
		}
		if (RetryAfterSeconds < 60)
		{
			return FText::Format(LOCTEXT("TooManyRequestsSeconds", "Trop de tentatives. Réessaie dans {0} s."), FText::AsNumber(FMath::Max(RetryAfterSeconds, 1)));
		}
		const int32 Minutes = FMath::DivideAndRoundUp(RetryAfterSeconds, 60);
		return FText::Format(LOCTEXT("TooManyRequestsMinutes", "Trop de tentatives. Réessaie dans {0} min."), FText::AsNumber(Minutes));
	}
}

namespace EudoriaCoreApi
{
	bool IsSecureBaseUrl(const FString& BaseUrl)
	{
		static const FString Scheme = TEXT("https://");
		if (!BaseUrl.StartsWith(Scheme, ESearchCase::IgnoreCase))
		{
			return false;
		}
		const FString Rest = BaseUrl.RightChop(Scheme.Len());
		int32 HostEnd = INDEX_NONE;
		Rest.FindChar(TEXT('/'), HostEnd);
		const FString Authority = HostEnd == INDEX_NONE ? Rest : Rest.Left(HostEnd);
		return !Authority.IsEmpty() && !Authority.Contains(TEXT("@")) && !Authority.Contains(TEXT(" "));
	}

	FEudoriaApiError ParseError(const FEudoriaHttpResponse& Response)
	{
		switch (Response.TransportStatus)
		{
		case EEudoriaTransportStatus::Failed:
			return MakeApiError(EEudoriaApiErrorKind::NetworkError);
		case EEudoriaTransportStatus::RefusedInsecureUrl:
			return MakeApiError(EEudoriaApiErrorKind::InsecureConfiguration);
		case EEudoriaTransportStatus::Received:
			break;
		}

		EEudoriaApiErrorKind Kind;
		switch (Response.StatusCode)
		{
		case 400: Kind = EEudoriaApiErrorKind::InvalidInput; break;
		case 401: Kind = EEudoriaApiErrorKind::Unauthorized; break;
		case 404: Kind = EEudoriaApiErrorKind::NotFound; break;
		case 409: Kind = EEudoriaApiErrorKind::Conflict; break;
		case 429: Kind = EEudoriaApiErrorKind::TooManyRequests; break;
		case 503: Kind = EEudoriaApiErrorKind::ServerBusy; break;
		default: return MakeApiError(EEudoriaApiErrorKind::UnexpectedResponse, Response.StatusCode);
		}

		FEudoriaApiError Error = MakeApiError(Kind, Response.StatusCode);
		Error.RetryAfterSeconds = ParseRetryAfter(Response.RetryAfter);

		// Problem details (RFC 9457) with an "errors" list; a 401 from /me has no body at all.
		if (const TSharedPtr<FJsonObject> Problem = ParseJsonObject(Response.Body))
		{
			const TArray<TSharedPtr<FJsonValue>>* Items = nullptr;
			if (Problem->TryGetArrayField(TEXT("errors"), Items))
			{
				for (const TSharedPtr<FJsonValue>& Item : *Items)
				{
					const TSharedPtr<FJsonObject>* ItemObject = nullptr;
					FEudoriaFieldError FieldError;
					if (Item.IsValid() && Item->TryGetObject(ItemObject) &&
						(*ItemObject)->TryGetStringField(TEXT("field"), FieldError.Field) &&
						(*ItemObject)->TryGetStringField(TEXT("code"), FieldError.Code))
					{
						Error.Errors.Add(MoveTemp(FieldError));
					}
				}
			}
		}
		return Error;
	}

	FEudoriaApiStatus ParseRegistration(const FEudoriaHttpResponse& Response)
	{
		if (Response.TransportStatus == EEudoriaTransportStatus::Received && Response.StatusCode == 202)
		{
			return {};
		}
		return ParseError(Response);
	}

	TValueOrError<FEudoriaTokenPair, FEudoriaApiError> ParseTokenPair(const FEudoriaHttpResponse& Response)
	{
		if (Response.TransportStatus != EEudoriaTransportStatus::Received || Response.StatusCode != 200)
		{
			return MakeError(ParseError(Response));
		}

		const TSharedPtr<FJsonObject> Object = ParseJsonObject(Response.Body);
		FEudoriaTokenPair Pair;
		FString TokenType;
		if (!Object.IsValid() ||
			!TryGetNonEmptyString(*Object, TEXT("accessToken"), Pair.AccessToken) ||
			!TryGetNonEmptyString(*Object, TEXT("refreshToken"), Pair.RefreshToken) ||
			!Object->TryGetStringField(TEXT("tokenType"), TokenType) || TokenType != TEXT("Bearer") ||
			!Object->TryGetNumberField(TEXT("expiresIn"), Pair.ExpiresInSeconds) || Pair.ExpiresInSeconds <= 0 ||
			!Object->TryGetNumberField(TEXT("refreshExpiresIn"), Pair.RefreshExpiresInSeconds) || Pair.RefreshExpiresInSeconds <= 0)
		{
			return MakeError(MakeApiError(EEudoriaApiErrorKind::UnexpectedResponse, Response.StatusCode));
		}
		return MakeValue(MoveTemp(Pair));
	}

	TValueOrError<FEudoriaProfile, FEudoriaApiError> ParseProfile(const FEudoriaHttpResponse& Response)
	{
		if (Response.TransportStatus != EEudoriaTransportStatus::Received || Response.StatusCode != 200)
		{
			return MakeError(ParseError(Response));
		}

		const TSharedPtr<FJsonObject> Object = ParseJsonObject(Response.Body);
		FEudoriaProfile Profile;
		FString CreatedAt;
		if (!Object.IsValid() ||
			!TryGetNonEmptyString(*Object, TEXT("id"), Profile.Id) ||
			!TryGetNonEmptyString(*Object, TEXT("pseudo"), Profile.Pseudo) ||
			!Object->TryGetStringField(TEXT("createdAt"), CreatedAt) ||
			!FDateTime::ParseIso8601(*CreatedAt, Profile.CreatedAt))
		{
			return MakeError(MakeApiError(EEudoriaApiErrorKind::UnexpectedResponse, Response.StatusCode));
		}
		return MakeValue(MoveTemp(Profile));
	}

	bool IsValidWorldId(const FString& WorldId)
	{
		if (WorldId.IsEmpty() || WorldId.Len() > 64 || WorldId.StartsWith(TEXT("-")) || WorldId.EndsWith(TEXT("-")))
		{
			return false;
		}
		for (const TCHAR Character : WorldId)
		{
			const bool bAllowed = (Character >= TEXT('a') && Character <= TEXT('z')) || (Character >= TEXT('0') && Character <= TEXT('9')) || Character == TEXT('-');
			if (!bAllowed)
			{
				return false;
			}
		}
		return true;
	}

	TValueOrError<FEudoriaEntryTicket, FEudoriaApiError> ParseEntryTicket(const FEudoriaHttpResponse& Response)
	{
		if (Response.TransportStatus != EEudoriaTransportStatus::Received || Response.StatusCode != 200)
		{
			return MakeError(ParseError(Response));
		}

		const TSharedPtr<FJsonObject> Object = ParseJsonObject(Response.Body);
		FEudoriaEntryTicket Ticket;
		if (!Object.IsValid() ||
			!TryGetNonEmptyString(*Object, TEXT("ticket"), Ticket.Ticket) ||
			!Object->TryGetNumberField(TEXT("expiresIn"), Ticket.ExpiresInSeconds) || Ticket.ExpiresInSeconds <= 0)
		{
			return MakeError(MakeApiError(EEudoriaApiErrorKind::UnexpectedResponse, Response.StatusCode));
		}
		return MakeValue(MoveTemp(Ticket));
	}

	FEudoriaApiStatus ParseLogout(const FEudoriaHttpResponse& Response)
	{
		if (Response.TransportStatus == EEudoriaTransportStatus::Received && Response.StatusCode == 204)
		{
			return {};
		}
		return ParseError(Response);
	}

	FString MakeRegisterBody(const FString& Email, const FString& Pseudo, const FString& Password)
	{
		return WriteJsonObject([&](FCondensedJsonWriter& Writer)
		{
			Writer.WriteValue(TEXT("email"), Email);
			Writer.WriteValue(TEXT("pseudo"), Pseudo);
			Writer.WriteValue(TEXT("password"), Password);
		});
	}

	FString MakeLoginBody(const FString& Email, const FString& Password)
	{
		return WriteJsonObject([&](FCondensedJsonWriter& Writer)
		{
			Writer.WriteValue(TEXT("email"), Email);
			Writer.WriteValue(TEXT("password"), Password);
		});
	}

	FString MakeRefreshTokenBody(const FString& RefreshToken)
	{
		return WriteJsonObject([&](FCondensedJsonWriter& Writer) { Writer.WriteValue(TEXT("refreshToken"), RefreshToken); });
	}

	FText ToUserMessage(const FEudoriaApiError& Error)
	{
		switch (Error.Kind)
		{
		case EEudoriaApiErrorKind::TooManyRequests:
			return WaitMessage(Error.RetryAfterSeconds);
		case EEudoriaApiErrorKind::ServerBusy:
			return LOCTEXT("ServerBusy", "Le serveur EUDORIA est très sollicité. Réessaie dans quelques secondes.");
		case EEudoriaApiErrorKind::SessionEnded:
			return LOCTEXT("SessionEnded", "Ta session a expiré. Reconnecte-toi.");
		case EEudoriaApiErrorKind::NetworkError:
			return LOCTEXT("NetworkError", "Impossible de joindre le serveur EUDORIA. Vérifie ta connexion et réessaie.");
		case EEudoriaApiErrorKind::InsecureConfiguration:
			return LOCTEXT("InsecureConfiguration", "Configuration invalide : l'adresse du serveur EUDORIA doit commencer par https://.");
		case EEudoriaApiErrorKind::UnexpectedResponse:
			return LOCTEXT("UnexpectedResponse", "Réponse inattendue du serveur EUDORIA. Réessaie plus tard.");
		case EEudoriaApiErrorKind::InvalidInput:
		case EEudoriaApiErrorKind::Unauthorized:
		case EEudoriaApiErrorKind::NotFound:
		case EEudoriaApiErrorKind::Conflict:
			break;
		}

		// One line per known code, without duplicates, in the order the core sent them.
		TArray<FString> Lines;
		for (const FEudoriaFieldError& FieldError : Error.Errors)
		{
			const FText Message = CodeToMessage(FieldError.Code);
			if (!Message.IsEmpty())
			{
				Lines.AddUnique(Message.ToString());
			}
		}
		if (Lines.IsEmpty())
		{
			return Error.Kind == EEudoriaApiErrorKind::Unauthorized
				? LOCTEXT("UnauthorizedFallback", "Ta session a expiré. Reconnecte-toi.")
				: LOCTEXT("GenericError", "Une erreur est survenue. Réessaie plus tard.");
		}
		return FText::FromString(FString::Join(Lines, TEXT("\n")));
	}
}

#undef LOCTEXT_NAMESPACE
