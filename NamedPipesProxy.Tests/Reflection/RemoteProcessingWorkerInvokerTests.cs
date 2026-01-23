using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AlphaOmega.IO;
using AlphaOmega.IO.DTOs;
using AlphaOmega.IO.Interfaces;
using AlphaOmega.IO.Reflection;
using Moq;
using NUnit.Framework;

namespace NamedPipesProxy.Tests.Reflection
{
	[TestFixture]
	[Timeout(5000)]
	public class RemoteProcessingWorkerInvokerTests
	{
		private Mock<IRegistryServer> _mockRegistryServer;
		private CancellationTokenSource _cancellationTokenSource;
		private RemoteProcessingWorkerInvoker _invoker;

		public interface ITestProcessingLogic
		{
			void VoidMethod();
			Int32 SyncMethod(String arg);
			Task AsyncMethod(String arg);
			Task<String> AsyncMethodWithResult(Int32 arg);
		}

		[SetUp]
		public void SetUp()
		{
			this._mockRegistryServer = new Mock<IRegistryServer>();
			this._cancellationTokenSource = new CancellationTokenSource();

#if NETFRAMEWORK
			this._invoker = new RemoteProcessingWorkerInvoker(typeof(ITestProcessingLogic));
#else
			this._invoker = new RemoteProcessingWorkerInvoker();
#endif
		}

		[TearDown]
		public void TearDown()
		{
			this._cancellationTokenSource?.Dispose();
		}

		#region Constructor Tests

		[Test]
		public void Constructor_DefaultConstructor_CreatesInstance()
		{
			RemoteProcessingWorkerInvoker invoker = new RemoteProcessingWorkerInvoker();
			Assert.IsNotNull(invoker);
		}

#if NETFRAMEWORK
		[Test]
		public void Constructor_WithValidInterfaceType_CreatesInstance()
		{
			RemoteProcessingWorkerInvoker invoker = new RemoteProcessingWorkerInvoker(typeof(ITestProcessingLogic));
			Assert.IsNotNull(invoker);
		}

		[Test]
		public void Constructor_WithNullInterfaceType_ThrowsException()
		{
			Assert.Throws<NullReferenceException>(() => new RemoteProcessingWorkerInvoker(null));
		}

		[Test]
		public void Constructor_WithNonInterfaceType_ThrowsArgumentException()
		{
			ArgumentException ex = Assert.Throws<ArgumentException>(() => new RemoteProcessingWorkerInvoker(typeof(String)));
			Assert.That(ex.Message, Does.Contain("MarshalByRef"));
		}
#endif

		#endregion

		#region Initialize Tests

		[Test]
		public void Initialize_WithValidParameters_InitializesProxy()
		{
			String workerId = "worker-123";

			this._invoker.Initialize<ITestProcessingLogic>(this._mockRegistryServer.Object, workerId, this._cancellationTokenSource.Token);

			PropertyInfo registerServerProp = typeof(RemoteProcessingLogicInvoker).GetProperty("RegisterServer", BindingFlags.NonPublic | BindingFlags.Instance);
			Object registerServer = registerServerProp?.GetValue(this._invoker);
			Assert.IsNotNull(registerServer);
			Assert.AreSame(this._mockRegistryServer.Object, registerServer);

			FieldInfo workerIdField = typeof(RemoteProcessingWorkerInvoker).GetField("_workerId", BindingFlags.NonPublic | BindingFlags.Instance);
			String storedWorkerId = (String)workerIdField?.GetValue(this._invoker);
			Assert.AreEqual(workerId, storedWorkerId);
		}

		[Test]
		public void Initialize_WithNullWorkerId_ThrowsArgumentNullException()
		{
			ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() =>
				this._invoker.Initialize<ITestProcessingLogic>(this._mockRegistryServer.Object, null, this._cancellationTokenSource.Token));
			Assert.That(ex.ParamName, Is.EqualTo("workerId"));
		}

