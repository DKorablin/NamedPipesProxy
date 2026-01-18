using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AlphaOmega.IO.DTOs;

namespace AlphaOmega.IO
{
	/// <summary>Provides helper methods to read and write pipe messages over a stream.</summary>
	internal static class PipeProtocol
	{
		/// <summary>Writes a length-prefixed pipe message to the stream and flushes it.</summary>
		/// <param name="stream">Target stream for writing.</param>
		/// <param name="message">Message to serialize and write.</param>
		/// <param name="token">Cancellation token.</param>
		public static async Task WriteMessageAsync(Stream stream, PipeMessage message, CancellationToken token)
		{
			TraceLogic.TraceSource.TraceInformation("Writing message: {0}", message.ToString());

			Byte[] data = PipeMessage.Serialize(message);
			Byte[] length = BitConverter.GetBytes(data.Length);

			await stream.WriteAsync(length,0, length.Length, token);
			await stream.WriteAsync(data, 0, data.Length, token);
			await stream.FlushAsync(token);
		}

		/// <summary>Reads a length-prefixed pipe message from the stream and deserializes it.</summary>
		/// <param name="stream">Source stream for reading.</param>
		/// <param name="token">Cancellation token.</param>
		/// <returns>Deserialized <see cref="PipeMessage"/> instance.</returns>
		/// <exception cref="InvalidDataException">Thrown if the message length is invalid.</exception>
		/// <exception cref="EndOfStreamException">Thrown if the stream ends unexpectedly.</exception>
		public static async Task<PipeMessage> ReadMessageAsync(Stream stream, CancellationToken token)
		{
			Byte[] lengthBuffer = new Byte[4];
			await ReadExactlyAsync(stream, lengthBuffer, token);

			Int32 length = BitConverter.ToInt32(lengthBuffer, 0);
			if(length <= 0)
				throw new InvalidDataException("Invalid message length");

			Byte[] payload = new Byte[length];
			await ReadExactlyAsync(stream, payload, token);

			PipeMessage result = PipeMessage.Deserialize<PipeMessage>(payload);
			Console.WriteLine($"Received message: {result.ToString()}");
			return result;
		}

		/// <summary>Reads exactly the specified number of bytes into the buffer or throws on premature end.</summary>
		/// <param name="stream">Source stream for reading.</param>
		/// <param name="buffer">Destination buffer to fill.</param>
		/// <param name="token">Cancellation token.</param>
		/// <exception cref="EndOfStreamException">Thrown if the stream ends unexpectedly.</exception>
		private static async Task ReadExactlyAsync(Stream stream, Byte[] buffer, CancellationToken token)
		{
			Int32 offset = 0;
			while(offset < buffer.Length)
			{
				Int32 read = await stream.ReadAsync(buffer, offset, buffer.Length - offset, token);
				if(read == 0)
					throw new EndOfStreamException("Unexpected end of stream");
				offset += read;
			}
		}
	}
}