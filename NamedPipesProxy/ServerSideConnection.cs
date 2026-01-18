using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace AlphaOmega.IO
{
	/// <summary>Represents a server-side client connection over a named pipe.</summary>
	public sealed class ServerSideConnection : IDisposable
	{
		/// <summary>Gets the unique identifier for this connection.</summary>
		public Guid ConnectionId { get; }

		/// <summary>Gets the underlying pipe stream for this connection.</summary>
		public PipeStream Pipe { get; }

		/// <summary>Gets a value indicating whether the pipe is connected.</summary>
		public Boolean IsConnected => this.Pipe?.IsConnected ?? false;

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

		/// <summary>Creates a server-side connection that waits for a client to connect.</summary>
		/// <param name="pipeName">The name of the pipe to create.</param>
		/// <param name="token">Cancellation token.</param>
		/// <returns>A new <see cref="ServerSideConnection"/> with an established connection.</returns>
		public static async Task<ServerSideConnection> CreateServerAsync(String pipeName, CancellationToken token)
		{
			NamedPipeServerStream pipe = new NamedPipeServerStream(
				pipeName,
				PipeDirection.InOut,
				NamedPipeServerStream.MaxAllowedServerInstances,
				PipeTransmissionMode.Byte,
				PipeOptions.Asynchronous);

			try
			{
				await pipe.WaitForConnectionAsync(token);
				return new ServerSideConnection(pipe);
			} catch
			{
				pipe?.Dispose();
				throw;
			}
		}

		/// <summary>Creates a client-side connection to a registry server.</summary>
		/// <param name="serverName">The server name (e.g., "." for local).</param>
		/// <param name="pipeName">The name of the pipe to connect to.</param>
		/// <param name="timeout">Connection timeout in milliseconds.</param>
		/// <param name="token">Cancellation token.</param>
		/// <returns>A new <see cref="ServerSideConnection"/> with an established connection.</returns>
		public static async Task<ServerSideConnection> CreateClientAsync(String serverName, String pipeName, Int32 timeout, CancellationToken token)
		{
			NamedPipeClientStream pipe = new NamedPipeClientStream(
				serverName,
				pipeName,
				PipeDirection.InOut,
				PipeOptions.Asynchronous);

			try
			{
				await pipe.ConnectAsync(timeout, token);
				return new ServerSideConnection(pipe);
			} catch
			{
				pipe?.Dispose();
				throw;
			}
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