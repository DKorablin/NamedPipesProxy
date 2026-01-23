using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AlphaOmega.IO;
using AlphaOmega.IO.DTOs;
using AlphaOmega.IO.Interfaces;
using AlphaOmega.IO.Reflection;
using Moq;
using NUnit.Framework;

#if NETFRAMEWORK
using System.Runtime.Remoting.Messaging;
#endif

namespace NamedPipesProxy.Tests.Reflection
{
	[TestFixture]
	[Timeout(5000)]
	public class RemoteProcessingLogicInvokerTests
	{
		private Mock<IRegistryServer> _mockRegistryServer;
		private CancellationTokenSource _cancellationTokenSource;
		private RemoteProcessingLogicInvoker _invoker;

		public interface ITestProcessingLogic
		{
			void VoidMethod();
			Int32 SyncMethod(String arg);
			Task AsyncMethod(String arg);
			Task<String> AsyncMethodWithResult(Int32 arg);
		}

		public interface INonInterface
		{
		}

		[SetUp]
		public void SetUp()
		{
			this._mockRegistryServer = new Mock<IRegistryServer>();
			this._cancellationTokenSource = new CancellationTokenSource();

#if NETFRAMEWORK
			this._invoker = new RemoteProcessingLogicInvoker(typeof(ITestProcessingLogic));
#else
			this._invoker = new RemoteProcessingLogicInvoker();
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
			RemoteProcessingLogicInvoker invoker = new RemoteProcessingLogicInvoker();
			Assert.IsNotNull(invoker);
		}

#if NETFRAMEWORK
		[Test]
		public void Constructor_WithValidInterfaceType_CreatesInstance()
		{
			RemoteProcessingLogicInvoker invoker = new RemoteProcessingLogicInvoker(typeof(ITestProcessingLogic));
			Assert.IsNotNull(invoker);
		}

		[Test]
		public void Constructor_WithNullInterfaceType_ThrowsException()
		{
			// In .NET Framework, RealProxy base constructor is called first and throws NullReferenceException
			Assert.Throws<NullReferenceException>(() => new RemoteProcessingLogicInvoker(null));
		}

		[Test]
		public void Constructor_WithNonInterfaceType_ThrowsArgumentException()
		{
			// In .NET Framework, RealProxy base constructor validates the type first
			ArgumentException ex = Assert.Throws<ArgumentException>(() => new RemoteProcessingLogicInvoker(typeof(String)));
			Assert.That(ex.Message, Does.Contain("MarshalByRef"));
		}
#endif

		#endregion

		#region Initialize Tests

		[Test]
		public void Initialize_WithValidParameters_InitializesProxy()
		{
			this._invoker.Initialize<ITestProcessingLogic>(this._mockRegistryServer.Object, this._cancellationTokenSource.Token);

			// Verify initialization by checking internal state via reflection
			PropertyInfo registerServerProp = typeof(RemoteProcessingLogicInvoker).GetProperty("RegisterServer", BindingFlags.NonPublic | BindingFlags.Instance);
			Object registerServer = registerServerProp?.GetValue(this._invoker);
			Assert.IsNotNull(registerServer);
			Assert.AreSame(this._mockRegistryServer.Object, registerServer);
		}

		[Test]
		public void Initialize_WithNullRegistryServer_ThrowsArgumentNullException()
		{
			Assert.Throws<ArgumentNullException>(() =>
				this._invoker.Initialize<ITestProcessingLogic>(null, this._cancellationTokenSource.Token));
		}

		[Test]
		public void Initialize_WithNonInterfaceType_ThrowsInvalidOperationException()
		{
			RemoteProcessingLogicInvoker invoker = new RemoteProcessingLogicInvoker();
			Assert.Throws<InvalidOperationException>(() =>
				invoker.Initialize<String>(this._mockRegistryServer.Object, this._cancellationTokenSource.Token));
		}

