#include "RenderTest/EudoriaRenderTestSettings.h"

#include "Misc/PackageName.h"

#include UE_INLINE_GENERATED_CPP_BY_NAME(EudoriaRenderTestSettings)

namespace
{
	TArray<TPair<FString, FSoftObjectPath>> AllPaths(const UEudoriaRenderTestSettings& Settings)
	{
		return {
			{TEXT("Map"), Settings.Map.ToSoftObjectPath()},
			{TEXT("CharacterMesh"), Settings.CharacterMesh.ToSoftObjectPath()},
			{TEXT("Locomotion"), Settings.Locomotion.ToSoftObjectPath()},
			{TEXT("JumpStart"), Settings.JumpStart.ToSoftObjectPath()},
			{TEXT("FallLoop"), Settings.FallLoop.ToSoftObjectPath()},
			{TEXT("Land"), Settings.Land.ToSoftObjectPath()},
		};
	}
}

TArray<FString> UEudoriaRenderTestSettings::FindEmptySettings() const
{
	TArray<FString> Empty;
	for (const TPair<FString, FSoftObjectPath>& Entry : AllPaths(*this))
	{
		if (Entry.Value.IsNull())
		{
			Empty.Add(Entry.Key);
		}
	}
	return Empty;
}

namespace
{
	TArray<FString> FindMissing(const TArray<TPair<FString, FSoftObjectPath>>& Paths)
	{
		TArray<FString> Missing;
		for (const TPair<FString, FSoftObjectPath>& Entry : Paths)
		{
			if (Entry.Value.IsNull() || !FPackageName::DoesPackageExist(Entry.Value.GetLongPackageName()))
			{
				Missing.Add(Entry.Key);
			}
		}
		return Missing;
	}
}

TArray<FString> UEudoriaRenderTestSettings::FindMissingContent() const
{
	return FindMissing(AllPaths(*this));
}

TArray<FString> UEudoriaRenderTestSettings::FindMissingCharacterContent() const
{
	TArray<TPair<FString, FSoftObjectPath>> Paths = AllPaths(*this);
	Paths.RemoveAll([](const TPair<FString, FSoftObjectPath>& Entry) { return Entry.Key == TEXT("Map"); });
	return FindMissing(Paths);
}
