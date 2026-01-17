using System;
using System.IO.Pipes;
using System.Threading;

namespace AlphaOmega.IO
{
	/// <summary>Represents a server-side client connection over a named pipe.</summary>
	public sealed class ServerSideConnection : IDisposable
	{
		/// <summary>Gets the unique identifier for this connection.</summary>
		public Guid ConnectionId { get; }

		/// <summary>Gets the underlying pipe stream for this connection.</summary>
		public PipeStream Pipe { get; }

		/// <summary>Semaphore to synchronize read/write operations on the pipe.</summary>
		internal SemaphoreSlim ReadWriteLock { get; } = new SemaphoreSlim(1, 1);

		/// <summary>Initializes a new instance of the <see cref="ServerSideConnection"/> class with a new connection ID.</summary>
		/// <param name="pipe">The pipe stream associated with this connection.</param>
		public ServerSideConnection(PipeStream pipe)
			: this(Guid.NewGuid(), pipe)
		{
		}

		/// <summary>Initializes a new instance of the <see cref="ServerSideConnection"/> class with a specified connection ID.</summary>
		/// <param name="connectionId">The unique identifier for the connection.</param>
		/// <param name="pipe">The pipe stream associated with this connection.</param>
		public ServerSideConnection(Guid connectionId, PipeStream pipe)
		{
			this.ConnectionId = connectionId;
			this.Pipe = pipe;
		}

		/// <summary>Releases all resources used by the <see cref="ServerSideConnection"/>.</summary>
		public void Dispose()
		{
			GC.SuppressFinalize(this);

			this.Pipe?.Dispose();
			this.ReadWriteLock.Dispose();
		}
	}
}