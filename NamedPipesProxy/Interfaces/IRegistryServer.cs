using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AlphaOmega.IO.DTOs;

namespace AlphaOmega.IO.Interfaces
{
	/// <summary>Registry server contract for managing worker connections and named pipe communication.</summary>
	public interface IRegistryServer : IServerBase
	{
		/// <summary>Response channel for asynchronous RPC responses.</summary>
		RpcResponseChannel ResponseChannel { get; }

		/// <summary>Currently connected worker IDs.</summary>
		IEnumerable<String> ConnectedWorkerIDs { get; }

		/// <summary>Raised when a worker connects.</summary>
		event Func<String, Task> WorkerConnected;

		/// <summary>Raised when a worker disconnects.</summary>
		event Func<String, Task> WorkerDisconnected;

		/// <summary>Sends a request to all workers.</summary>
		/// <param name="request">Request message.</param>
		/// <param name="token">Cancellation token.</param>
		Task SendRequestToWorkers(PipeMessage request, CancellationToken token);

		/// <summary>Sends a request to a specific worker and awaits response.</summary>
		/// <param name="workerId">Target worker ID.</param>
		/// <param name="request">Request message.</param>
		/// <param name="token">Cancellation token.</param>
		/// <returns>Response message from worker.</returns>
		Task<PipeMessage> SendRequestToWorker(String workerId, PipeMessage request, CancellationToken token);

		/// <summary>Creates a processing logic proxy for remote invocation.</summary>
		/// <typeparam name="T">Processing logic interface type.</typeparam>
		/// <returns>Proxy instance.</returns>
		T CreateProcessingLogic<T>() where T : class;

		/// <summary>Creates a processing logic proxy for a specific worker.</summary>
		/// <typeparam name="T">Processing logic interface type.</typeparam>
		/// <param name="workerId">Target worker ID.</param>
		/// <returns>Proxy instance for worker.</returns>
		T CreateProcessingLogic<T>(String workerId) where T : class;
	}
}