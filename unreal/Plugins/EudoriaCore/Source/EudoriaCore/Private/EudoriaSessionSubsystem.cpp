#include "EudoriaSessionSubsystem.h"

#include "EudoriaCore.h"
#include "EudoriaCoreSettings.h"
#include "EudoriaHttpTransport.h"

#include UE_INLINE_GENERATED_CPP_BY_NAME(EudoriaSessionSubsystem)

void UEudoriaSessionSubsystem::Initialize(FSubsystemCollectionBase& Collection)
{
	Super::Initialize(Collection);

	const UEudoriaCoreSettings* Settings = GetDefault<UEudoriaCoreSettings>();
	bSecureConfiguration = EudoriaCoreApi::IsSecureBaseUrl(Settings->CoreBaseUrl);
	if (bSecureConfiguration)
	{
		UE_LOG(LogEudoriaCore, Log, TEXT("Core URL: %s"), *Settings->CoreBaseUrl);
	}
	else
	{
		UE_LOG(LogEudoriaCore, Error, TEXT("CoreBaseUrl '%s' is not an https:// URL: no request will be sent."), *Settings->CoreBaseUrl);
	}

	// The transport itself refuses a non-https URL: the check above only reports it early.
	Session = MakeShared<FEudoriaSession>(MakeShared<FEudoriaHttpModuleTransport>(Settings->CoreBaseUrl, Settings->RequestTimeoutSeconds));
}

void UEudoriaSessionSubsystem::Deinitialize()
{
	// Forget the tokens; the session stays valid on the core until it expires (plan 1.3).
	Session.Reset();
	Super::Deinitialize();
}
