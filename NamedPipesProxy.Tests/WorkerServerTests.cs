using System;
using System.IO.Pipes;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AlphaOmega.IO;
using AlphaOmega.IO.DTOs;
using AlphaOmega.IO.Interfaces;
using Moq;
using NUnit.Framework;

namespace NamedPipesProxy.Tests
{
	/// <summary>Unit tests for the WorkerServer class.</summary>
	[TestFixture]
	public class WorkerServerTests
	{
		private Mock<IPipeConnectionFactory> _connectionFactory;
		private Mock<IPipeConnection> _connection;

		[SetUp]
		public void Setup()
		{
			this._connectionFactory = null;
			this._connection = null;
		}

		[Test]
		public void Constructor_NullWorkerLogic_ThrowsArgumentNullException()
		{
			Assert.Throws<ArgumentNullException>(() => new WorkerServer(null));
		}

		[Test]
		public void Constructor_WithDefaultValues_AssignsIdentifiers()
		{
			TestWorkerLogic workerLogic = new TestWorkerLogic();

			WorkerServer server = new WorkerServer(workerLogic);

			Assert.AreEqual(RegistryServer.RegistryPipeName, server.RegistryPipeName);
			Assert.IsFalse(String.IsNullOrWhiteSpace(server.WorkerId));
			Assert.IsTrue(server.PipeName.StartsWith("AlphaOmega.NamedPipes.Worker.", StringComparison.Ordinal));
		}

		[Test]
		public async Task TryHandleAsync_WithoutHandler_ReturnsNull()
		{
			this.SetupDummyFactory();

			WorkerServer server = new WorkerServer("RegistryPipe", "Worker.", "worker1", new TestWorkerLogic(), this._connectionFactory.Object);
			PipeMessage request = new PipeMessage("Echo", "payload");

			PipeMessage response = await server.TryHandleAsync(request, CancellationToken.None);

			Assert.IsNull(response);
		}

		[Test]
		public async Task TryHandleAsync_WithHandler_ReturnsHandlerResult()
		{
			this.SetupDummyFactory();

			WorkerServer server = new WorkerServer("RegistryPipe", "Worker.", "worker1", new TestWorkerLogic(), this._connectionFactory.Object);
			PipeMessage request = new PipeMessage("Echo", "payload");
			PipeMessage expected = new PipeMessage(request, "Handled", "ok");

			server.RequestReceived += async (message, token) =>
			{
				Assert.AreSame(request, message);
				return expected;
			};

			PipeMessage response = await server.TryHandleAsync(request, CancellationToken.None);

			Assert.AreSame(expected, response);
		}

		[Test]
		public async Task StartAsync_RegistersWorkerAndStartsListening()
		{
			String registryPipeName = "RegistryPipe";
			this.SetupConnectionMocks(registryPipeName);

			WorkerServer server = new WorkerServer(registryPipeName, "Worker.", "worker1", new TestWorkerLogic(), this._connectionFactory.Object);

			await server.StartAsync(CancellationToken.None);

			Assert.IsTrue(server.IsStarted);
			this._connectionFactory.Verify(f => f.CreateClientAsync(".", registryPipeName, 5000, It.IsAny<CancellationToken>()), Times.Once);
			this._connection.Verify(c => c.SendMessageAsync(It.Is<PipeMessage>(m => m.Type == PipeMessageType.RegisterWorker.ToString()), It.IsAny<CancellationToken>()), Times.Once);

			await server.StopAsync();
		}

		[Test]
		public async Task InvokeMethodAsync_WithTaskOfResult_ReturnsResponseMessage()
		{
			this.SetupDummyFactory();

			TestWorkerLogic workerLogic = new TestWorkerLogic();
			WorkerServer server = new WorkerServer("RegistryPipe", "Worker.", "worker1", workerLogic, this._connectionFactory.Object);
			PipeMessage request = new PipeMessage("AddNumbers", new Object[] { 2, 3 });

			PipeMessage response = await this.InvokePrivateMethodAsync(server, "InvokeMethodAsync", request);

			Assert.IsNotNull(response);
			Assert.AreEqual("AddNumbers", response.Type);
			Int32 result = response.Deserialize<Int32>();
			Assert.AreEqual(5, result);
		}

