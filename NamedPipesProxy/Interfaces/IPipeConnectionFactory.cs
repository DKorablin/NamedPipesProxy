using System;
using System.Threading;
using System.Threading.Tasks;

namespace AlphaOmega.IO.Interfaces
{
	public interface IPipeConnectionFactory
	{
		/// <summary>Creates a client-side connection to a registry server.</summary>
		/// <param name="serverName">The server name (e.g., "." for local).</param>
		/// <param name="pipeName">The name of the pipe to connect to.</param>
		/// <param name="timeout">Connection timeout in milliseconds.</param>
		/// <param name="token">Cancellation token.</param>
		/// <returns>A new <see cref="IPipeConnection"/> with an established connection.</returns>
		Task<IPipeConnection> CreateClientAsync(String serverName, String pipeName, Int32 timeout, CancellationToken token);

		/// <summary>Creates a server-side connection that waits for a client to connect.</summary>
		/// <param name="pipeName">The name of the pipe to create.</param>
		/// <param name="token">Cancellation token.</param>
		/// <returns>A new <see cref="IPipeConnection"/> with an established connection.</returns>
		Task<IPipeConnection> CreateServerAsync(String pipeName, CancellationToken token);
	}
}