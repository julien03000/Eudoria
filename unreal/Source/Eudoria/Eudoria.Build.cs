using UnrealBuildTool;

/// <summary>Main module: the client front-end (game mode, screens) and the render test scene (sub-step 1.7).
/// Depends only on the EudoriaCore plugin and engine modules.</summary>
public class Eudoria : ModuleRules
{
	public Eudoria(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;

		PublicDependencyModuleNames.AddRange(new string[] { "Core", "CoreUObject", "Engine", "DeveloperSettings", "InputCore" });
		PrivateDependencyModuleNames.AddRange(new string[] { "EudoriaCore", "UMG", "Slate", "SlateCore", "EnhancedInput" });
	}
}
