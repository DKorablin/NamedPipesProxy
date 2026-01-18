using System;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AlphaOmega.IO.DTOs;
using AlphaOmega.IO.Interfaces;

namespace AlphaOmega.IO
{
	/// <summary>Represents a worker server that connects to a registry and handles named pipe communication for method invocation.</summary>
	/// <remarks>
	/// This sealed class manages a dedicated named pipe for receiving requests from clients, registering with a registry server,
	/// and invoking methods on a worker logic object via reflection. It handles asynchronous communication and graceful shutdown.
	/// </remarks>
	public sealed class WorkerServer : PipeServerBase, IWorkerServer
	{
		private ServerSideConnection _connection;
		private CancellationTokenSource _cts;
		private readonly Object _workerLogic;
		private Task _listenTask;
		private Boolean _cleanedUp = false;

		/// <inheritdoc/>
		public event Func<PipeMessage, CancellationToken, Task<PipeMessage>> RequestReceived;

		/// <inheritdoc/>
		public event Func<Task> ConnectionLost;

		/// <inheritdoc/>
		public String RegistryPipeName { get; }

		/// <inheritdoc/>
		public String WorkerId { get; }

		/// <inheritdoc/>
		public String PipeName { get; }

		/// <summary>Gets a value indicating whether the worker server has been successfully started and registered with the registry.</summary>
		/// <value><c>true</c> if the server is started; otherwise, <c>false</c>.</value>
		public Boolean IsStarted { get; private set; }

		/// <summary>Initializes a new instance of the <see cref="WorkerServer"/> class with a worker logic object using default registry and pipe names.</summary>
		/// <param name="workerLogic">The object containing the methods to invoke when messages are received. Must not be <c>null</c>.</param>
		/// <exception cref="ArgumentNullException">Thrown if <paramref name="workerLogic"/> is <c>null</c>.</exception>
		public WorkerServer(Object workerLogic)
			: this(RegistryServer.RegistryPipeName, "AlphaOmega.NamedPipes.Worker.", Guid.NewGuid().ToString("N"), workerLogic)
		{
		}

		/// <summary>Initializes a new instance of the <see cref="WorkerServer"/> class with specified registry, pipe, and worker identifiers.</summary>
		/// <param name="registryPipeName">The name of the registry server pipe. Must not be <c>null</c> or whitespace.</param>
		/// <param name="workerPipeName">The prefix for the worker's unique pipe name. Must not be <c>null</c> or whitespace.</param>
		/// <param name="workerId">The unique identifier for this worker instance. Must not be <c>null</c> or whitespace.</param>
		/// <param name="workerLogic">The object containing the methods to invoke when messages are received. Must not be <c>null</c>.</param>
		/// <exception cref="ArgumentNullException">Thrown if any parameter is <c>null</c>, empty, or contains only whitespace.</exception>
		public WorkerServer(String registryPipeName, String workerPipeName, String workerId, Object workerLogic)
		{
			if(String.IsNullOrWhiteSpace(registryPipeName))
				throw new ArgumentNullException(nameof(registryPipeName));
			if(String.IsNullOrWhiteSpace(workerId))
				throw new ArgumentNullException(nameof(workerId));
			if(String.IsNullOrWhiteSpace(workerPipeName))
				throw new ArgumentNullException(nameof(workerPipeName));

			this.RegistryPipeName = registryPipeName ?? throw new ArgumentNullException(nameof(registryPipeName));
			this.WorkerId = workerId ?? throw new ArgumentNullException(nameof(workerId));
			this.PipeName = workerPipeName + this.WorkerId;

			this._workerLogic = workerLogic ?? throw new ArgumentNullException(nameof(workerLogic));
		}

		/// <summary>Starts the worker server asynchronously by registering with the registry and beginning to listen for client requests.</summary>
		/// <param name="token">A cancellation token that can be used to cancel the startup operation.</param>
		/// <returns>A task representing the asynchronous startup operation.</returns>
		/// <remarks>
		/// This method registers the worker with the registry server using the configured pipe names and identifiers,
		/// then starts listening for incoming client requests on the dedicated named pipe.
		/// </remarks>
		/// <inheritdoc/>
		public async Task StartAsync(CancellationToken token)
		{
			if(this._cts == null || this._cts.IsCancellationRequested)
				this._cts = new CancellationTokenSource();

			this._cleanedUp = false;
			CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(this._cts.Token, token);

			await this.RegisterWorkerAsync(linkedCts.Token);
			this._listenTask = Task.Run(() => this.ListenAsync(linkedCts), linkedCts.Token);
		}

