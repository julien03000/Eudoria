using UnrealBuildTool;

/// <summary>Editor-only module: checks and tools for the editor (never shipped in the client or the servers).</summary>
public class EudoriaEditor : ModuleRules
{
	public EudoriaEditor(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;

		PublicDependencyModuleNames.AddRange(new string[] { "Core", "CoreUObject", "Engine" });
		PrivateDependencyModuleNames.AddRange(new string[] { "UnrealEd", "BlueprintGraph", "AssetRegistry", "Projects" });
	}
}
