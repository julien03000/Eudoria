using UnrealBuildTool;

/// <summary>EUDORIA editor (development and automation tests).</summary>
public class EudoriaEditorTarget : TargetRules
{
	public EudoriaEditorTarget(TargetInfo Target) : base(Target)
	{
		Type = TargetType.Editor;
		DefaultBuildSettings = BuildSettingsVersion.V7;
		IncludeOrderVersion = EngineIncludeOrderVersion.Unreal5_8;
		ExtraModuleNames.AddRange(new string[] { "Eudoria", "EudoriaEditor" });
	}
}
