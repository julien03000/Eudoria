#include "EudoriaHttpTransport.h"

#include "EudoriaCore.h"
#include "HttpModule.h"
#include "Interfaces/IHttpRequest.h"
#include "Interfaces/IHttpResponse.h"

FEudoriaHttpModuleTransport::FEudoriaHttpModuleTransport(FString InBaseUrl, float InTimeoutSeconds)
	: BaseUrl(MoveTemp(InBaseUrl))
	, TimeoutSeconds(InTimeoutSeconds)
{
	BaseUrl.RemoveFromEnd(TEXT("/"));
}

void FEudoriaHttpModuleTransport::Send(const FEudoriaHttpRequest& Request, FEudoriaHttpCallback OnComplete)
{
	if (!EudoriaCoreApi::IsSecureBaseUrl(BaseUrl))
	{
		UE_LOG(LogEudoriaCore, Error, TEXT("Request %s %s not sent: the core URL must use https:// (CoreBaseUrl in DefaultGame.ini)."),
			*Request.Verb, *Request.Path);
		FEudoriaHttpResponse Refused;
		Refused.TransportStatus = EEudoriaTransportStatus::RefusedInsecureUrl;
		OnComplete(Refused);
		return;
	}

	const TSharedRef<IHttpRequest, ESPMode::ThreadSafe> HttpRequest = FHttpModule::Get().CreateRequest();
	HttpRequest->SetURL(BaseUrl + Request.Path);
	HttpRequest->SetVerb(Request.Verb);
	HttpRequest->SetHeader(TEXT("Accept"), TEXT("application/json"));
	HttpRequest->SetTimeout(TimeoutSeconds);
	HttpRequest->SetDelegateThreadPolicy(EHttpRequestDelegateThreadPolicy::CompleteOnGameThread);
	if (!Request.JsonBody.IsEmpty())
	{
		HttpRequest->SetHeader(TEXT("Content-Type"), TEXT("application/json"));
		HttpRequest->SetContentAsString(Request.JsonBody);
	}
	if (!Request.BearerToken.IsEmpty())
	{
		HttpRequest->SetHeader(TEXT("Authorization"), TEXT("Bearer ") + Request.BearerToken);
	}

	// Only the verb, the path and the status are logged: never a body, a header or a token.
	HttpRequest->OnProcessRequestComplete().BindLambda(
		[Verb = Request.Verb, Path = Request.Path, OnComplete = MoveTemp(OnComplete)](
			FHttpRequestPtr, FHttpResponsePtr HttpResponse, bool bConnectedSuccessfully)
		{
			FEudoriaHttpResponse Response;
			if (bConnectedSuccessfully && HttpResponse.IsValid())
			{
				Response.TransportStatus = EEudoriaTransportStatus::Received;
				Response.StatusCode = HttpResponse->GetResponseCode();
				Response.Body = HttpResponse->GetContentAsString();
				Response.RetryAfter = HttpResponse->GetHeader(TEXT("Retry-After"));
				UE_LOG(LogEudoriaCore, Log, TEXT("%s %s -> %d"), *Verb, *Path, Response.StatusCode);
			}
			else
			{
				UE_LOG(LogEudoriaCore, Warning, TEXT("%s %s -> no response (network, certificate or timeout)"), *Verb, *Path);
			}
			OnComplete(Response);
		});

	if (!HttpRequest->ProcessRequest())
	{
		UE_LOG(LogEudoriaCore, Warning, TEXT("%s %s -> could not be started"), *Request.Verb, *Request.Path);
	}
}