		[Test]
		public void Initialize_CachesInterfaceMethods()
		{
			this._invoker.Initialize<ITestProcessingLogic>(this._mockRegistryServer.Object, this._cancellationTokenSource.Token);

			FieldInfo methodsCacheField = typeof(RemoteProcessingLogicInvoker).GetField("_methodsCache", BindingFlags.NonPublic | BindingFlags.Instance);
			var methodsCache = methodsCacheField?.GetValue(this._invoker) as Dictionary<String, MethodInfo>;

			Assert.IsNotNull(methodsCache);
			Assert.IsTrue(methodsCache.Count > 0);
			Assert.IsTrue(methodsCache.ContainsKey("VoidMethod"));
			Assert.IsTrue(methodsCache.ContainsKey("SyncMethod"));
			Assert.IsTrue(methodsCache.ContainsKey("AsyncMethod"));
			Assert.IsTrue(methodsCache.ContainsKey("AsyncMethodWithResult"));
		}

		#endregion

		#region InvokeImpl Tests (via Invoke)

#if NETFRAMEWORK
		[Test]
		public void Invoke_WithNullMessage_ReturnsReturnMessage()
		{
			this._invoker.Initialize<ITestProcessingLogic>(this._mockRegistryServer.Object, this._cancellationTokenSource.Token);

			IMessage result = this._invoker.Invoke(null);

			Assert.IsNotNull(result);
			Assert.IsInstanceOf<ReturnMessage>(result);
		}

		[Test]
		public void Invoke_WithNonMethodCallMessage_ReturnsReturnMessage()
		{
			this._invoker.Initialize<ITestProcessingLogic>(this._mockRegistryServer.Object, this._cancellationTokenSource.Token);

			Mock<IMessage> mockMessage = new Mock<IMessage>();
			IMessage result = this._invoker.Invoke(mockMessage.Object);

			Assert.IsNotNull(result);
			Assert.IsInstanceOf<ReturnMessage>(result);
		}
#endif

		[Test]
		public void InvokeImpl_WithoutInitialization_ThrowsInvalidOperationException()
		{
			RemoteProcessingLogicInvoker invoker = new RemoteProcessingLogicInvoker();
			MethodInfo method = typeof(ITestProcessingLogic).GetMethod("VoidMethod");

			MethodInfo invokeImplMethod = typeof(RemoteProcessingLogicInvoker).GetMethod("InvokeImpl", BindingFlags.NonPublic | BindingFlags.Instance);

			TargetInvocationException ex = Assert.Throws<TargetInvocationException>(() =>
				invokeImplMethod.Invoke(invoker, new Object[] { method, new Object[] { } }));

			Assert.IsInstanceOf<InvalidOperationException>(ex.InnerException);
			Assert.That(ex.InnerException.Message, Does.Contain("not properly initialized"));
		}

		#endregion

		#region SendRequestAndGetResponseAsync Tests

		[Test]
		public async Task SendRequestAndGetResponseAsync_NoWorkersConnected_ThrowsInvalidOperationException()
		{
			this._mockRegistryServer.Setup(x => x.ConnectedWorkerIDs).Returns(Enumerable.Empty<String>());
			this._invoker.Initialize<ITestProcessingLogic>(this._mockRegistryServer.Object, this._cancellationTokenSource.Token);

			MethodInfo sendRequestMethod = typeof(RemoteProcessingLogicInvoker).GetMethod("SendRequestAndGetResponseAsync", BindingFlags.NonPublic | BindingFlags.Instance);
			PipeMessage request = new PipeMessage("TestMethod", new Object[] { });

			Task<Object> task = (Task<Object>)sendRequestMethod.Invoke(this._invoker, new Object[] { request, typeof(Object) });

			InvalidOperationException ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await task);
			Assert.That(ex.Message, Does.Contain("No workers connected"));
		}

