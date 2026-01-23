using System;
using System.Reflection;
using System.Threading;
using AlphaOmega.IO.Interfaces;
using AlphaOmega.IO.Reflection;
using Moq;
using NUnit.Framework;
#if NETFRAMEWORK
using System.Runtime.Remoting;
using System.Runtime.Remoting.Proxies;
#endif

namespace NamedPipesProxy.Tests.Reflection
{
	[TestFixture]
	[Timeout(5000)]
	public class RemoteProcessingLogicBuilderTests
	{
		private Mock<IRegistryServer> _mockRegistryServer;
		private CancellationTokenSource _cancellationTokenSource;

		public interface ITestProcessingLogic
		{
			void VoidMethod();
		}

		[SetUp]
		public void SetUp()
		{
			this._mockRegistryServer = new Mock<IRegistryServer>();
			this._cancellationTokenSource = new CancellationTokenSource();
		}

		[TearDown]
		public void TearDown()
		{
			this._cancellationTokenSource?.Dispose();
		}

		[Test]
		public void CreateProcessingLogic_WithNullRegistryServer_ThrowsArgumentNullException()
		{
			Assert.Throws<ArgumentNullException>(() => RemoteProcessingLogicBuilder.CreateProcessingLogic<ITestProcessingLogic>(null, CancellationToken.None));
		}

		[Test]
		public void CreateProcessingLogic_WithValidParameters_ReturnsProxyAndInitializesInvoker()
		{
			CancellationToken token = this._cancellationTokenSource.Token;

			ITestProcessingLogic proxy = RemoteProcessingLogicBuilder.CreateProcessingLogic<ITestProcessingLogic>(this._mockRegistryServer.Object, token);

			Assert.IsNotNull(proxy);
			// Avoid IsInstanceOf for remoting proxies (may trigger GetType on proxy and invoke).

			RemoteProcessingLogicInvoker invoker = this.GetLogicInvoker(proxy);

			PropertyInfo registerServerProp = typeof(RemoteProcessingLogicInvoker).GetProperty("RegisterServer", BindingFlags.NonPublic | BindingFlags.Instance);
			Object registerServer = registerServerProp?.GetValue(invoker);

			Assert.AreSame(this._mockRegistryServer.Object, registerServer);
		}

		[Test]
		public void CreateProcessingLogic_ForWorker_WithNullRegistryServer_ThrowsArgumentNullException()
		{
			Assert.Throws<ArgumentNullException>(() => RemoteProcessingLogicBuilder.CreateProcessingLogic<ITestProcessingLogic>(null, "worker", CancellationToken.None));
		}

		[Test]
		public void CreateProcessingLogic_ForWorker_WithNullWorkerId_ThrowsArgumentNullException()
		{
			ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => RemoteProcessingLogicBuilder.CreateProcessingLogic<ITestProcessingLogic>(this._mockRegistryServer.Object, null, CancellationToken.None));
			Assert.That(exception.ParamName, Is.EqualTo("workerId"));
		}

		[Test]
		public void CreateProcessingLogic_ForWorker_WithEmptyWorkerId_ThrowsArgumentNullException()
		{
			ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => RemoteProcessingLogicBuilder.CreateProcessingLogic<ITestProcessingLogic>(this._mockRegistryServer.Object, String.Empty, CancellationToken.None));
			Assert.That(exception.ParamName, Is.EqualTo("workerId"));
		}

		[Test]
		public void CreateProcessingLogic_ForWorker_WithWhitespaceWorkerId_ThrowsArgumentNullException()
		{
			ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => RemoteProcessingLogicBuilder.CreateProcessingLogic<ITestProcessingLogic>(this._mockRegistryServer.Object, "   ", CancellationToken.None));
			Assert.That(exception.ParamName, Is.EqualTo("workerId"));
		}

		[Test]
		public void CreateProcessingLogic_ForWorker_WithValidParameters_ReturnsProxyAndInitializesInvoker()
		{
			String workerId = "worker-123";
			CancellationToken token = this._cancellationTokenSource.Token;

			ITestProcessingLogic proxy = RemoteProcessingLogicBuilder.CreateProcessingLogic<ITestProcessingLogic>(this._mockRegistryServer.Object, workerId, token);

			Assert.IsNotNull(proxy);
			// Avoid IsInstanceOf for remoting proxies (may trigger GetType on proxy and invoke).

			RemoteProcessingWorkerInvoker invoker = this.GetWorkerInvoker(proxy);

			PropertyInfo registerServerProp = typeof(RemoteProcessingLogicInvoker).GetProperty("RegisterServer", BindingFlags.NonPublic | BindingFlags.Instance);
			Object registerServer = registerServerProp?.GetValue(invoker);
			Assert.AreSame(this._mockRegistryServer.Object, registerServer);

			FieldInfo workerIdField = typeof(RemoteProcessingWorkerInvoker).GetField("_workerId", BindingFlags.NonPublic | BindingFlags.Instance);
			String storedWorkerId = (String)workerIdField?.GetValue(invoker);
			Assert.AreEqual(workerId, storedWorkerId);
		}

		private RemoteProcessingLogicInvoker GetLogicInvoker(ITestProcessingLogic proxy)
		{
#if NETFRAMEWORK
			RealProxy realProxy = RemotingServices.GetRealProxy(proxy);
			return (RemoteProcessingLogicInvoker)realProxy;
#else
			return (RemoteProcessingLogicInvoker)(Object)proxy;
#endif
		}

		private RemoteProcessingWorkerInvoker GetWorkerInvoker(ITestProcessingLogic proxy)
		{
#if NETFRAMEWORK
			RealProxy realProxy = RemotingServices.GetRealProxy(proxy);
			return (RemoteProcessingWorkerInvoker)realProxy;
#else
			return (RemoteProcessingWorkerInvoker)(Object)proxy;
#endif
		}
	}
}
