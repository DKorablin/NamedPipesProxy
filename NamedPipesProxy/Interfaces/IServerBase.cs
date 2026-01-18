using System;
using System.Threading;
using System.Threading.Tasks;

namespace AlphaOmega.IO.Interfaces
{
	/// <summary>Base contract for a named pipe server.</summary>
	public interface IServerBase : IDisposable
	{
		/// <summary>Gets the unique name of the named pipe used for this worker's communication with clients.</summary>
		/// <value>The pipe name combining the worker prefix and the worker identifier.</value>
		String PipeName { get; }

		/// <summary>Indicates if the server is started.</summary>
		Boolean IsStarted { get; }

		/// <summary>Raised when a message is received from a client, allowing custom handling before method invocation.</summary>
		/// <remarks>Subscribers can intercept, validate, or transform incoming requests before they are processed.</remarks>
		event Func<PipeMessage, CancellationToken, Task<PipeMessage>> RequestReceived;

		/// <summary>Starts the server asynchronously.</summary>
		/// <param name="token">A cancellation token that can be used to cancel the startup operation.</param>
		/// <returns>A task representing the asynchronous startup operation.</returns>
		Task StartAsync(CancellationToken token);

		/// <summary>Stops the server asynchronously.</summary>
		/// <returns>A task representing the asynchronous stop operation.</returns>
		Task StopAsync();
	}
}