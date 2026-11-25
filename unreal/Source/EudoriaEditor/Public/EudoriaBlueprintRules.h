#pragma once

#include "CoreMinimal.h"

class UBlueprint;

/**
 * ADR-0003 rule: Blueprints only assign assets and settings to C++ classes; their graphs hold NO logic.
 *
 * A graph node counts as logic unless it is one of the nodes Unreal places on its own and that do nothing:
 * the disabled "ghost" events of a new Blueprint (BeginPlay, Tick...), function entry/result nodes, macro
 * tunnels, and comments. (Python cannot do this check: graph nodes are protected from scripts; see plan 1.5, § 14.)
 */
namespace EudoriaBlueprintRules
{
	/** One line per logic node found in the Blueprint ("Graph: node title [node class]"); empty = compliant. */
	EUDORIAEDITOR_API TArray<FString> FindLogic(const UBlueprint& Blueprint);

	/** Content roots of the project: /Game and every project plugin that has content. */
	EUDORIAEDITOR_API TArray<FName> GetProjectContentRoots();

	/**
	 * Checks every Blueprint (widget Blueprints included) under the project content roots.
	 * Returns one line per logic node, prefixed by the asset path; OutBlueprintCount = Blueprints checked.
	 */
	EUDORIAEDITOR_API TArray<FString> ScanProjectBlueprints(int32& OutBlueprintCount);
}
