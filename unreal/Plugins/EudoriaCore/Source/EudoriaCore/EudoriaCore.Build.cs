using UnrealBuildTool;

/// <summary>Foundation module shared by the client and every world (ADR-0004). Must never depend on a world.</summary>
public class EudoriaCore : ModuleRules
{
	public EudoriaCore(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;

		PublicDependencyModuleNames.AddRange(new string[] { "Core", "CoreUObject", "Engine" });
	}
}
