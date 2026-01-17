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
	/// <summary>Internal invoker that handles the actual method interception and message routing using DispatchProxy for .NET 5+.</summary>
	public class RemoteProcessingLogicInvoker : ProxyBaseClass
	{
		private Type _interfaceType;
		private readonly Dictionary<String, MethodInfo> _methodsCache = new Dictionary<String, MethodInfo>();

		protected IRegistryServer RegisterServer { get; private set; }

		protected CancellationToken CancellationToken { get; private set; }

		public RemoteProcessingLogicInvoker() { }

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
		protected override Object Invoke(MethodInfo targetMethod, Object[] args)
			=> this.InvokeImpl(targetMethod, args);
#endif

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

		private Object InvokeImpl(MethodInfo method, Object[] args)
		{
			if(method == null || this.RegisterServer == null)
				throw new InvalidOperationException("Proxy not properly initialized");

			PipeMessage request = new PipeMessage(method.Name, args);

			PipeServerBase.TraceSource.TraceInformation("[RPC Proxy] Sending request: {0}", request);

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

		/// <summary>Sends a request and waits for response.</summary>
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

		private static async Task<T> CastTask<T>(Task<Object> task)
		{
			var result = await task;
			return (T)result;
		}
	}
}