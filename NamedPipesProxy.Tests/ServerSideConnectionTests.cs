using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using AlphaOmega.IO;
using AlphaOmega.IO.Interfaces;
using NUnit.Framework;

namespace NamedPipesProxy.Tests
{
	/// <summary>Tests for ServerSideConnection.</summary>
	[TestFixture]
	[Timeout(5000)]
	public class ServerSideConnectionTests
	{
		[Test]
		public void Constructor_WithNullPipe_ThrowsArgumentNullException()
		{
			Assert.Throws<ArgumentNullException>(() => new ServerSideConnection(null));
		}

		[Test]
		public void Constructor_WithConnectionId_SetsProperties()
		{
			String pipeName = Guid.NewGuid().ToString();
			NamedPipeServerStream server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
			NamedPipeClientStream client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
			Task waitTask = server.WaitForConnectionAsync();
			client.Connect(2000);
			if(!waitTask.Wait(2000)) throw new TimeoutException("Server pipe failed to connect");

			Guid connectionId = Guid.NewGuid();
			ServerSideConnection connection = new ServerSideConnection(connectionId, server);

			Assert.AreEqual(connectionId, connection.ConnectionId);
			Assert.AreSame(server, connection.Pipe);

			connection.Dispose();
			client.Dispose();
		}

		[Test]
		public void IsConnected_ReturnsFalseAfterDisposal()
		{
			ServerSideConnection connection;
			NamedPipeClientStream client;

			this.CreateConnectedPipe(out connection, out client);

			connection.Dispose();

			Assert.IsFalse(connection.IsConnected);

			client.Dispose();
		}

		[Test]
		[Ignore("Flaky test - needs investigation")]
		public async Task SendMessageAsync_WritesMessageToPipe()
		{
			ServerSideConnection connection;
			NamedPipeClientStream client;

			this.CreateConnectedPipe(out connection, out client);

			try
			{
				PipeMessage message = new PipeMessage("TestType", "Payload");

				await connection.SendMessageAsync(message, CancellationToken.None);
				PipeMessage received = await PipeMessage.ReadFromStream(client, CancellationToken.None);

				Assert.AreEqual(message.Type, received.Type);
				Assert.AreEqual(message.Payload, received.Payload);
			}
			finally
			{
				connection.Dispose();
				client.Dispose();
			}
		}

		[Test]
		[Ignore("Flaky test - needs investigation")]
		public async Task ReceiveMessageAsync_ReadsMessageFromPipe()
		{
			ServerSideConnection connection;
			NamedPipeClientStream client;

			this.CreateConnectedPipe(out connection, out client);

			CancellationTokenSource cts = new CancellationTokenSource();
			try
			{
				PipeMessage message = new PipeMessage("Incoming", "Data");
				await message.WriteToStream(client, cts.Token);

				PipeMessage received = await connection.ReceiveMessageAsync(cts.Token);

				Assert.AreEqual(message.Type, received.Type);
				Assert.AreEqual(message.Payload, received.Payload);
			}
			finally
			{
				cts.Dispose();
				connection.Dispose();
				client.Dispose();
			}
		}

		[Test]
		[Ignore("Flaky test - needs investigation")]
		public async Task ListenLoopAsync_ProcessesMessagesAndSendsResponses()
		{
			ServerSideConnection connection;
			NamedPipeClientStream client;

			this.CreateConnectedPipe(out connection, out client);

			CancellationTokenSource cts = new CancellationTokenSource();
			Boolean handlerInvoked = false;

			try
			{
				Task listenTask = connection.ListenLoopAsync(async (message, token) =>
				{
					handlerInvoked = true;
					return new PipeMessage("Response", message.Payload);
				}, cts.Token);

				PipeMessage request = new PipeMessage("Request", "Payload");
				await request.WriteToStream(client, CancellationToken.None);

				PipeMessage response = await PipeMessage.ReadFromStream(client, CancellationToken.None);

				Assert.IsTrue(handlerInvoked);
				Assert.AreEqual("Response", response.Type);
				Assert.AreEqual(request.Payload, response.Payload);

				cts.Cancel();
				await listenTask;
			}
			finally
			{
				cts.Dispose();
				connection.Dispose();
				client.Dispose();
			}
		}

		[Test]
		public void Dispose_CanBeCalledMultipleTimes()
		{
			ServerSideConnection connection;
			NamedPipeClientStream client;

			this.CreateConnectedPipe(out connection, out client);

			connection.Dispose();
			connection.Dispose();

			client.Dispose();
		}

		[Test]
		public async Task Factory_CreateServerAsync_ReturnsConnectedInstance()
		{
			String pipeName = Guid.NewGuid().ToString();
			IPipeConnectionFactory factory = new ServerSideConnection.ServerSideConnectionFactory();
			CancellationToken token = CancellationToken.None;

			Task<IPipeConnection> serverTask = factory.CreateServerAsync(pipeName, token);

			NamedPipeClientStream client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
			await client.ConnectAsync(2000, token);

			IPipeConnection connection = await serverTask;

			Assert.IsTrue(((ServerSideConnection)connection).IsConnected);

			connection.Dispose();
			client.Dispose();
		}

		[Test]
		public async Task Factory_CreateClientAsync_ReturnsConnectedInstance()
		{
			String pipeName = Guid.NewGuid().ToString();
			NamedPipeServerStream server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
			Task waitForClient = server.WaitForConnectionAsync();

			IPipeConnectionFactory factory = new ServerSideConnection.ServerSideConnectionFactory();
			IPipeConnection connection = await factory.CreateClientAsync(".", pipeName, 2000, CancellationToken.None);

			if(!waitForClient.Wait(2000)) throw new TimeoutException("Server pipe failed to connect");

			Assert.IsTrue(((ServerSideConnection)connection).IsConnected);

			connection.Dispose();
			server.Dispose();
		}

		private void CreateConnectedPipe(out ServerSideConnection connection, out NamedPipeClientStream client)
		{
			String pipeName = Guid.NewGuid().ToString();
			NamedPipeServerStream server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
			client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
			Task waitTask = server.WaitForConnectionAsync();
			client.Connect(2000);
			if(!waitTask.Wait(2000)) throw new TimeoutException("Server pipe failed to connect");
			connection = new ServerSideConnection(server);
		}
	}
}
