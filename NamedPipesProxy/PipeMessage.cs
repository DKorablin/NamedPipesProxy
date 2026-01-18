using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AlphaOmega.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AlphaOmega.IO
{
	/// <summary>Represents a message for inter-process communication via named pipes.</summary>
	public sealed class PipeMessage
	{
		/// <summary>JSON serializer settings for message serialization and deserialization.</summary>
		private static readonly JsonSerializerSettings JsonSettings =
			new JsonSerializerSettings()
			{
				ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
				NullValueHandling = NullValueHandling.Ignore,
				TypeNameHandling = TypeNameHandling.None
			};

		/// <summary>Global correlation id for entire request between all instances.</summary>
		public Guid RequestId { get; set; } = Guid.NewGuid();

		/// <summary>The message identifier for message identification on RegistryServer instance.</summary>
		public Guid MessageId { get; set; } = Guid.NewGuid();

		/// <summary>Gets or sets the message type or method name.</summary>
		public String Type { get; set; }

		/// <summary>Gets or sets the serialized payload of the message.</summary>
		public Byte[] Payload { get; set; }

		/// <summary>Initializes a new instance of the <see cref="PipeMessage"/> class with a method name and payload.</summary>
		/// <param name="methodName">The method name or message type.</param>
		/// <param name="payload">The payload object to serialize.</param>
		public PipeMessage(String methodName, Object payload)
		{
			this.Type = methodName;
			this.Payload = Serialize(payload);
		}

		/// <summary>Initializes a new instance of the <see cref="PipeMessage"/> class by copying request IDs and setting a new method name and payload.</summary>
		/// <param name="request">The original request message to copy IDs from.</param>
		/// <param name="methodName">The method name or message type.</param>
		/// <param name="payload">The payload object to serialize.</param>
		public PipeMessage(PipeMessage request, String methodName, Object payload)
			: this(methodName, payload)
		{
			this.RequestId = request.RequestId;
			this.MessageId = request.MessageId;
		}

		/// <summary>Initializes a new instance of the <see cref="PipeMessage"/> class by copying type, payload, and request ID from another message.</summary>
		/// <param name="request">The original request message to copy from.</param>
		public PipeMessage(PipeMessage request)
		{
			this.Type = request.Type;
			this.Payload = request.Payload;
			this.RequestId = request.RequestId;
		}

		/// <summary>Deserializes the payload to the specified type.</summary>
		/// <typeparam name="T">The type to deserialize to.</typeparam>
		/// <returns>The deserialized object.</returns>
		public T Deserialize<T>()
			=> PipeMessage.Deserialize<T>(this.Payload);

		/// <summary>Deserializes the payload to the specified type.</summary>
		/// <param name="targetType">The target type for deserialization.</param>
		/// <returns>The deserialized object.</returns>
		/// <exception cref="InvalidOperationException">Thrown if the payload is invalid.</exception>
		public Object Deserialize(Type targetType)
		{
			String json = Encoding.UTF8.GetString(this.Payload);
			Object result = JsonConvert.DeserializeObject(json, targetType, JsonSettings);
			return result
				?? throw new InvalidOperationException("Invalid payload");
		}

		/// <summary>Deserializes the payload to an array of objects with specified types.</summary>
		/// <param name="targetTypes">The array of target types for deserialization.</param>
		/// <returns>An array of deserialized objects.</returns>
		/// <exception cref="InvalidOperationException">Thrown if the parameter count does not match or payload is invalid.</exception>
		public Object[] Deserialize(Type[] targetTypes)
		{
			String json = Encoding.UTF8.GetString(this.Payload);
			JArray jArray = JArray.Parse(json);

			if(jArray.Count != targetTypes.Length)
				throw new InvalidOperationException($"Parameter count mismatch: Expected {targetTypes.Length}, got {jArray.Count}");

			Object[] results = new Object[targetTypes.Length];
			JsonSerializer serializer = JsonSerializer.Create(JsonSettings);

			for(Int32 loop = 0; loop < targetTypes.Length; loop++)
				results[loop] = jArray[loop].ToObject(targetTypes[loop], serializer);

			return results;
		}

		/// <summary>Initializes a new instance of the <see cref="PipeMessage"/> class for deserialization.</summary>
		[JsonConstructor]
		private PipeMessage()
		{
		}

		/// <summary>Serializes an object to a UTF-8 encoded JSON byte array.</summary>
		/// <param name="message">The object to serialize.</param>
		/// <returns>The serialized byte array.</returns>
		public static Byte[] Serialize(Object message)
			=> Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message, JsonSettings));

		/// <summary>Deserializes a UTF-8 encoded JSON byte array to the specified type.</summary>
		/// <typeparam name="T">The type to deserialize to.</typeparam>
		/// <param name="payload">The byte array payload.</param>
		/// <returns>The deserialized object.</returns>
		/// <exception cref="InvalidOperationException">Thrown if the payload is invalid.</exception>
		public static T Deserialize<T>(Byte[] payload)
		{
			String json = Encoding.UTF8.GetString(payload);
			T result = JsonConvert.DeserializeObject<T>(json, JsonSettings);
			return result == null
				? throw new InvalidOperationException("Invalid payload")
				: result;
		}

		/// <summary>Returns a string representation of the message.</summary>
		/// <returns>A string describing the message.</returns>
		public override String ToString()
			=> $"[{nameof(this.Type)}={this.Type}] {nameof(this.RequestId)}={this.RequestId}; {nameof(this.MessageId)}={this.MessageId}; {nameof(this.Payload)}:{Encoding.UTF8.GetString(this.Payload)}";

		public async Task ToStream(Stream stream, CancellationToken token)
		{
			TraceLogic.TraceSource.TraceInformation("Writing message: {0}", this.ToString());

			Byte[] data = PipeMessage.Serialize(this);
			Byte[] length = BitConverter.GetBytes(data.Length);

			await stream.WriteAsync(length, 0, length.Length, token);
			await stream.WriteAsync(data, 0, data.Length, token);
			await stream.FlushAsync(token);
		}

		/// <summary>Reads a length-prefixed pipe message from the stream and deserializes it.</summary>
		/// <param name="stream">Source stream for reading.</param>
		/// <param name="token">Cancellation token.</param>
		/// <returns>Deserialized <see cref="PipeMessage"/> instance.</returns>
		/// <exception cref="InvalidDataException">Thrown if the message length is invalid.</exception>
		/// <exception cref="EndOfStreamException">Thrown if the stream ends unexpectedly.</exception>
		public static async Task<PipeMessage> FromStream(Stream stream, CancellationToken token)
		{
			Byte[] lengthBuffer = new Byte[4];
			await ReadExactlyAsync(lengthBuffer);

			Int32 length = BitConverter.ToInt32(lengthBuffer, 0);
			if(length <= 0)
				throw new InvalidDataException("Invalid message length");

			Byte[] payload = new Byte[length];
			await ReadExactlyAsync(payload);

			PipeMessage result = PipeMessage.Deserialize<PipeMessage>(payload);
			Console.WriteLine($"Received message: {result.ToString()}");
			return result;

			async Task ReadExactlyAsync(Byte[] buffer)
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
}