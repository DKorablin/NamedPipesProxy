using System;

namespace AlphaOmega.IO
{
	/// <summary>Represents a server-side worker registered with the registry.</summary>
	public sealed class ServerSideWorker
	{
		/// <summary>Gets the unique identifier for this worker.</summary>
		public String WorkerId { get; }

		/// <summary>Gets the named pipe name used by this worker for direct communication.</summary>
		public String WorkerPipeName { get; }

		/// <summary>Gets the connection ID of the registry connection through which this worker is registered.</summary>
		public Guid ConnectionId { get; }

		/// <summary>Initializes a new instance of the <see cref="ServerSideWorker"/> class.</summary>
		/// <param name="workerId">The unique identifier for the worker.</param>
		/// <param name="workerPipeName">The named pipe name used by the worker.</param>
		/// <param name="connectionId">The connection ID of the registry connection.</param>
		public ServerSideWorker(String workerId, String workerPipeName, Guid connectionId)
		{
			this.WorkerId = workerId ?? throw new ArgumentNullException(nameof(workerId));
			this.WorkerPipeName = workerPipeName ?? throw new ArgumentNullException(nameof(workerPipeName));
			this.ConnectionId = connectionId;
		}
	}
}