using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AlphaOmega.IO.DTOs;

namespace AlphaOmega.IO
{
	/// <summary>Base class for pipe-based servers that encapsulates common read, handle, and respond logic.</summary>
	public abstract class PipeServerBase
	{
		/// <summary>Sends a message on the pipe.</summary>
		/// <param name="connection">Server-side pipe connection.</param>
		/// <param name="message">Message to send.</param>
		/// <param name="token">Cancellation token.</param>
		protected async Task SendMessageAsync(ServerSideConnection connection, PipeMessage message, CancellationToken token)
		{
			await connection.ReadWriteLock.WaitAsync(token);
			try
			{
				await message.ToStream(connection.Pipe, token);
			} finally
			{
				connection.ReadWriteLock.Release();
			}
		}

		/// <summary>Reads and sends messages in a loop until disconnection or cancellation.</summary>
		/// <param name="connection">Server-side pipe connection.</param>
		/// <param name="handler">Message handler that produces a response.</param>
		/// <param name="token">Cancellation token.</param>
		protected async Task ListenLoopAsync(ServerSideConnection connection, Func<PipeMessage, CancellationToken, Task<PipeMessage>> handler, CancellationToken token)
		{
			while(!token.IsCancellationRequested && connection.Pipe.IsConnected)
			{
				try
				{
					PipeMessage message = await PipeMessage.FromStream(connection.Pipe, token);

					PipeMessage response = await handler.Invoke(message, token);
					if(response != null)
						await this.SendMessageAsync(connection, response, token);
				} catch(EndOfStreamException)
				{
					TraceLogic.TraceSource.TraceEvent(TraceEventType.Stop, 1, "Lost connection to named pipe instance");
					break;
				} catch(IOException ex)
				{
					TraceLogic.TraceSource.TraceEvent(TraceEventType.Error, 9, "Pipe communication error: {0}", ex.Message);
					throw;
				}
			}
		}
	}
}