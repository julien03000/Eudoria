#pragma once

// Test helpers shared by the automation tests of EudoriaCore (moved here from EudoriaSessionTests.cpp in 1.6 B).

#include "CoreMinimal.h"
#include "EudoriaHttpTransport.h"
#include "Misc/OutputDevice.h"
#include "Misc/OutputDeviceRedirector.h"
#include "Misc/ScopeLock.h"

namespace EudoriaTestSupport
{
	/** Keeps every request pending until Answer() is called: "simultaneous" requests become deterministic. */
	class FFakeTransport final : public IEudoriaHttpTransport
	{
	public:
		struct FPending
		{
			FEudoriaHttpRequest Request;
			FEudoriaHttpCallback Callback;
		};

		virtual void Send(const FEudoriaHttpRequest& Request, FEudoriaHttpCallback OnComplete) override
		{
			Sent.Add(Request);
			Pending.Add(FPending{Request, MoveTemp(OnComplete)});
		}

		int32 CountSent(const TCHAR* Path) const
		{
			return Sent.FilterByPredicate([Path](const FEudoriaHttpRequest& Request) { return Request.Path == Path; }).Num();
		}

		int32 CountPending(const TCHAR* Path) const
		{
			return Pending.FilterByPredicate([Path](const FPending& Item) { return Item.Request.Path == Path; }).Num();
		}

		/** Answers the oldest pending request to Path; returns the request answered. */
		FEudoriaHttpRequest Answer(const TCHAR* Path, const FEudoriaHttpResponse& Response)
		{
			const int32 Index = Pending.IndexOfByPredicate([Path](const FPending& Item) { return Item.Request.Path == Path; });
			check(Index != INDEX_NONE);
			FPending Item = MoveTemp(Pending[Index]);
			Pending.RemoveAt(Index);
			Item.Callback(Response);
			return Item.Request;
		}

		TArray<FEudoriaHttpRequest> Sent;
		TArray<FPending> Pending;
	};

	/** Collects every log line written while it exists. */
	class FLogCapture final : public FOutputDevice
	{
	public:
		FLogCapture() { GLog->AddOutputDevice(this); }
		virtual ~FLogCapture() override { GLog->RemoveOutputDevice(this); }

		virtual void Serialize(const TCHAR* Message, ELogVerbosity::Type, const FName& Category) override
		{
			FScopeLock Lock(&Mutex);
			Text += Category.ToString() + TEXT(": ") + Message + TEXT("\n");
		}

		FString Get()
		{
			GLog->Flush();
			FScopeLock Lock(&Mutex);
			return Text;
		}

	private:
		FCriticalSection Mutex;
		FString Text;
	};
}
