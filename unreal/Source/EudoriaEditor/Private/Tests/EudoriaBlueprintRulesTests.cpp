// "No logic in Blueprints" check (ADR-0003), run with the other tests by tools/dev/test-unreal.ps1.
// Category: Eudoria.Editor.

#include "AssetRegistry/AssetRegistryModule.h"
#include "EdGraph/EdGraph.h"
#include "EudoriaBlueprintRules.h"
#include "GameFramework/Actor.h"
#include "K2Node_CallFunction.h"
#include "Kismet/KismetSystemLibrary.h"
#include "Kismet2/BlueprintEditorUtils.h"
#include "Kismet2/KismetEditorUtilities.h"
#include "Misc/AutomationTest.h"
#include "Misc/PackageName.h"
#include "UObject/Package.h"

#if WITH_DEV_AUTOMATION_TESTS

namespace EudoriaBlueprintRulesTests
{
	constexpr EAutomationTestFlags EditorTestFlags = EAutomationTestFlags::EditorContext | EAutomationTestFlags::ProductFilter;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaNoBlueprintLogicTest, "Eudoria.Editor.NoBlueprintLogic", EudoriaBlueprintRulesTests::EditorTestFlags)
bool FEudoriaNoBlueprintLogicTest::RunTest(const FString&)
{
	int32 Checked = 0;
	const TArray<FString> Logic = EudoriaBlueprintRules::ScanProjectBlueprints(Checked);
	AddInfo(FString::Printf(TEXT("%d Blueprint(s) checked under %d content root(s)."), Checked, EudoriaBlueprintRules::GetProjectContentRoots().Num()));
	for (const FString& Line : Logic)
	{
		AddError(FString::Printf(TEXT("Logic in a Blueprint (forbidden by ADR-0003, move it to C++): %s"), *Line));
	}
	return Logic.IsEmpty();
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaNoBlueprintLogicDetectsFaultTest, "Eudoria.Editor.NoBlueprintLogicDetectsFaultyBlueprint", EudoriaBlueprintRulesTests::EditorTestFlags)
bool FEudoriaNoBlueprintLogicDetectsFaultTest::RunTest(const FString&)
{
	// A throwaway folder, in memory only: the Blueprint is never saved, and is removed at the end.
	const FString Folder = FString::Printf(TEXT("/Game/__EudoriaTmp_%s"), *FGuid::NewGuid().ToString(EGuidFormats::Digits));
	const FString PackageName = Folder + TEXT("/BP_Faulty");
	UPackage* Package = CreatePackage(*PackageName);
	UBlueprint* Blueprint = FKismetEditorUtilities::CreateBlueprint(AActor::StaticClass(), Package, TEXT("BP_Faulty"), BPTYPE_Normal);
	if (!TestNotNull(TEXT("temporary Blueprint created"), Blueprint))
	{
		return false;
	}
	FAssetRegistryModule::AssetCreated(Blueprint);

	// 1. A brand-new actor Blueprint (ghost BeginPlay/Tick, empty construction script) is compliant.
	TestEqual(TEXT("new Blueprint: Unreal's own ghost nodes are not logic"), EudoriaBlueprintRules::FindLogic(*Blueprint).Num(), 0);

	// 2. Add one real node (Print String) to the event graph: it must be detected, by the rule and by the project scan.
	UEdGraph* EventGraph = FBlueprintEditorUtils::FindEventGraph(Blueprint);
	if (TestNotNull(TEXT("event graph"), EventGraph))
	{
		FGraphNodeCreator<UK2Node_CallFunction> Creator(*EventGraph);
		UK2Node_CallFunction* Node = Creator.CreateNode();
		Node->SetFromFunction(UKismetSystemLibrary::StaticClass()->FindFunctionByName(GET_FUNCTION_NAME_CHECKED(UKismetSystemLibrary, PrintString)));
		Creator.Finalize();

		const TArray<FString> Logic = EudoriaBlueprintRules::FindLogic(*Blueprint);
		TestEqual(TEXT("one logic node detected"), Logic.Num(), 1);
		if (Logic.Num() == 1)
		{
			AddInfo(FString::Printf(TEXT("Detected: %s"), *Logic[0]));
			TestTrue(TEXT("the report names the node class"), Logic[0].Contains(TEXT("K2Node_CallFunction")));
		}

		int32 Checked = 0;
		const TArray<FString> Scan = EudoriaBlueprintRules::ScanProjectBlueprints(Checked);
		TestTrue(TEXT("the project scan reports the faulty Blueprint"),
			Scan.ContainsByPredicate([&PackageName](const FString& Line) { return Line.StartsWith(PackageName); }));
	}

	// 3. Remove everything: registry entry, objects; nothing was ever written to disk.
	FAssetRegistryModule::AssetDeleted(Blueprint);
	Blueprint->ClearFlags(RF_Public | RF_Standalone);
	Blueprint->MarkAsGarbage();
	Package->ClearFlags(RF_Public | RF_Standalone);
	Package->MarkAsGarbage();
	TestFalse(TEXT("no file on disk"), FPackageName::DoesPackageExist(PackageName));

	int32 Checked = 0;
	const TArray<FString> After = EudoriaBlueprintRules::ScanProjectBlueprints(Checked);
	TestFalse(TEXT("the throwaway Blueprint is gone from the scan"),
		After.ContainsByPredicate([&PackageName](const FString& Line) { return Line.StartsWith(PackageName); }));
	return true;
}

#endif // WITH_DEV_AUTOMATION_TESTS
