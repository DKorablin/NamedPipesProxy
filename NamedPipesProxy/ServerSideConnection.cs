using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using AlphaOmega.IO.Interfaces;

namespace AlphaOmega.IO
{
	/// <summary>Represents a server-side client connection over a named pipe.</summary>
	public sealed class ServerSideConnection : IPipeConnection
	{
		public class ServerSideConnectionFactory : IPipeConnectionFactory
		{
			/// <inheritdoc/>
			async Task<IPipeConnection> IPipeConnectionFactory.CreateServerAsync(String pipeName, CancellationToken token)
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

			/// <inheritdoc/>
			async Task<IPipeConnection> IPipeConnectionFactory.CreateClientAsync(String serverName, String pipeName, Int32 timeout, CancellationToken token)
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
		}

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
			this.Pipe = pipe ?? throw new ArgumentNullException(nameof(pipe));
		}

		/// <summary>Sends a message on the pipe.</summary>
		/// <param name="message">Message to send.</param>
		/// <param name="token">Cancellation token.</param>
		public async Task SendMessageAsync(PipeMessage message, CancellationToken token)
		{
			TraceLogic.TraceSource.TraceEvent(TraceEventType.Verbose, 0, "[{0}] Sending message: {1}", this.ConnectionId, message.ToString());

			await this.ReadWriteLock.WaitAsync(token);
			try
			{
				await message.ToStream(this.Pipe, token);
			} finally
			{
				this.ReadWriteLock.Release();
				TraceLogic.TraceSource.TraceEvent(TraceEventType.Verbose, 0, "[{0}] Message sent: {1}", this.ConnectionId, message.ToString());
			}
		}

		/// <summary>Receives a message from the pipe.</summary>
		/// <param name="token">Cancellation token.</param>
		/// <returns>The received message.</returns>
		public async Task<PipeMessage> ReceiveMessageAsync(CancellationToken token)
		{
			TraceLogic.TraceSource.TraceEvent(TraceEventType.Verbose, 0, "[{0}] Waiting for messages...", this.ConnectionId);
			await this.ReadWriteLock.WaitAsync(token);
			try
			{
				var result = await PipeMessage.FromStream(this.Pipe, token);
				TraceLogic.TraceSource.TraceEvent(TraceEventType.Verbose, 0, "[{0}] Message {1} received", this.ConnectionId, result?.ToString() ?? "null");
				return result;
			} finally
			{
				this.ReadWriteLock.Release();
			}
		}

		/// <summary>Reads and sends messages in a loop until disconnection or cancellation.</summary>
		/// <param name="connection">Server-side pipe connection.</param>
		/// <param name="handler">Message handler that produces a response.</param>
		/// <param name="token">Cancellation token.</param>
		public async Task ListenLoopAsync(Func<PipeMessage, CancellationToken, Task<PipeMessage>> handler, CancellationToken token)
		{
			while(!token.IsCancellationRequested && this.Pipe.IsConnected)
			{
				try
				{
					PipeMessage message = await PipeMessage.FromStream(this.Pipe, token);

					PipeMessage response = await handler.Invoke(message, token);
					if(response != null)
						await this.SendMessageAsync(response, token);
				} catch(EndOfStreamException)
				{
					TraceLogic.TraceSource.TraceEvent(TraceEventType.Stop, 1, "[{0}] Lost connection to named pipe instance", this.ConnectionId);
					break;
				}
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