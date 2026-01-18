using System.Reflection;
using AlphaOmega.IO.DTOs;
using AlphaOmega.IO.Interfaces;
using System;
using System.Threading;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

#if NETFRAMEWORK
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using ProxyBaseClass = System.Runtime.Remoting.Proxies.RealProxy;
#else
using ProxyBaseClass = System.Reflection.DispatchProxy;
#endif

namespace AlphaOmega.IO.Reflection
{
	/// <summary>Handles method interception and routes calls to all connected workers, using RealProxy for .NET Framework or DispatchProxy for .NET Standard.</summary>
	public class RemoteProcessingLogicInvoker : ProxyBaseClass
	{
		private Type _interfaceType;
		private readonly Dictionary<String, MethodInfo> _methodsCache = new Dictionary<String, MethodInfo>();

		/// <summary>Gets the registry server managing worker connections.</summary>
		protected IRegistryServer RegisterServer { get; private set; }

		/// <summary>Gets the cancellation token for the proxy lifecycle.</summary>
		protected CancellationToken CancellationToken { get; private set; }

		/// <summary>Initializes a new instance of the <see cref="RemoteProcessingLogicInvoker"/> class for DispatchProxy usage.</summary>
		public RemoteProcessingLogicInvoker() { }

		/// <summary>Initializes a new instance of the <see cref="RemoteProcessingLogicInvoker"/> class with the specified interface type for RealProxy usage.</summary>
		/// <param name="interfaceType">The interface type to proxy.</param>
		/// <exception cref="ArgumentNullException">Thrown when <paramref name="interfaceType"/> is null.</exception>
		/// <exception cref="ArgumentException">Thrown when <paramref name="interfaceType"/> is not an interface.</exception>
		public RemoteProcessingLogicInvoker(Type interfaceType)
#if NETFRAMEWORK
			: base(interfaceType)
#endif
		{
			this._interfaceType = interfaceType ?? throw new ArgumentNullException(nameof(interfaceType));

			if(!interfaceType.IsInterface)
				throw new ArgumentException("Type must be an interface", nameof(interfaceType));
		}

#if NETFRAMEWORK
		/// <summary>Intercepts method calls on the proxy for .NET Framework using RealProxy.</summary>
		/// <param name="msg">The message containing method call information.</param>
		/// <returns>A return message containing the result or error.</returns>
		public override IMessage Invoke(IMessage msg)
		{
			var methodCall = msg as IMethodCallMessage;
			if(methodCall == null)
				return new ReturnMessage(null, null, 0, methodCall?.LogicalCallContext, methodCall);

			var method = methodCall.MethodBase as MethodInfo;
			if(method == null)
				return new ReturnMessage(null, null, 0, methodCall.LogicalCallContext, methodCall);

			try
			{
				Object result = this.InvokeImpl(method, methodCall.Args);
				return new ReturnMessage(result, null, 0, methodCall.LogicalCallContext, methodCall);
			} catch(Exception ex)
			{
				Console.WriteLine($"Error in RPC proxy invoke: {ex.Message}");
				return new ReturnMessage(ex, methodCall);
			}
		}
#else
		/// <summary>Intercepts method calls on the proxy for .NET Standard using DispatchProxy.</summary>
		/// <param name="targetMethod">The method being invoked.</param>
		/// <param name="args">The arguments passed to the method.</param>
		/// <returns>The result of the method invocation.</returns>
		protected override Object Invoke(MethodInfo targetMethod, Object[] args)
			=> this.InvokeImpl(targetMethod, args);
#endif

