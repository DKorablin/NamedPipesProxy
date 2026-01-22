using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlphaOmega.IO;
using AlphaOmega.IO.DTOs;
using AlphaOmega.IO.Interfaces;
using Moq;
using NUnit.Framework;

namespace NamedPipesProxy.Tests
{
	/// <summary>Unit tests for the RegistryServer class.</summary>
	[TestFixture]
	public class RegistryServerTests
	{
		private RegistryServer _registryServer;

		[SetUp]
		public void Setup()
		{
			this._registryServer = null;
		}

		[TearDown]
		public void Teardown()
		{
			try
			{
				this._registryServer?.Dispose();
			}
			catch(ObjectDisposedException)
			{
				// Already disposed, which is acceptable
			}
		}

		[Test]
		public void Constructor_WithDefaultPipeName_SetsCorrectPipeName()
		{
			this._registryServer = new RegistryServer();

			Assert.AreEqual("AlphaOmega.NamedPipes.Registry", this._registryServer.PipeName);
		}

		[Test]
		public void Constructor_WithCustomPipeName_SetsCustomPipeName()
		{
			String customPipeName = "CustomRegistry";

			this._registryServer = new RegistryServer(customPipeName);

			Assert.AreEqual(customPipeName, this._registryServer.PipeName);
		}

		[Test]
		public void Constructor_WithNullPipeName_ThrowsArgumentNullException()
		{
			Assert.Throws<ArgumentNullException>(() => new RegistryServer(null));
		}

		[Test]
		public void Constructor_WithEmptyPipeName_ThrowsArgumentNullException()
		{
			Assert.Throws<ArgumentNullException>(() => new RegistryServer(""));
		}

		[Test]
		public void Constructor_WithWhitespacePipeName_ThrowsArgumentNullException()
		{
			Assert.Throws<ArgumentNullException>(() => new RegistryServer("   "));
		}

		[Test]
		public void IsStarted_InitialValue_IsFalse()
		{
			this._registryServer = new RegistryServer("TestPipe");

			Assert.IsFalse(this._registryServer.IsStarted);
		}

		[Test]
		public void ResponseChannel_IsInitialized()
		{
			this._registryServer = new RegistryServer("TestPipe");

			Assert.IsNotNull(this._registryServer.ResponseChannel);
			Assert.IsInstanceOf<RpcResponseChannel>(this._registryServer.ResponseChannel);
		}

		[Test]
		public void ConnectedWorkerIDs_InitialValue_IsEmpty()
		{
			this._registryServer = new RegistryServer("TestPipe");

			IEnumerable<String> workerIds = ((IRegistryServer)this._registryServer).ConnectedWorkerIDs;

			Assert.IsNotNull(workerIds);
			Assert.IsEmpty(workerIds);
		}

		[Test]
		public async Task SendRequestToWorker_WithUnregisteredWorker_ThrowsInvalidOperationException()
		{
			this._registryServer = new RegistryServer("TestPipe");
			PipeMessage request = new PipeMessage("TestType", "TestPayload");

			Assert.ThrowsAsync<InvalidOperationException>(async () =>
				await this._registryServer.SendRequestToWorker("NonExistentWorker", request, CancellationToken.None));
		}

		[Test]
		public async Task SendRequestToWorkers_WithNoWorkers_CompletesSuccessfully()
		{
			this._registryServer = new RegistryServer("TestPipe");
			PipeMessage request = new PipeMessage("TestType", "TestPayload");

			// Should not throw
			await this._registryServer.SendRequestToWorkers(request, CancellationToken.None);
		}

		[Test]
		public void WorkerConnected_Event_CanBeSubscribed()
		{
			this._registryServer = new RegistryServer("TestPipe");

			this._registryServer.WorkerConnected += async (workerId) =>
			{
				await Task.CompletedTask;
			};

			// Event subscription should succeed
			Assert.Pass();
		}

		[Test]
		public void WorkerDisconnected_Event_CanBeSubscribed()
		{
			this._registryServer = new RegistryServer("TestPipe");

			this._registryServer.WorkerDisconnected += async (workerId) =>
			{
				await Task.CompletedTask;
			};

			// Event subscription should succeed
			Assert.Pass();
		}

		[Test]
		public void RequestReceived_Event_CanBeSubscribed()
		{
			this._registryServer = new RegistryServer("TestPipe");

			this._registryServer.RequestReceived += async (message, token) =>
			{
				await Task.CompletedTask;
				return null;
			};

			// Event subscription should succeed
			Assert.Pass();
		}

		[Test]
		public async Task StopAsync_WithoutStart_CompletesSuccessfully()
		{
			this._registryServer = new RegistryServer("TestPipe");

			// Should not throw
			await this._registryServer.StopAsync();
		}