		[Test]
		public async Task SendRequestAndGetResponseAsync_WithWorkerReturningValidResponse_ReturnsDeserializedResponse()
		{
			String workerId = "worker-1";
			this._mockRegistryServer.Setup(x => x.ConnectedWorkerIDs).Returns(new[] { workerId });

			PipeMessage response = new PipeMessage("Response", 42);
			this._mockRegistryServer
				.Setup(x => x.SendRequestToWorker(workerId, It.IsAny<PipeMessage>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(response);

			this._invoker.Initialize<ITestProcessingLogic>(this._mockRegistryServer.Object, this._cancellationTokenSource.Token);

			MethodInfo sendRequestMethod = typeof(RemoteProcessingLogicInvoker).GetMethod("SendRequestAndGetResponseAsync", BindingFlags.NonPublic | BindingFlags.Instance);
			PipeMessage request = new PipeMessage("TestMethod", new Object[] { });

			Task<Object> task = (Task<Object>)sendRequestMethod.Invoke(this._invoker, new Object[] { request, typeof(Int32) });
			Object result = await task;

			Assert.AreEqual(42, result);
		}

		[Test]
		public async Task SendRequestAndGetResponseAsync_WithWorkerReturningError_ThrowsInvalidOperationException()
		{
			String workerId = "worker-1";
			this._mockRegistryServer.Setup(x => x.ConnectedWorkerIDs).Returns(new[] { workerId });

			ErrorResponse errorResponse = new ErrorResponse("Test error message");
			PipeMessage response = new PipeMessage(PipeMessageType.Error.ToString(), errorResponse);
			this._mockRegistryServer
				.Setup(x => x.SendRequestToWorker(workerId, It.IsAny<PipeMessage>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(response);

			this._invoker.Initialize<ITestProcessingLogic>(this._mockRegistryServer.Object, this._cancellationTokenSource.Token);

			MethodInfo sendRequestMethod = typeof(RemoteProcessingLogicInvoker).GetMethod("SendRequestAndGetResponseAsync", BindingFlags.NonPublic | BindingFlags.Instance);
			PipeMessage request = new PipeMessage("TestMethod", new Object[] { });

			Task<Object> task = (Task<Object>)sendRequestMethod.Invoke(this._invoker, new Object[] { request, typeof(Object) });

			InvalidOperationException ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await task);
			Assert.That(ex.Message, Does.Contain("Test error message"));
		}

		[Test]
		public async Task SendRequestAndGetResponseAsync_WithWorkerReturningNull_ReturnsNull()
		{
			String workerId = "worker-1";
			this._mockRegistryServer.Setup(x => x.ConnectedWorkerIDs).Returns(new[] { workerId });

			PipeMessage response = new PipeMessage(PipeMessageType.Null.ToString(), null);
			this._mockRegistryServer
				.Setup(x => x.SendRequestToWorker(workerId, It.IsAny<PipeMessage>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(response);

			this._invoker.Initialize<ITestProcessingLogic>(this._mockRegistryServer.Object, this._cancellationTokenSource.Token);

			MethodInfo sendRequestMethod = typeof(RemoteProcessingLogicInvoker).GetMethod("SendRequestAndGetResponseAsync", BindingFlags.NonPublic | BindingFlags.Instance);
			PipeMessage request = new PipeMessage("TestMethod", new Object[] { });

			Task<Object> task = (Task<Object>)sendRequestMethod.Invoke(this._invoker, new Object[] { request, typeof(Object) });
			Object result = await task;

			Assert.IsNull(result);
		}

		[Test]
		public async Task SendRequestAndGetResponseAsync_WithMultipleWorkers_ReturnsFirstNonNullResponse()
		{
			String workerId1 = "worker-1";
			String workerId2 = "worker-2";
			this._mockRegistryServer.Setup(x => x.ConnectedWorkerIDs).Returns(new[] { workerId1, workerId2 });

			TaskCompletionSource<PipeMessage> tcs1 = new TaskCompletionSource<PipeMessage>();
			TaskCompletionSource<PipeMessage> tcs2 = new TaskCompletionSource<PipeMessage>();

			this._mockRegistryServer
				.Setup(x => x.SendRequestToWorker(workerId1, It.IsAny<PipeMessage>(), It.IsAny<CancellationToken>()))
				.Returns(tcs1.Task);

			this._mockRegistryServer
				.Setup(x => x.SendRequestToWorker(workerId2, It.IsAny<PipeMessage>(), It.IsAny<CancellationToken>()))
				.Returns(tcs2.Task);

			this._invoker.Initialize<ITestProcessingLogic>(this._mockRegistryServer.Object, this._cancellationTokenSource.Token);

			MethodInfo sendRequestMethod = typeof(RemoteProcessingLogicInvoker).GetMethod("SendRequestAndGetResponseAsync", BindingFlags.NonPublic | BindingFlags.Instance);
			PipeMessage request = new PipeMessage("TestMethod", new Object[] { });

			Task<Object> task = (Task<Object>)sendRequestMethod.Invoke(this._invoker, new Object[] { request, typeof(String) });

			PipeMessage nullResponse = new PipeMessage(PipeMessageType.Null.ToString(), null);
			tcs1.SetResult(nullResponse);

			await Task.Delay(50);

			PipeMessage validResponse = new PipeMessage("Response", "result");
			tcs2.SetResult(validResponse);

			Object result = await task;
			Assert.AreEqual("result", result);
		}

		[Test]
		public async Task SendRequestAndGetResponseAsync_AllWorkersReturnNull_ReturnsNull()
		{
			String workerId1 = "worker-1";
			String workerId2 = "worker-2";
			this._mockRegistryServer.Setup(x => x.ConnectedWorkerIDs).Returns(new[] { workerId1, workerId2 });

			PipeMessage nullResponse = new PipeMessage(PipeMessageType.Null.ToString(), null);
			this._mockRegistryServer
				.Setup(x => x.SendRequestToWorker(It.IsAny<String>(), It.IsAny<PipeMessage>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(nullResponse);

			this._invoker.Initialize<ITestProcessingLogic>(this._mockRegistryServer.Object, this._cancellationTokenSource.Token);

			MethodInfo sendRequestMethod = typeof(RemoteProcessingLogicInvoker).GetMethod("SendRequestAndGetResponseAsync", BindingFlags.NonPublic | BindingFlags.Instance);
			PipeMessage request = new PipeMessage("TestMethod", new Object[] { });

			Task<Object> task = (Task<Object>)sendRequestMethod.Invoke(this._invoker, new Object[] { request, typeof(Object) });
			Object result = await task;

			Assert.IsNull(result);
		}

		#endregion

		#region CastTask Tests

		[Test]
		public async Task CastTask_CastsTaskObjectToTaskT()
		{
			MethodInfo castTaskMethod = typeof(RemoteProcessingLogicInvoker).GetMethod("CastTask", BindingFlags.NonPublic | BindingFlags.Static);
			MethodInfo genericCastTask = castTaskMethod.MakeGenericMethod(typeof(String));

			Task<Object> sourceTask = Task.FromResult<Object>("test result");
			Task<String> resultTask = (Task<String>)genericCastTask.Invoke(null, new Object[] { sourceTask });

			String result = await resultTask;
			Assert.AreEqual("test result", result);
		}

		[Test]
		public async Task CastTask_WithInt32_CastsCorrectly()
		{
			MethodInfo castTaskMethod = typeof(RemoteProcessingLogicInvoker).GetMethod("CastTask", BindingFlags.NonPublic | BindingFlags.Static);
			MethodInfo genericCastTask = castTaskMethod.MakeGenericMethod(typeof(Int32));

			Task<Object> sourceTask = Task.FromResult<Object>(42);
			Task<Int32> resultTask = (Task<Int32>)genericCastTask.Invoke(null, new Object[] { sourceTask });

			Int32 result = await resultTask;
			Assert.AreEqual(42, result);
		}

		#endregion

		#region Integration Tests for Different Method Return Types

		[Test]
		public void InvokeImpl_SyncMethod_ReturnsResult()
		{
			String workerId = "worker-1";
			this._mockRegistryServer.Setup(x => x.ConnectedWorkerIDs).Returns(new[] { workerId });

			PipeMessage response = new PipeMessage("Response", 42);
			this._mockRegistryServer
				.Setup(x => x.SendRequestToWorker(workerId, It.IsAny<PipeMessage>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(response);

			this._invoker.Initialize<ITestProcessingLogic>(this._mockRegistryServer.Object, this._cancellationTokenSource.Token);

			MethodInfo method = typeof(ITestProcessingLogic).GetMethod("SyncMethod");
			MethodInfo invokeImplMethod = typeof(RemoteProcessingLogicInvoker).GetMethod("InvokeImpl", BindingFlags.NonPublic | BindingFlags.Instance);

			Object result = invokeImplMethod.Invoke(this._invoker, new Object[] { method, new Object[] { "test" } });

			Assert.AreEqual(42, result);
		}

		[Test]
		public void InvokeImpl_TaskMethod_ReturnsTask()
		{
			String workerId = "worker-1";
			this._mockRegistryServer.Setup(x => x.ConnectedWorkerIDs).Returns(new[] { workerId });

			PipeMessage response = new PipeMessage("Response", new Object());
			this._mockRegistryServer
				.Setup(x => x.SendRequestToWorker(workerId, It.IsAny<PipeMessage>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(response);

			this._invoker.Initialize<ITestProcessingLogic>(this._mockRegistryServer.Object, this._cancellationTokenSource.Token);

			MethodInfo method = typeof(ITestProcessingLogic).GetMethod("AsyncMethod");
			MethodInfo invokeImplMethod = typeof(RemoteProcessingLogicInvoker).GetMethod("InvokeImpl", BindingFlags.NonPublic | BindingFlags.Instance);

			Object result = invokeImplMethod.Invoke(this._invoker, new Object[] { method, new Object[] { "test" } });

			Assert.IsInstanceOf<Task<Object>>(result);
		}

		[Test]
		public async Task InvokeImpl_TaskOfTMethod_ReturnsTaskOfT()
		{
			String workerId = "worker-1";
			this._mockRegistryServer.Setup(x => x.ConnectedWorkerIDs).Returns(new[] { workerId });

			PipeMessage response = new PipeMessage("Response", "test result");
			this._mockRegistryServer
				.Setup(x => x.SendRequestToWorker(workerId, It.IsAny<PipeMessage>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(response);

			this._invoker.Initialize<ITestProcessingLogic>(this._mockRegistryServer.Object, this._cancellationTokenSource.Token);

			MethodInfo method = typeof(ITestProcessingLogic).GetMethod("AsyncMethodWithResult");
			MethodInfo invokeImplMethod = typeof(RemoteProcessingLogicInvoker).GetMethod("InvokeImpl", BindingFlags.NonPublic | BindingFlags.Instance);

			Object result = invokeImplMethod.Invoke(this._invoker, new Object[] { method, new Object[] { 123 } });

			Assert.IsInstanceOf<Task<String>>(result);

			Task<String> taskResult = (Task<String>)result;
			String stringResult = await taskResult;
			Assert.AreEqual("test result", stringResult);
		}

		#endregion

		#region Property Tests

		[Test]
		public void RegisterServer_AfterInitialize_IsSet()
		{
			this._invoker.Initialize<ITestProcessingLogic>(this._mockRegistryServer.Object, this._cancellationTokenSource.Token);

			PropertyInfo registerServerProp = typeof(RemoteProcessingLogicInvoker).GetProperty("RegisterServer", BindingFlags.NonPublic | BindingFlags.Instance);
			Object registerServer = registerServerProp?.GetValue(this._invoker);
			Assert.IsNotNull(registerServer);
			Assert.AreSame(this._mockRegistryServer.Object, registerServer);
		}

		[Test]
		public void CancellationToken_AfterInitialize_IsSet()
		{
			this._invoker.Initialize<ITestProcessingLogic>(this._mockRegistryServer.Object, this._cancellationTokenSource.Token);

			PropertyInfo cancellationTokenProp = typeof(RemoteProcessingLogicInvoker).GetProperty("CancellationToken", BindingFlags.NonPublic | BindingFlags.Instance);
			CancellationToken token = (CancellationToken)cancellationTokenProp?.GetValue(this._invoker);
			Assert.AreEqual(this._cancellationTokenSource.Token, token);
		}

		#endregion
	}
}
