using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace AlphaOmega.IO.Interfaces
{
	/// <summary>Represents a connection over a named pipe with send/receive capabilities.</summary>
	public interface IPipeConnection : IDisposable
	{
		/// <summary>Gets the unique identifier for this connection.</summary>
		Guid ConnectionId { get; }

		/// <summary>Gets the underlying pipe stream for this connection.</summary>
		PipeStream Pipe { get; }

		/// <summary>Gets a value indicating whether the pipe is connected.</summary>
		Boolean IsConnected { get; }

		/// <summary>Sends a message on the pipe.</summary>
		Task SendMessageAsync(PipeMessage message, CancellationToken token);

		/// <summary>Receives a message from the pipe.</summary>
		Task<PipeMessage> ReceiveMessageAsync(CancellationToken token);

		/// <summary>Reads and sends messages in a loop until disconnection or cancellation.</summary>
		Task ListenLoopAsync(Func<PipeMessage, CancellationToken, Task<PipeMessage>> handler, CancellationToken token);
	}
}