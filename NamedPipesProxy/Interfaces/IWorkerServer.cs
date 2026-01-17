using System;
using System.Threading.Tasks;

namespace AlphaOmega.IO.Interfaces
{
	/// <summary>Worker server contract for connecting to a registry and handling named pipe communication.</summary>
	public interface IWorkerServer : IServerBase
	{
		/// <summary>Gets the name of the registry server pipe that this worker is connected to.</summary>
		/// <value>The registry pipe name used for worker registration and communication.</value>
		String RegistryPipeName { get; }

		/// <summary>Gets the unique identifier of this worker instance.</summary>
		/// <value>A unique identifier assigned to this worker, typically a GUID or PID string.</value>
		String WorkerId { get; }

		/// <summary>Raised when the connection to the registry or a client is lost.</summary>
		/// <remarks>This event allows graceful cleanup or recovery actions when the named pipe connection is unexpectedly closed.</remarks>
		event Func<Task> ConnectionLost;
	}
}