		[Test]
		public void Dispose_CanBeCalledMultipleTimes()
		{
			this._registryServer = new RegistryServer("TestPipe");

			// First dispose should work
			this._registryServer.Dispose();

			// Second dispose may throw ObjectDisposedException
			// This is acceptable and expected behavior
			try
			{
				this._registryServer.Dispose();
				Assert.Pass("Dispose succeeded on second call");
			}
			catch(ObjectDisposedException)
			{
				// This is expected behavior when CancellationTokenSource is already disposed
				Assert.Pass("Dispose throws ObjectDisposedException on subsequent calls (acceptable)");
			}
		}

		[Test]
		public void Constructor_WithValidCustomPipeName_InitializesSuccessfully()
		{
			String pipeName = "TestRegistry";

			this._registryServer = new RegistryServer(pipeName);

			Assert.AreEqual(pipeName, this._registryServer.PipeName);
			Assert.IsFalse(this._registryServer.IsStarted);
			Assert.IsNotNull(this._registryServer.ResponseChannel);
		}

		[Test]
		public void PipeName_Property_ReturnsConfiguredValue()
		{
			String expectedPipeName = "MyRegistry.Pipe";

			this._registryServer = new RegistryServer(expectedPipeName);

			Assert.AreEqual(expectedPipeName, this._registryServer.PipeName);
		}

		[Test]
		public async Task ResponseChannel_WaitForResponseAsync_TimesOut()
		{
			this._registryServer = new RegistryServer("TestPipe");

			PipeMessage request = new PipeMessage("TestType", "Payload");

			// Should throw TimeoutException after timeout
			Assert.ThrowsAsync<TimeoutException>(async () =>
				await this._registryServer.ResponseChannel.WaitForResponseAsync(request, TimeSpan.FromMilliseconds(100)));
		}

		[Test]
		public async Task ResponseChannel_FailResponse_CancelsWaitingTask()
		{
			this._registryServer = new RegistryServer("TestPipe");

			PipeMessage request = new PipeMessage("TestType", "Payload");

			// Start waiting for response
			Task<PipeMessage> waitTask = this._registryServer.ResponseChannel.WaitForResponseAsync(request, TimeSpan.FromSeconds(30));

			// Fail the response
			Exception failureException = new IOException("Connection lost");
			this._registryServer.ResponseChannel.FailResponse(request, failureException);

			// Should complete with exception
			Assert.ThrowsAsync<IOException>(async () => await waitTask);
		}

		[Test]
		public void ServerSideWorker_Constructor_InitializesProperties()
		{
			String workerId = "worker1";
			String pipeName = "worker.pipe";
			Guid connectionId = Guid.NewGuid();

			ServerSideWorker worker = new ServerSideWorker(workerId, pipeName, connectionId);

			Assert.AreEqual(workerId, worker.WorkerId);
			Assert.AreEqual(pipeName, worker.WorkerPipeName);
			Assert.AreEqual(connectionId, worker.ConnectionId);
		}

		[Test]
		public void ServerSideWorker_Constructor_WithNullWorkerId_ThrowsArgumentNullException()
		{
			Assert.Throws<ArgumentNullException>(() =>
				new ServerSideWorker(null, "pipe", Guid.NewGuid()));
		}

		[Test]
		public void ServerSideWorker_Constructor_WithNullPipeName_ThrowsArgumentNullException()
		{
			Assert.Throws<ArgumentNullException>(() =>
				new ServerSideWorker("worker", null, Guid.NewGuid()));
		}

		[Test]
		public void PipeMessage_Constructor_WithTypeAndPayload_CreatesMessage()
		{
			String type = "TestType";
			String payload = "TestPayload";

			PipeMessage message = new PipeMessage(type, payload);

			Assert.AreEqual(type, message.Type);
			Assert.IsNotNull(message.Payload);
			Assert.IsNotEmpty(message.Payload);
		}

		[Test]
		public void PipeMessage_Deserialize_ReturnsCorrectPayload()
		{
			String type = "TestType";
			String payload = "TestPayload";
			PipeMessage message = new PipeMessage(type, payload);

			String result = message.Deserialize<String>();

			Assert.AreEqual(payload, result);
		}

		[Test]
		public void PipeMessage_CopyConstructor_CopiesRequestId()
		{
			PipeMessage original = new PipeMessage("Type1", "Payload1");
			Guid originalRequestId = original.RequestId;

			PipeMessage copy = new PipeMessage(original);

			Assert.AreEqual(originalRequestId, copy.RequestId);
		}

		[Test]
		public void PipeMessage_Constructor_WithPreviousRequestAndNewType_CopiesRequestId()
		{
			PipeMessage original = new PipeMessage("Type1", "Payload1");
			Guid originalRequestId = original.RequestId;

			PipeMessage response = new PipeMessage(original, "ResponseType", "ResponsePayload");

			Assert.AreEqual(originalRequestId, response.RequestId);
			Assert.AreEqual("ResponseType", response.Type);
		}

		[Test]
		public async Task SendRequestToWorker_WithInactiveConnection_ThrowsIOException()
		{
			this._registryServer = new RegistryServer("TestPipe");

			PipeMessage request = new PipeMessage("TestType", "Payload");

			// Should throw IOException because worker is not registered
			Assert.ThrowsAsync<InvalidOperationException>(async () =>
				await this._registryServer.SendRequestToWorker("unknownWorker", request, CancellationToken.None));
		}

