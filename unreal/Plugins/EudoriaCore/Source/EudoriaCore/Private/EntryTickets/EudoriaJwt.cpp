#include "EudoriaJwt.h"

#if EUDORIA_WITH_TICKET_VERIFIER

#include "Containers/StringConv.h"
#include "Serialization/JsonReader.h"

namespace EudoriaJwt
{
	namespace
	{
		const ANSICHAR Alphabet[] = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

		/** Value of one Base64 URL-safe character, or -1. */
		int32 ValueOf(TCHAR Character)
		{
			if (Character >= TEXT('A') && Character <= TEXT('Z')) { return Character - TEXT('A'); }
			if (Character >= TEXT('a') && Character <= TEXT('z')) { return Character - TEXT('a') + 26; }
			if (Character >= TEXT('0') && Character <= TEXT('9')) { return Character - TEXT('0') + 52; }
			if (Character == TEXT('-')) { return 62; }
			if (Character == TEXT('_')) { return 63; }
			return -1;
		}

		bool IsContinuation(uint8 Byte) { return (Byte & 0xC0) == 0x80; }

		/** Unicode, table 3-7 ("well-formed UTF-8 byte sequences"). */
		bool IsWellFormedUtf8(TConstArrayView<uint8> Bytes)
		{
			int32 Index = 0;
			while (Index < Bytes.Num())
			{
				const uint8 Lead = Bytes[Index];
				int32 Length;
				uint8 SecondMin = 0x80;
				uint8 SecondMax = 0xBF;
				if (Lead <= 0x7F) { ++Index; continue; }
				else if (Lead >= 0xC2 && Lead <= 0xDF) { Length = 2; }
				else if (Lead == 0xE0) { Length = 3; SecondMin = 0xA0; }
				else if (Lead >= 0xE1 && Lead <= 0xEC) { Length = 3; }
				else if (Lead == 0xED) { Length = 3; SecondMax = 0x9F; } // no surrogates
				else if (Lead >= 0xEE && Lead <= 0xEF) { Length = 3; }
				else if (Lead == 0xF0) { Length = 4; SecondMin = 0x90; }
				else if (Lead >= 0xF1 && Lead <= 0xF3) { Length = 4; }
				else if (Lead == 0xF4) { Length = 4; SecondMax = 0x8F; } // nothing above U+10FFFF
				else { return false; }

				if (Index + Length > Bytes.Num() || Bytes[Index + 1] < SecondMin || Bytes[Index + 1] > SecondMax)
				{
					return false;
				}
				for (int32 Offset = 2; Offset < Length; ++Offset)
				{
					if (!IsContinuation(Bytes[Index + Offset]))
					{
						return false;
					}
				}
				Index += Length;
			}
			return true;
		}

		constexpr int32 MaxDepth = 4;
	}

	bool DecodeBase64Url(FStringView Text, TArray<uint8>& OutBytes)
	{
		OutBytes.Reset();
		const int32 Remainder = Text.Len() % 4;
		if (Remainder == 1)
		{
			return false;
		}
		OutBytes.Reserve(Text.Len() * 3 / 4);

		uint32 Buffer = 0;
		int32 BufferedBits = 0;
		for (const TCHAR Character : Text)
		{
			const int32 Value = ValueOf(Character);
			if (Value < 0)
			{
				return false;
			}
			Buffer = (Buffer << 6) | static_cast<uint32>(Value);
			BufferedBits += 6;
			if (BufferedBits >= 8)
			{
				BufferedBits -= 8;
				OutBytes.Add(static_cast<uint8>((Buffer >> BufferedBits) & 0xFF));
			}
		}
		// Left-over bits (2 or 4) of the last character must be zero: otherwise the same bytes would
		// have several encodings, and the .NET decoder refuses them too.
		return (Buffer & ((1u << BufferedBits) - 1)) == 0;
	}