		[Test]
		public void Initialize_WithEmptyWorkerId_ThrowsArgumentNullException()
		{
			ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() =>
				this._invoker.Initialize<ITestProcessingLogic>(this._mockRegistryServer.Object, "", this._cancellationTokenSource.Token));
			Assert.That(ex.ParamName, Is.EqualTo("workerId"));
		}

		[Test]
		public void Initialize_WithWhitespaceWorkerId_ThrowsArgumentNullException()
		{
			ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() =>
				this._invoker.Initialize<ITestProcessingLogic>(this._mockRegistryServer.Object, "   ", this._cancellationTokenSource.Token));
			Assert.That(ex.ParamName, Is.EqualTo("workerId"));
		}

		[Test]
		public void Initialize_WithNullRegistryServer_ThrowsArgumentNullException()
		{
			Assert.Throws<ArgumentNullException>(() =>
				this._invoker.Initialize<ITestProcessingLogic>(null, "worker-1", this._cancellationTokenSource.Token));
		}

		[Test]
		public void Initialize_WithNonInterfaceType_ThrowsInvalidOperationException()
		{
			RemoteProcessingWorkerInvoker invoker = new RemoteProcessingWorkerInvoker();
			Assert.Throws<InvalidOperationException>(() =>
				invoker.Initialize<String>(this._mockRegistryServer.Object, "worker-1", this._cancellationTokenSource.Token));
		}

		[Test]
		public void Initialize_StoresWorkerIdInField()
		{
			String workerId = "test-worker-id";
			this._invoker.Initialize<ITestProcessingLogic>(this._mockRegistryServer.Object, workerId, this._cancellationTokenSource.Token);

			FieldInfo workerIdField = typeof(RemoteProcessingWorkerInvoker).GetField("_workerId", BindingFlags.NonPublic | BindingFlags.Instance);
			String storedWorkerId = (String)workerIdField?.GetValue(this._invoker);

			Assert.AreEqual(workerId, storedWorkerId);
		}

		[Test]
		public void Initialize_CallsBaseInitialize()
		{
			String workerId = "worker-1";
			this._invoker.Initialize<ITestProcessingLogic>(this._mockRegistryServer.Object, workerId, this._cancellationTokenSource.Token);

			FieldInfo methodsCacheField = typeof(RemoteProcessingLogicInvoker).GetField("_methodsCache", BindingFlags.NonPublic | BindingFlags.Instance);
			var methodsCache = methodsCacheField?.GetValue(this._invoker);

			Assert.IsNotNull(methodsCache);
		}

		#endregion

		#region SendRequestAndGetResponseAsync Tests

		[Test]
		public async Task SendRequestAndGetResponseAsync_WithoutInitialization_ThrowsInvalidOperationException()
		{
			RemoteProcessingWorkerInvoker invoker = new RemoteProcessingWorkerInvoker();
			MethodInfo sendRequestMethod = typeof(RemoteProcessingWorkerInvoker).GetMethod("SendRequestAndGetResponseAsync", BindingFlags.NonPublic | BindingFlags.Instance);
			PipeMessage request = new PipeMessage("TestMethod", new Object[] { });

			Task<Object> task = (Task<Object>)sendRequestMethod.Invoke(invoker, new Object[] { request, typeof(Object) });

			InvalidOperationException ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await task);
			Assert.That(ex.Message, Does.Contain("not properly initialized"));
		}

		[Test]
		public async Task SendRequestAndGetResponseAsync_WithValidResponse_ReturnsDeserializedResponse()
		{
			String workerId = "worker-1";
			this._invoker.Initialize<ITestProcessingLogic>(this._mockRegistryServer.Object, workerId, this._cancellationTokenSource.Token);

			PipeMessage response = new PipeMessage("Response", 42);
			this._mockRegistryServer
				.Setup(x => x.SendRequestToWorker(workerId, It.IsAny<PipeMessage>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(response);

			MethodInfo sendRequestMethod = typeof(RemoteProcessingWorkerInvoker).GetMethod("SendRequestAndGetResponseAsync", BindingFlags.NonPublic | BindingFlags.Instance);
			PipeMessage request = new PipeMessage("TestMethod", new Object[] { });

			Task<Object> task = (Task<Object>)sendRequestMethod.Invoke(this._invoker, new Object[] { request, typeof(Int32) });
			Object result = await task;

			Assert.AreEqual(42, result);
			this._mockRegistryServer.Verify(x => x.SendRequestToWorker(workerId, It.IsAny<PipeMessage>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task SendRequestAndGetResponseAsync_WithErrorResponse_ThrowsInvalidOperationException()
		{
			String workerId = "worker-1";
			this._invoker.Initialize<ITestProcessingLogic>(this._mockRegistryServer.Object, workerId, this._cancellationTokenSource.Token);

			ErrorResponse errorResponse = new ErrorResponse("Test error message");
			PipeMessage response = new PipeMessage(PipeMessageType.Error.ToString(), errorResponse);
			this._mockRegistryServer
				.Setup(x => x.SendRequestToWorker(workerId, It.IsAny<PipeMessage>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(response);

			MethodInfo sendRequestMethod = typeof(RemoteProcessingWorkerInvoker).GetMethod("SendRequestAndGetResponseAsync", BindingFlags.NonPublic | BindingFlags.Instance);
			PipeMessage request = new PipeMessage("TestMethod", new Object[] { });

			Task<Object> task = (Task<Object>)sendRequestMethod.Invoke(this._invoker, new Object[] { request, typeof(Object) });

			InvalidOperationException ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await task);
			Assert.That(ex.Message, Does.Contain("Test error message"));
		}

		[Test]
		public async Task SendRequestAndGetResponseAsync_WithStringResponse_ReturnsString()
		{
			String workerId = "worker-1";
			this._invoker.Initialize<ITestProcessingLogic>(this._mockRegistryServer.Object, workerId, this._cancellationTokenSource.Token);

			PipeMessage response = new PipeMessage("Response", "test result");
			this._mockRegistryServer
				.Setup(x => x.SendRequestToWorker(workerId, It.IsAny<PipeMessage>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(response);

			MethodInfo sendRequestMethod = typeof(RemoteProcessingWorkerInvoker).GetMethod("SendRequestAndGetResponseAsync", BindingFlags.NonPublic | BindingFlags.Instance);
			PipeMessage request = new PipeMessage("TestMethod", new Object[] { });

			Task<Object> task = (Task<Object>)sendRequestMethod.Invoke(this._invoker, new Object[] { request, typeof(String) });
			Object result = await task;

			Assert.AreEqual("test result", result);
		}

		[Test]
		public async Task SendRequestAndGetResponseAsync_SendsToCorrectWorker()
		{
			String workerId = "specific-worker-123";
			this._invoker.Initialize<ITestProcessingLogic>(this._mockRegistryServer.Object, workerId, this._cancellationTokenSource.Token);

			PipeMessage response = new PipeMessage("Response", 100);
			this._mockRegistryServer
				.Setup(x => x.SendRequestToWorker(workerId, It.IsAny<PipeMessage>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(response);

			MethodInfo sendRequestMethod = typeof(RemoteProcessingWorkerInvoker).GetMethod("SendRequestAndGetResponseAsync", BindingFlags.NonPublic | BindingFlags.Instance);
			PipeMessage request = new PipeMessage("TestMethod", new Object[] { });

			Task<Object> task = (Task<Object>)sendRequestMethod.Invoke(this._invoker, new Object[] { request, typeof(Int32) });
			await task;

			this._mockRegistryServer.Verify(x => x.SendRequestToWorker(
				It.Is<String>(id => id == workerId),
				It.IsAny<PipeMessage>(),
				It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task SendRequestAndGetResponseAsync_PassesCancellationToken()
		{
			String workerId = "worker-1";
			this._invoker.Initialize<ITestProcessingLogic>(this._mockRegistryServer.Object, workerId, this._cancellationTokenSource.Token);

			PipeMessage response = new PipeMessage("Response", 42);
			CancellationToken capturedToken = default;

			this._mockRegistryServer
				.Setup(x => x.SendRequestToWorker(It.IsAny<String>(), It.IsAny<PipeMessage>(), It.IsAny<CancellationToken>()))
				.Callback<String, PipeMessage, CancellationToken>((id, msg, token) => capturedToken = token)
				.ReturnsAsync(response);

			MethodInfo sendRequestMethod = typeof(RemoteProcessingWorkerInvoker).GetMethod("SendRequestAndGetResponseAsync", BindingFlags.NonPublic | BindingFlags.Instance);
			PipeMessage request = new PipeMessage("TestMethod", new Object[] { });

			Task<Object> task = (Task<Object>)sendRequestMethod.Invoke(this._invoker, new Object[] { request, typeof(Int32) });
			await task;

			Assert.AreEqual(this._cancellationTokenSource.Token, capturedToken);
		}

		#endregion

		#region Integration Tests for Different Method Return Types

		[Test]
		public void InvokeImpl_SyncMethod_SendsToSpecificWorker()
		{
			String workerId = "worker-1";
			this._invoker.Initialize<ITestProcessingLogic>(this._mockRegistryServer.Object, workerId, this._cancellationTokenSource.Token);

			PipeMessage response = new PipeMessage("Response", 42);
			this._mockRegistryServer
				.Setup(x => x.SendRequestToWorker(workerId, It.IsAny<PipeMessage>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(response);

			MethodInfo method = typeof(ITestProcessingLogic).GetMethod("SyncMethod");
			MethodInfo invokeImplMethod = typeof(RemoteProcessingLogicInvoker).GetMethod("InvokeImpl", BindingFlags.NonPublic | BindingFlags.Instance);

			Object result = invokeImplMethod.Invoke(this._invoker, new Object[] { method, new Object[] { "test" } });

			Assert.AreEqual(42, result);
			this._mockRegistryServer.Verify(x => x.SendRequestToWorker(workerId, It.IsAny<PipeMessage>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task InvokeImpl_AsyncMethodWithResult_SendsToSpecificWorker()
		{
			String workerId = "worker-1";
			this._invoker.Initialize<ITestProcessingLogic>(this._mockRegistryServer.Object, workerId, this._cancellationTokenSource.Token);

			PipeMessage response = new PipeMessage("Response", "test result");
			this._mockRegistryServer
				.Setup(x => x.SendRequestToWorker(workerId, It.IsAny<PipeMessage>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(response);

			MethodInfo method = typeof(ITestProcessingLogic).GetMethod("AsyncMethodWithResult");
			MethodInfo invokeImplMethod = typeof(RemoteProcessingLogicInvoker).GetMethod("InvokeImpl", BindingFlags.NonPublic | BindingFlags.Instance);

			Object result = invokeImplMethod.Invoke(this._invoker, new Object[] { method, new Object[] { 123 } });

			Assert.IsInstanceOf<Task<String>>(result);

			Task<String> taskResult = (Task<String>)result;
			String stringResult = await taskResult;
			Assert.AreEqual("test result", stringResult);

			this._mockRegistryServer.Verify(x => x.SendRequestToWorker(workerId, It.IsAny<PipeMessage>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		#endregion

		#region WorkerId Field Tests

		[Test]
		public void WorkerId_BeforeInitialization_IsNull()
		{
			RemoteProcessingWorkerInvoker invoker = new RemoteProcessingWorkerInvoker();

			FieldInfo workerIdField = typeof(RemoteProcessingWorkerInvoker).GetField("_workerId", BindingFlags.NonPublic | BindingFlags.Instance);
			String workerId = (String)workerIdField?.GetValue(invoker);

			Assert.IsNull(workerId);
		}

		[Test]
		public void WorkerId_AfterInitialization_IsSet()
		{
			String expectedWorkerId = "my-worker-id";
			this._invoker.Initialize<ITestProcessingLogic>(this._mockRegistryServer.Object, expectedWorkerId, this._cancellationTokenSource.Token);

			FieldInfo workerIdField = typeof(RemoteProcessingWorkerInvoker).GetField("_workerId", BindingFlags.NonPublic | BindingFlags.Instance);
			String actualWorkerId = (String)workerIdField?.GetValue(this._invoker);

			Assert.AreEqual(expectedWorkerId, actualWorkerId);
		}

		#endregion

		#region Edge Case Tests

		[Test]
		public async Task SendRequestAndGetResponseAsync_WithComplexObject_ReturnsDeserializedObject()
		{
			String workerId = "worker-1";
			this._invoker.Initialize<ITestProcessingLogic>(this._mockRegistryServer.Object, workerId, this._cancellationTokenSource.Token);

			TestPayload expectedPayload = new TestPayload { Name = "Test", Value = 99 };
			PipeMessage response = new PipeMessage("Response", expectedPayload);
			this._mockRegistryServer
				.Setup(x => x.SendRequestToWorker(workerId, It.IsAny<PipeMessage>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(response);

			MethodInfo sendRequestMethod = typeof(RemoteProcessingWorkerInvoker).GetMethod("SendRequestAndGetResponseAsync", BindingFlags.NonPublic | BindingFlags.Instance);
			PipeMessage request = new PipeMessage("TestMethod", new Object[] { });

			Task<Object> task = (Task<Object>)sendRequestMethod.Invoke(this._invoker, new Object[] { request, typeof(TestPayload) });
			Object result = await task;

			Assert.IsInstanceOf<TestPayload>(result);
			TestPayload actualPayload = (TestPayload)result;
			Assert.AreEqual(expectedPayload.Name, actualPayload.Name);
			Assert.AreEqual(expectedPayload.Value, actualPayload.Value);
		}

		[Test]
		public void Initialize_CanReinitializeWithDifferentWorkerId()
		{
			String firstWorkerId = "worker-1";
			String secondWorkerId = "worker-2";

			this._invoker.Initialize<ITestProcessingLogic>(this._mockRegistryServer.Object, firstWorkerId, this._cancellationTokenSource.Token);

			FieldInfo workerIdField = typeof(RemoteProcessingWorkerInvoker).GetField("_workerId", BindingFlags.NonPublic | BindingFlags.Instance);
			String storedWorkerId = (String)workerIdField?.GetValue(this._invoker);
			Assert.AreEqual(firstWorkerId, storedWorkerId);

			this._invoker.Initialize<ITestProcessingLogic>(this._mockRegistryServer.Object, secondWorkerId, this._cancellationTokenSource.Token);

			storedWorkerId = (String)workerIdField?.GetValue(this._invoker);
			Assert.AreEqual(secondWorkerId, storedWorkerId);
		}

		#endregion

		#region Helper Classes

		private class TestPayload
		{
			public String Name { get; set; }
			public Int32 Value { get; set; }
		}

		#endregion
	}
}
