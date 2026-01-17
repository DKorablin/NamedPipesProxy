using System;
using System.Reflection;
using System.Threading;
using AlphaOmega.IO.Interfaces;

#if NETFRAMEWORK
using System.Runtime.Remoting.Proxies;
#endif

namespace AlphaOmega.IO.Reflection
{
	public static class RemoteProcessingLogicBuilder
	{
		/// <summary>
		/// Creates a dynamic proxy for the processing logic interface.
		/// All method calls are converted to RPC messages and sent to workers.
		/// </summary>
		public static T CreateProcessingLogic<T>(IRegistryServer registerServer, CancellationToken token) where T : class
		{
			_ = registerServer ?? throw new ArgumentNullException(nameof(registerServer));

#if NETFRAMEWORK
			var invoker = new RemoteProcessingLogicInvoker(typeof(T));
			invoker.Initialize<T>(registerServer, token);
			return (T)(Object)invoker.GetTransparentProxy();
#else
			var proxy = DispatchProxy.Create<T, RemoteProcessingLogicInvoker>();
			var invoker = (RemoteProcessingLogicInvoker)(Object)proxy;
			invoker.Initialize<T>(registerServer, token);
			return proxy;
#endif
		}

		/// <summary>
		/// Creates a dynamic proxy for the processing logic interface.
		/// All method calls are converted to RPC messages and sent to the specified worker.
		/// </summary>
		public static T CreateProcessingLogic<T>(IRegistryServer registerServer, String workerId, CancellationToken token) where T : class
		{
			_ = registerServer ?? throw new ArgumentNullException(nameof(registerServer));
			if(String.IsNullOrWhiteSpace(workerId))
				throw new ArgumentNullException(nameof(workerId));

#if NETFRAMEWORK
			var invoker = new RemoteProcessingWorkerInvoker(typeof(T));
			invoker.Initialize<T>(registerServer, workerId, token);
			return (T)(Object)invoker.GetTransparentProxy();
#else
			var proxy = DispatchProxy.Create<T, RemoteProcessingWorkerInvoker>();
			var invoker = (RemoteProcessingWorkerInvoker)(Object)proxy;
			invoker.Initialize<T>(registerServer, workerId, token);
			return proxy;
#endif
		}
	}
}