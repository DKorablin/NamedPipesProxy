using System;
using System.Reflection;
using System.Threading;
using AlphaOmega.IO.Interfaces;

#if NETFRAMEWORK
using System.Runtime.Remoting.Proxies;
#endif

namespace AlphaOmega.IO.Reflection
{
	/// <summary>Factory for creating dynamic proxies that convert method calls into RPC messages for remote worker invocation.</summary>
	public static class RemoteProcessingLogicBuilder
	{
		/// <summary>Creates a dynamic proxy for the processing logic interface. All method calls are converted to RPC messages and sent to workers.</summary>
		/// <typeparam name="T">The processing logic interface type to proxy.</typeparam>
		/// <param name="registerServer">The registry server managing worker connections.</param>
		/// <param name="token">Cancellation token for the proxy lifecycle.</param>
		/// <returns>A proxy instance that forwards method calls to all connected workers.</returns>
		/// <exception cref="ArgumentNullException">Thrown when <paramref name="registerServer"/> is null.</exception>
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

		/// <summary>Creates a dynamic proxy for the processing logic interface. All method calls are converted to RPC messages and sent to the specified worker.</summary>
		/// <typeparam name="T">The processing logic interface type to proxy.</typeparam>
		/// <param name="registerServer">The registry server managing worker connections.</param>
		/// <param name="workerId">The unique identifier of the target worker.</param>
		/// <param name="token">Cancellation token for the proxy lifecycle.</param>
		/// <returns>A proxy instance that forwards method calls to the specified worker.</returns>
		/// <exception cref="ArgumentNullException">Thrown when <paramref name="registerServer"/> is null or <paramref name="workerId"/> is null or whitespace.</exception>
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