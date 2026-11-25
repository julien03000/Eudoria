using UnrealBuildTool;

/// <summary>EUDORIA game client (PC first).</summary>
public class EudoriaTarget : TargetRules
{
	public EudoriaTarget(TargetInfo Target) : base(Target)
	{
		Type = TargetType.Game;
		DefaultBuildSettings = BuildSettingsVersion.V7;
		IncludeOrderVersion = EngineIncludeOrderVersion.Unreal5_8;
		ExtraModuleNames.Add("Eudoria");
	}
}
