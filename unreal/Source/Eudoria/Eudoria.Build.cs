using UnrealBuildTool;

/// <summary>Main module: the client front-end (game mode, screens). Depends only on the EudoriaCore plugin.</summary>
public class Eudoria : ModuleRules
{
	public Eudoria(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;

		PublicDependencyModuleNames.AddRange(new string[] { "Core", "CoreUObject", "Engine" });
		PrivateDependencyModuleNames.AddRange(new string[] { "EudoriaCore", "UMG", "Slate", "SlateCore" });
	}
}