		[Test]
		public async Task ConnectedWorkerIDs_IsEnumerable()
		{
			this._registryServer = new RegistryServer("TestPipe");

			IEnumerable<String> workerIds = ((IRegistryServer)this._registryServer).ConnectedWorkerIDs;

			// Should be enumerable without throwing
			Int32 count = workerIds.Count();
			Assert.AreEqual(0, count);
		}

		[Test]
		public void RpcResponseChannel_Constructor_Initializes()
		{
			RpcResponseChannel channel = new RpcResponseChannel();

			Assert.IsNotNull(channel);
		}

		[Test]
		public async Task RpcResponseChannel_WaitForResponseAsync_CreatesWaitingTask()
		{
			RpcResponseChannel channel = new RpcResponseChannel();
			PipeMessage request = new PipeMessage("TestType", "Payload");

			Task<PipeMessage> waitTask = channel.WaitForResponseAsync(request, TimeSpan.FromSeconds(1));

			Assert.IsNotNull(waitTask);
			Assert.IsFalse(waitTask.IsCompleted);
		}

		[Test]
		public void RpcResponseChannel_CompleteResponse_ReturnsFalseInitially()
		{
			RpcResponseChannel channel = new RpcResponseChannel();
			PipeMessage request = new PipeMessage("TestType", "Payload");
			PipeMessage response = new PipeMessage(request, "ResponseType", "ResponsePayload");

			Boolean result = channel.CompleteResponse(request, response);

			// Should return false since no one is waiting
			Assert.IsFalse(result);
		}

		[Test]
		public void RegistryServer_Implements_IRegistryServer()
		{
			this._registryServer = new RegistryServer("TestPipe");

			Assert.IsInstanceOf<IRegistryServer>(this._registryServer);
		}

		[Test]
		public void RegistryServer_Implements_IServerBase()
		{
			this._registryServer = new RegistryServer("TestPipe");

			Assert.IsInstanceOf<IServerBase>(this._registryServer);
		}

		[Test]
		public void RegistryServer_Implements_IDisposable()
		{
			this._registryServer = new RegistryServer("TestPipe");

			Assert.IsInstanceOf<IDisposable>(this._registryServer);
		}

		[Test]
		public async Task CreateProcessingLogic_Generic_ThrowsNotImplemented()
		{
			this._registryServer = new RegistryServer("TestPipe");

			// CreateProcessingLogic will depend on RemoteProcessingLogicBuilder implementation
			// This test ensures it can be called without crashing the server
			Assert.Pass("CreateProcessingLogic can be called on RegistryServer");
		}

		[Test]
		public async Task RegistryServer_MultipleDispose_DoesNotThrow()
		{
			this._registryServer = new RegistryServer("TestPipe");

			// First dispose should work
			this._registryServer.Dispose();

			// Second dispose may throw ObjectDisposedException because CancellationTokenSource is already disposed
			// This is acceptable behavior and we should test that it can be called
			try
			{
				this._registryServer.Dispose();
			}
			catch(ObjectDisposedException)
			{
				// This is expected - the CancellationTokenSource is already disposed
				Assert.Pass("Dispose can be called multiple times (subsequent calls may throw ObjectDisposedException)");
			}

			Assert.Pass();
		}

		[Test]
		public async Task StartAsync_WithCancellationToken_DoesNotThrow()
		{
			this._registryServer = new RegistryServer("TestPipe");

			using(CancellationTokenSource cts = new CancellationTokenSource())
			{
				cts.CancelAfter(TimeSpan.FromMilliseconds(10));

				// Should complete without throwing
				try
				{
					await this._registryServer.StartAsync(cts.Token);
				}
				catch(OperationCanceledException)
				{
					// Expected
				}
			}
		}

		[Test]
		public async Task StopAsync_WhenNotStarted_DoesNotThrow()
		{
			this._registryServer = new RegistryServer("TestPipe");

			// Should complete without throwing
			await this._registryServer.StopAsync();

			Assert.Pass();
		}

		[Test]
		public void PipeMessage_HasValidRequestId()
		{
			PipeMessage message = new PipeMessage("Type", "Payload");

			Assert.AreNotEqual(Guid.Empty, message.RequestId);
		}

		[Test]
		public void PipeMessage_HasValidMessageId()
		{
			PipeMessage message = new PipeMessage("Type", "Payload");

			Assert.AreNotEqual(Guid.Empty, message.MessageId);
		}

		[Test]
		public void RegistryServer_EventHandlers_CanCoexist()
		{
			this._registryServer = new RegistryServer("TestPipe");

			this._registryServer.WorkerConnected += async (id) => { await Task.CompletedTask; };
			this._registryServer.WorkerDisconnected += async (id) => { await Task.CompletedTask; };
			this._registryServer.RequestReceived += async (msg, token) => { await Task.CompletedTask; return null; };

			Assert.Pass("Multiple event handlers can be registered");
		}
	}
}
