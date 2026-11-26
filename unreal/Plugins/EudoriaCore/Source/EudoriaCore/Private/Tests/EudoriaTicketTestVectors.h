#pragma once

// Loads the shared entry ticket test vectors (shared/test-vectors/entry-tickets/, made by
// tools/dev/new-entry-ticket-vectors.ps1): the same files the .NET verifier checks. Tests only.

#include "CoreMinimal.h"
#include "Dom/JsonObject.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"

namespace EudoriaTicketTestVectors
{
	struct FVector
	{
		FString Name;
		FString Ticket;
		int64 Now = 0;
		/** "accepted" or a rejection code. */
		FString Expected;
	};

	struct FVectors
	{
		FString World;
		FString AccountId;
		int64 ClockToleranceSeconds = 0;
		/** keys.json, as the core's key route would send it. */
		FString KeysJson;
		/** Coordinates of the first published key (Base64 URL-safe). */
		FString KeyX;
		FString KeyY;
		TArray<FVector> Vectors;

		const FVector* Find(const TCHAR* Name) const
		{
			return Vectors.FindByPredicate([Name](const FVector& Vector) { return Vector.Name == Name; });
		}
	};

	/** The folder of the shared vectors: <repository>/shared/test-vectors/entry-tickets. */
	inline FString Directory()
	{
		return FPaths::ConvertRelativePathToFull(FPaths::Combine(FPaths::ProjectDir(), TEXT("../shared/test-vectors/entry-tickets")));
	}

	inline bool Load(FVectors& Out)
	{
		FString VectorsJson;
		if (!FFileHelper::LoadFileToString(VectorsJson, *FPaths::Combine(Directory(), TEXT("vectors.json")))
			|| !FFileHelper::LoadFileToString(Out.KeysJson, *FPaths::Combine(Directory(), TEXT("keys.json"))))
		{
			return false;
		}

		TSharedPtr<FJsonObject> Document;
		TSharedPtr<FJsonObject> Keys;
		if (!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(VectorsJson), Document) || !Document.IsValid()
			|| !FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Out.KeysJson), Keys) || !Keys.IsValid())
		{
			return false;
		}

		const TArray<TSharedPtr<FJsonValue>>* KeyList = nullptr;
		if (!Keys->TryGetArrayField(TEXT("keys"), KeyList) || KeyList->IsEmpty()
			|| !(*KeyList)[0]->AsObject()->TryGetStringField(TEXT("x"), Out.KeyX)
			|| !(*KeyList)[0]->AsObject()->TryGetStringField(TEXT("y"), Out.KeyY))
		{
			return false;
		}

		const TArray<TSharedPtr<FJsonValue>>* List = nullptr;
		if (!Document->TryGetStringField(TEXT("world"), Out.World) || !Document->TryGetStringField(TEXT("accountId"), Out.AccountId)
			|| !Document->TryGetNumberField(TEXT("clockToleranceSeconds"), Out.ClockToleranceSeconds)
			|| !Document->TryGetArrayField(TEXT("vectors"), List))
		{
			return false;
		}
		for (const TSharedPtr<FJsonValue>& Item : *List)
		{
			const TSharedPtr<FJsonObject> Object = Item->AsObject();
			FVector Vector;
			if (!Object.IsValid() || !Object->TryGetStringField(TEXT("name"), Vector.Name) || !Object->TryGetStringField(TEXT("ticket"), Vector.Ticket)
				|| !Object->TryGetNumberField(TEXT("now"), Vector.Now) || !Object->TryGetStringField(TEXT("expected"), Vector.Expected))
			{
				return false;
			}
			Out.Vectors.Add(MoveTemp(Vector));
		}
		return !Out.Vectors.IsEmpty();
	}
}