		[Test]
		public async Task InvokeMethodAsync_WithTaskWithoutResult_ReturnsNull()
		{
			this.SetupDummyFactory();

			TestWorkerLogic workerLogic = new TestWorkerLogic();
			WorkerServer server = new WorkerServer("RegistryPipe", "Worker.", "worker1", workerLogic, this._connectionFactory.Object);
			PipeMessage request = new PipeMessage("DoWorkAsync", "payload");

			PipeMessage response = await this.InvokePrivateMethodAsync(server, "InvokeMethodAsync", request);

			Assert.IsNull(response);
			Assert.IsTrue(workerLogic.WorkInvoked);
		}

		[Test]
		public async Task HandleMessageAsync_WithMissingMethod_ReturnsErrorResponse()
		{
			this.SetupDummyFactory();

			WorkerServer server = new WorkerServer("RegistryPipe", "Worker.", "worker1", new TestWorkerLogic(), this._connectionFactory.Object);
			PipeMessage request = new PipeMessage("MissingMethod", "payload");

			PipeMessage response = await this.InvokePrivateMethodAsync(server, "HandleMessageAsync", request);

			Assert.IsNotNull(response);
			Assert.AreEqual(PipeMessageType.Error.ToString(), response.Type);
			ErrorResponse error = response.Deserialize<ErrorResponse>();
			StringAssert.Contains("MissingMethod", error.Message);
		}

		private async Task<PipeMessage> InvokePrivateMethodAsync(WorkerServer server, String methodName, PipeMessage message)
		{
			MethodInfo method = typeof(WorkerServer).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
			return await (Task<PipeMessage>)method.Invoke(server, new Object[] { message, CancellationToken.None });
		}

		private void SetupDummyFactory()
		{
			this._connectionFactory = new Mock<IPipeConnectionFactory>(MockBehavior.Loose);
			this._connection = new Mock<IPipeConnection>(MockBehavior.Loose);
		}

		private void SetupConnectionMocks(String registryPipeName)
		{
			this._connectionFactory = new Mock<IPipeConnectionFactory>(MockBehavior.Strict);
			this._connection = new Mock<IPipeConnection>(MockBehavior.Strict);

			this._connectionFactory.Setup(f => f.CreateClientAsync(".", registryPipeName, 5000, It.IsAny<CancellationToken>()))
				.ReturnsAsync(this._connection.Object);

			this._connection.SetupGet(c => c.ConnectionId).Returns(Guid.NewGuid());
			this._connection.SetupGet(c => c.Pipe).Returns((PipeStream)null);
			this._connection.SetupGet(c => c.IsConnected).Returns(true);
			this._connection.Setup(c => c.SendMessageAsync(It.Is<PipeMessage>(m => m.Type == PipeMessageType.RegisterWorker.ToString()), It.IsAny<CancellationToken>()))
				.Returns(Task.CompletedTask);
			this._connection.Setup(c => c.ListenLoopAsync(It.IsAny<Func<PipeMessage, CancellationToken, Task<PipeMessage>>>(), It.IsAny<CancellationToken>()))
				.Returns((Func<PipeMessage, CancellationToken, Task<PipeMessage>> handler, CancellationToken token) => Task.Delay(Timeout.Infinite, token));
			this._connection.Setup(c => c.Dispose());
		}

		private sealed class TestWorkerLogic
		{
			public Boolean WorkInvoked { get; private set; }

			public String Echo(String input)
				=> input;

			public Task<Int32> AddNumbers(Int32 first, Int32 second)
				=> Task.FromResult(first + second);

			public async Task DoWorkAsync(String payload)
			{
				this.WorkInvoked = true;
				await Task.CompletedTask;
			}
		}
	}
}