		/// <summary>Initializes the proxy with registry server, cancellation token, and caches interface methods.</summary>
		/// <typeparam name="T">The processing logic interface type.</typeparam>
		/// <param name="registerServer">The registry server managing worker connections.</param>
		/// <param name="cancellationToken">Cancellation token for the proxy lifecycle.</param>
		/// <exception cref="InvalidOperationException">Thrown when <typeparamref name="T"/> is not an interface.</exception>
		/// <exception cref="ArgumentNullException">Thrown when <paramref name="registerServer"/> is null.</exception>
		public void Initialize<T>(IRegistryServer registerServer, CancellationToken cancellationToken) where T : class
		{
			this._interfaceType = typeof(T);
			if(!this._interfaceType.IsInterface)
				throw new InvalidOperationException($"Type {typeof(T)} must be an interface");

			this.RegisterServer = registerServer ?? throw new ArgumentNullException(nameof(registerServer));
			this.CancellationToken = cancellationToken;

			this._methodsCache.Clear();
			foreach(var method in this._interfaceType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
				this._methodsCache[method.Name] = method;
		}

		/// <summary>Implements the method invocation logic by creating an RPC message, sending it to all workers, and handling the response.</summary>
		/// <param name="method">The method being invoked.</param>
		/// <param name="args">The arguments passed to the method.</param>
		/// <returns>The result of the method invocation, which may be a Task, Task&lt;T&gt;, or a direct value.</returns>
		/// <exception cref="InvalidOperationException">Thrown when the proxy is not properly initialized.</exception>
		private Object InvokeImpl(MethodInfo method, Object[] args)
		{
			if(method == null || this.RegisterServer == null)
				throw new InvalidOperationException("Proxy not properly initialized");

			PipeMessage request = new PipeMessage(method.Name, args);

			TraceLogic.TraceSource.TraceInformation("[RPC Proxy] Sending request: {0}", request);

			Type returnType = method.ReturnType;
			Boolean isTask = returnType == typeof(Task);
			Boolean isGenericTask = returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>);

			Type responseType;
			if(isGenericTask)
				responseType = returnType.GetGenericArguments()[0];
			else if(isTask)
				responseType = typeof(Object);
			else
				responseType = returnType;

			Task<Object> rawResponse = this.SendRequestAndGetResponseAsync(request, responseType);

			if(isTask)
				return rawResponse;
			else if(isGenericTask)
				return _castTaskMethod
					.MakeGenericMethod(responseType)
					.Invoke(null, new Object[] { rawResponse });
			else
			{
				var result = rawResponse.GetAwaiter().GetResult();
				return result;
			}
		}

		/// <summary>Sends an RPC request to all connected workers and returns the first non-null, non-error response.</summary>
		/// <param name="request">The RPC request message.</param>
		/// <param name="responseType">The expected response type.</param>
		/// <returns>The deserialized response from the first worker that provides a valid result, or null if all workers return null.</returns>
		/// <exception cref="InvalidOperationException">Thrown when no workers are connected or when a worker returns an error response.</exception>
		protected virtual async Task<Object> SendRequestAndGetResponseAsync(PipeMessage request, Type responseType)
		{
			var workerIds = this.RegisterServer.ConnectedWorkerIDs.ToArray();
			if(workerIds.Length == 0)
				throw new InvalidOperationException("No workers connected to handle the request.");

			var pendingTasks = new List<Task<PipeMessage>>();

			foreach(var workerId in this.RegisterServer.ConnectedWorkerIDs)
			{
				PipeMessage workerRequest = new PipeMessage(request);
				Task<PipeMessage> task = this.RegisterServer.SendRequestToWorker(workerId, workerRequest, this.CancellationToken);
				pendingTasks.Add(task);
			}

			while(pendingTasks.Count > 0)
			{
				Task<PipeMessage> completedTask = await Task.WhenAny(pendingTasks);
				pendingTasks.Remove(completedTask);

				PipeMessage response = await completedTask;
				if(response.Type == PipeMessageType.Error.ToString())
				{
					var error = response.Deserialize<ErrorResponse>();
					throw new InvalidOperationException(error.Message);
				}

				if(response.Type != PipeMessageType.Null.ToString())
					return response.Deserialize(responseType);
			}

			return null;//All workers returns null
		}

		private static readonly MethodInfo _castTaskMethod = typeof(RemoteProcessingLogicInvoker).GetMethod(nameof(CastTask), BindingFlags.NonPublic | BindingFlags.Static);

		/// <summary>Casts a Task&lt;Object&gt; to a Task&lt;T&gt; to match the expected return type.</summary>
		/// <typeparam name="T">The target type for the task result.</typeparam>
		/// <param name="task">The task returning an Object.</param>
		/// <returns>A task returning the casted result.</returns>
		private static async Task<T> CastTask<T>(Task<Object> task)
		{
			var result = await task;
			return (T)result;
		}
	}
}