#include "EudoriaBlueprintRules.h"

#include "AssetRegistry/AssetRegistryModule.h"
#include "AssetRegistry/IAssetRegistry.h"
#include "EdGraph/EdGraph.h"
#include "EdGraph/EdGraphNode.h"
#include "EdGraphNode_Comment.h"
#include "Engine/Blueprint.h"
#include "Interfaces/IPluginManager.h"
#include "K2Node_FunctionEntry.h"
#include "K2Node_FunctionResult.h"
#include "K2Node_Tunnel.h"

namespace
{
	/** Nodes that Unreal adds by itself and that execute nothing. */
	bool IsHarmlessNode(const UEdGraphNode& Node)
	{
		return Node.IsAutomaticallyPlacedGhostNode()
			|| Node.IsA<UEdGraphNode_Comment>()
			|| Node.IsA<UK2Node_FunctionEntry>()
			|| Node.IsA<UK2Node_FunctionResult>()
			|| Node.IsA<UK2Node_Tunnel>(); // macro entry/exit (a macro with real nodes is still caught)
	}
}

namespace EudoriaBlueprintRules
{
	TArray<FString> FindLogic(const UBlueprint& Blueprint)
	{
		TArray<UEdGraph*> Graphs;
		Blueprint.GetAllGraphs(Graphs);

		TArray<FString> Logic;
		for (const UEdGraph* Graph : Graphs)
		{
			if (!Graph)
			{
				continue;
			}
			for (const UEdGraphNode* Node : Graph->Nodes)
			{
				if (Node && !IsHarmlessNode(*Node))
				{
					Logic.Add(FString::Printf(TEXT("%s: %s [%s]"), *Graph->GetName(),
						*Node->GetNodeTitle(ENodeTitleType::ListView).ToString(), *Node->GetClass()->GetName()));
				}
			}
		}
		return Logic;
	}

	TArray<FName> GetProjectContentRoots()
	{
		TArray<FName> Roots = {TEXT("/Game")};
		for (const TSharedRef<IPlugin>& Plugin : IPluginManager::Get().GetEnabledPluginsWithContent())
		{
			if (Plugin->GetLoadedFrom() == EPluginLoadedFrom::Project)
			{
				FString MountPoint = Plugin->GetMountedAssetPath();
				MountPoint.RemoveFromEnd(TEXT("/"));
				Roots.AddUnique(FName(*MountPoint));
			}
		}
		return Roots;
	}

	TArray<FString> ScanProjectBlueprints(int32& OutBlueprintCount)
	{
		IAssetRegistry& Registry = FAssetRegistryModule::GetRegistry();
		Registry.SearchAllAssets(/*bSynchronousSearch*/ true);

		FARFilter Filter;
		Filter.ClassPaths.Add(UBlueprint::StaticClass()->GetClassPathName());
		Filter.bRecursiveClasses = true; // widget Blueprints, animation Blueprints...
		Filter.PackagePaths = GetProjectContentRoots();
		Filter.bRecursivePaths = true;

		TArray<FAssetData> Assets;
		Registry.GetAssets(Filter, Assets);

		OutBlueprintCount = 0;
		TArray<FString> Logic;
		for (const FAssetData& Asset : Assets)
		{
			const UBlueprint* Blueprint = Cast<UBlueprint>(Asset.GetAsset());
			if (!Blueprint)
			{
				continue;
			}
			++OutBlueprintCount;
			for (const FString& Line : FindLogic(*Blueprint))
			{
				Logic.Add(FString::Printf(TEXT("%s -> %s"), *Asset.GetObjectPathString(), *Line));
			}
		}
		return Logic;
	}
}