		private async Task RegisterWorkerAsync(CancellationToken token)
		{
			var registryPipe = new NamedPipeClientStream(".", this.RegistryPipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

			TraceLogic.TraceSource.TraceInformation("Connecting to registry...");
			await registryPipe.ConnectAsync(5000, token);

			this._connection = new ServerSideConnection(registryPipe);

			TraceLogic.TraceSource.TraceInformation("Registering with registry...");
			await SendMessageAsync(this._connection,
				new PipeMessage(PipeMessageType.RegisterWorker.ToString(), new RegisterWorkerRequest(this.WorkerId, this.PipeName)),
				token);

			TraceLogic.TraceSource.TraceInformation("Connected to registry");
			this.IsStarted = true;
		}

		private async Task ListenAsync(CancellationTokenSource linkedCtsToken)
		{
			try
			{
				await this.ListenLoopAsync(this._connection, this.HandleMessageAsync, linkedCtsToken.Token);
			}
			catch(OperationCanceledException)
			{
				// Expected on shutdown
			} catch(IOException exc)
			{
				TraceLogic.TraceSource.TraceData(System.Diagnostics.TraceEventType.Error, 9, exc);
				this._cts.Cancel();
			}
			catch(Exception exc)
			{
				TraceLogic.TraceSource.TraceData(System.Diagnostics.TraceEventType.Error, 9, exc);
			}
			finally
			{
				await this.CleanupAsync();
				linkedCtsToken.Dispose();
			}
		}

		private async Task<PipeMessage> HandleMessageAsync(PipeMessage message, CancellationToken token)
		{
			try
			{
				var result = await this.InvokeMethodAsync(message, token);

				return result == null
					? new PipeMessage(message, PipeMessageType.Null.ToString(), NullResponse.Instance)
					: result;
			}catch(InvalidOperationException exc)
			{
				return new PipeMessage(message, PipeMessageType.Error.ToString(), new ErrorResponse(exc.Message));
			}
			catch(Exception exc)
			{
				exc.Data.Add(nameof(message.Type), message.Type);
				TraceLogic.TraceSource.TraceData(System.Diagnostics.TraceEventType.Error, 9, exc);
				return new PipeMessage(message, PipeMessageType.Error.ToString(), new ErrorResponse(exc.Message));
			}
		}

		/// <summary>Stops the worker server asynchronously by canceling ongoing operations, closing pipes, and cleaning up resources.</summary>
		/// <returns>A task representing the asynchronous stop operation.</returns>
		/// <remarks>
		/// This method gracefully shuts down the server by canceling the listening task and disposing of the cancellation token source.
		/// If the server is not started, this method returns immediately without performing any operations.
		/// </remarks>
		public async Task StopAsync()
		{
			if(!this.IsStarted) return;

			if(this._cts?.IsCancellationRequested == false)
				this._cts?.Cancel();

			if(this._listenTask != null)
				await Task.WhenAny(this._listenTask, Task.Delay(TimeSpan.FromSeconds(2)));

			if(this._cts != null)
			{
				this._cts.Dispose();
				this._cts = null;
			}
		}

		private async Task CleanupAsync()
		{
			if(this._cleanedUp)
				return;//We don't need to invoke ConnectionLost event multiple times

			this._cleanedUp = true;

			this._connection?.Dispose();
			this._connection = null;

			// Notify subscribers
			if(this.ConnectionLost != null)
				await this.ConnectionLost.Invoke();

			this.IsStarted = false;
			TraceLogic.TraceSource.TraceEvent(System.Diagnostics.TraceEventType.Stop, 1, "Worker server fully stopped and cleaned up.");
		}

		/// <summary>Attempts to handle an incoming request using the registered <see cref="RequestReceived"/> event handler.</summary>
		/// <param name="request">The incoming request message to handle.</param>
		/// <param name="token">A cancellation token that can be used to cancel the handling operation.</param>
		/// <returns>A task that represents the asynchronous operation and returns the response message, or <c>null</c> if no handler is registered.</returns>
		/// <remarks>
		/// This method invokes the <see cref="RequestReceived"/> event if a subscriber is registered.
		/// It allows custom handling of requests before standard method invocation processing occurs.
		/// </remarks>
		public async Task<PipeMessage> TryHandleAsync(PipeMessage request, CancellationToken token)
		{
			return this.RequestReceived == null
				? null
				: await this.RequestReceived.Invoke(request, token);
		}

		private async Task<PipeMessage> InvokeMethodAsync(PipeMessage message, CancellationToken token)
		{
			String methodName = message.Type;
			MethodInfo method = this._workerLogic.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.IgnoreCase | BindingFlags.Instance)
				?? throw new InvalidOperationException($"Method {methodName} not found in the {this._workerLogic.GetType()}");

			ParameterInfo[] parameters = method.GetParameters();
			Type[] requestTypes = Array.ConvertAll(parameters, p => p.ParameterType);
			Object[] requestPayload = message.Deserialize(requestTypes);

			Object resultValue = method.Invoke(this._workerLogic, requestPayload);
			if(resultValue is Task task)
			{
				await task;

				var returnType = method.ReturnType;
				if(returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
				{
					var property = task.GetType().GetProperty(nameof(Task<Object>.Result));
					Object result = property.GetValue(task);
					return new PipeMessage(message, method.Name, result);
				}

				// Task without result
				return null;
			} else if(resultValue != null)
				return new PipeMessage(message, method.Name, resultValue);

			return null;
		}

		/// <summary>Releases all resources used by the <see cref="WorkerServer"/> instance.</summary>
		/// <remarks>
		/// This method cancels ongoing operations, disposes the cancellation token source, and closes the named pipe connection.
		/// Cleanup operations are performed asynchronously on a background task.
		/// </remarks>
		public void Dispose()
		{
			_ = Task.Run(this.CleanupAsync);

			if(this._cts != null)
			{
				this._cts.Cancel();
				this._cts.Dispose();
				this._cts = null;
			}
			this._connection?.Dispose();
		}
	}
}