	FString EncodeBase64Url(TConstArrayView<uint8> Bytes)
	{
		FString Text;
		Text.Reserve((Bytes.Num() * 4 + 2) / 3);
		uint32 Buffer = 0;
		int32 BufferedBits = 0;
		for (const uint8 Byte : Bytes)
		{
			Buffer = (Buffer << 8) | Byte;
			BufferedBits += 8;
			while (BufferedBits >= 6)
			{
				BufferedBits -= 6;
				Text.AppendChar(Alphabet[(Buffer >> BufferedBits) & 0x3F]);
			}
		}
		if (BufferedBits > 0)
		{
			Text.AppendChar(Alphabet[(Buffer << (6 - BufferedBits)) & 0x3F]);
		}
		return Text;
	}

	bool DecodeUtf8(TConstArrayView<uint8> Bytes, FString& OutText)
	{
		if (!IsWellFormedUtf8(Bytes))
		{
			return false;
		}
		const FUTF8ToTCHAR Converted(reinterpret_cast<const UTF8CHAR*>(Bytes.GetData()), Bytes.Num());
		OutText = FString::ConstructFromPtrSize(Converted.Get(), Converted.Length());
		return true;
	}

	bool ReadObject(const FString& Json, TMap<FString, FJsonMember>& OutMembers)
	{
		OutMembers.Reset();
		struct FLevel
		{
			bool bIsObject = false;
			TSet<FString> Names;
		};
		TArray<FLevel, TInlineAllocator<MaxDepth>> Levels;
		bool bRootSeen = false;

		const TSharedRef<TJsonReader<TCHAR>> Reader = TJsonReaderFactory<TCHAR>::Create(Json);
		EJsonNotation Notation = EJsonNotation::Error;
		while (Reader->ReadNext(Notation))
		{
			if (Notation == EJsonNotation::Error)
			{
				return false;
			}
			const bool bClosing = Notation == EJsonNotation::ObjectEnd || Notation == EJsonNotation::ArrayEnd;

			if (Levels.IsEmpty())
			{
				// The root: exactly one object.
				if (bRootSeen || Notation != EJsonNotation::ObjectStart)
				{
					return false;
				}
				bRootSeen = true;
			}
			else if (!bClosing && Levels.Last().bIsObject)
			{
				// A member of an object: its name must be new in that object (any depth).
				bool bAlreadyThere = false;
				Levels.Last().Names.Add(Reader->GetIdentifier(), &bAlreadyThere);
				if (bAlreadyThere)
				{
					return false;
				}
			}

			const bool bTopLevelMember = Levels.Num() == 1 && !bClosing;
			switch (Notation)
			{
			case EJsonNotation::ObjectStart:
			case EJsonNotation::ArrayStart:
				if (bTopLevelMember)
				{
					OutMembers.Add(Reader->GetIdentifier(), FJsonMember{FJsonMember::EKind::Other, FString()});
				}
				Levels.Add(FLevel{Notation == EJsonNotation::ObjectStart, {}});
				if (Levels.Num() > MaxDepth)
				{
					return false;
				}
				break;
			case EJsonNotation::ObjectEnd:
			case EJsonNotation::ArrayEnd:
				Levels.Pop(EAllowShrinking::No);
				break;
			case EJsonNotation::String:
				if (bTopLevelMember)
				{
					OutMembers.Add(Reader->GetIdentifier(), FJsonMember{FJsonMember::EKind::String, FString(Reader->GetValueAsString())});
				}
				break;
			case EJsonNotation::Number:
				if (bTopLevelMember)
				{
					OutMembers.Add(Reader->GetIdentifier(), FJsonMember{FJsonMember::EKind::Number, FString(Reader->GetValueAsNumberString())});
				}
				break;
			default:
				if (bTopLevelMember)
				{
					OutMembers.Add(Reader->GetIdentifier(), FJsonMember{FJsonMember::EKind::Other, FString()});
				}
				break;
			}
		}
		return bRootSeen && Levels.IsEmpty() && Reader->GetErrorMessage().IsEmpty();
	}
}

#endif // EUDORIA_WITH_TICKET_VERIFIER
