using UnrealBuildTool;

/// <summary>Foundation module shared by the client and every world (ADR-0004). Must never depend on a world.</summary>
public class EudoriaCore : ModuleRules
{
	public EudoriaCore(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;

		PublicDependencyModuleNames.AddRange(new string[] { "Core", "CoreUObject", "Engine", "DeveloperSettings" });
		PrivateDependencyModuleNames.AddRange(new string[] { "HTTP", "Json" });

		// World entry ticket verification (ADR-0007, plan 1.6 B): only where server code is built (editor,
		// dedicated server; not a future "Client" target), on the desktop/server platforms, with the
		// engine's own OpenSSL (no new library). Only public keys are used: signatures are verified, never made.
		bool bVerifiesEntryTickets = Target.bWithServerCode
			&& (Target.Platform == UnrealTargetPlatform.Win64 || Target.Platform == UnrealTargetPlatform.Linux
				|| Target.Platform == UnrealTargetPlatform.LinuxArm64);
		if (bVerifiesEntryTickets)
		{
			AddEngineThirdPartyPrivateStaticDependencies(Target, "OpenSSL");
		}
		PublicDefinitions.Add("EUDORIA_WITH_TICKET_VERIFIER=" + (bVerifiesEntryTickets ? "1" : "0"));
	}
}
