using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlphaOmega.IO.DTOs;
using AlphaOmega.IO.Interfaces;
using AlphaOmega.IO.Reflection;

namespace AlphaOmega.IO
{
	/// <summary>Registry server that manages worker registrations and routes requests via named pipes.</summary>
	public sealed class RegistryServer : IRegistryServer
	{
		internal const String RegistryPipeName = "AlphaOmega.NamedPipes.Registry";

		private readonly IPipeConnectionFactory _connectionFactory;
		private readonly Dictionary<String, ServerSideWorker> _workers = new Dictionary<String, ServerSideWorker>(StringComparer.OrdinalIgnoreCase);
		private readonly ConcurrentDictionary<Guid, IPipeConnection> _activeConnections = new ConcurrentDictionary<Guid, IPipeConnection>();
		private readonly CancellationTokenSource _cts = new CancellationTokenSource();

		/// <inheritdoc/>
		public event Func<PipeMessage, CancellationToken, Task<PipeMessage>> RequestReceived;

		/// <inheritdoc/>
		public event Func<String, Task> WorkerConnected;

		/// <inheritdoc/>
		public event Func<String, Task> WorkerDisconnected;

		/// <summary>Response channel for asynchronous RPC responses.</summary>
		public RpcResponseChannel ResponseChannel { get; } = new RpcResponseChannel();

		/// <inheritdoc/>
		public Boolean IsStarted { get; private set; }

		/// <inheritdoc/>
		public String PipeName { get; }

		/// <inheritdoc/>
		IEnumerable<String> IRegistryServer.ConnectedWorkerIDs => this._workers.Keys;

		/// <summary>Initializes a new instance of the registry server with the default pipe name.</summary>
		public RegistryServer()
			: this(RegistryPipeName) { }

		/// <summary>Initializes a new instance of the registry server with a custom pipe name.</summary>
		/// <param name="registryPipeName">Pipe name used by the server.</param>
		public RegistryServer(String registryPipeName)
			: this(registryPipeName, new ServerSideConnection.ServerSideConnectionFactory())
		{
		}

		internal RegistryServer(String registryPipeName, IPipeConnectionFactory connectionFactory)
		{
			if(String.IsNullOrWhiteSpace(registryPipeName))
				throw new ArgumentNullException(nameof(registryPipeName));

			this.PipeName = registryPipeName;
			this._connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
		}

		/// <inheritdoc/>
		public async Task StartAsync(CancellationToken token)
		{
			using(var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(this._cts.Token, token))
			{
				var linkedToken = linkedCts.Token;

				try
				{
					this.IsStarted = true;
					while(!linkedToken.IsCancellationRequested)
					{
						IPipeConnection connection = null;

						try
						{
							connection = await this._connectionFactory.CreateServerAsync(this.PipeName, linkedToken);
							_ = this.ListenConnectionAsync(connection, linkedToken);
						} catch(OperationCanceledException)
						{
							connection?.Dispose();
							break;
						} catch(Exception exc)
						{
							connection?.Dispose();
							TraceLogic.TraceSource.TraceData(TraceEventType.Error, 9, exc);
						}
					}
				} finally
				{
					this.IsStarted = false;
				}
			}
		}

		/// <inheritdoc/>
		public T CreateProcessingLogic<T>() where T : class
			=> RemoteProcessingLogicBuilder.CreateProcessingLogic<T>(this, this._cts.Token);

		/// <inheritdoc/>
		public T CreateProcessingLogic<T>(String workerId) where T : class
			=> RemoteProcessingLogicBuilder.CreateProcessingLogic<T>(this, workerId, this._cts.Token);

		/// <summary>Listens to a connection, processes messages, and handles registration lifecycle.</summary>
		/// <param name="connection">Server-side connection.</param>
		/// <param name="token">Cancellation token.</param>
		private async Task ListenConnectionAsync(IPipeConnection connection, CancellationToken token)
		{
			try
			{
				this._activeConnections.TryAdd(connection.ConnectionId, connection);

				// First message must be RegisterWorker
				PipeMessage firstMessage = await PipeMessage.ReadFromStream(connection.Pipe, token);
				if(firstMessage.Type != PipeMessageType.RegisterWorker.ToString())
					throw new InvalidOperationException($"Expected {PipeMessageType.RegisterWorker}, got {firstMessage.Type}");

				var registerPayload = firstMessage.Deserialize<RegisterWorkerRequest>();
				var worker = new ServerSideWorker(registerPayload.WorkerId, registerPayload.PipeName, connection.ConnectionId);

				var listenTask = connection.ListenLoopAsync(HandleConnectionMessageAsync, token);

				await this.RegisterWorker(worker);

				await listenTask;
			} catch(EndOfStreamException)
			{
				TraceLogic.TraceSource.TraceInformation("Connection {0} closed", connection.ConnectionId);
			} catch(OperationCanceledException)
			{
				// Server shutting down
			} catch(Exception exc)
			{
				TraceLogic.TraceSource.TraceEvent(TraceEventType.Error, 9, "Error in connection {0}: {1}", connection.ConnectionId, exc);
			} finally
			{
				// Unregister any worker on this connection
				var workerToRemove = this._workers.Values.FirstOrDefault(w => w.ConnectionId == connection.ConnectionId);
				if(workerToRemove != null)
					await this.UnregisterWorker(workerToRemove);

				this._activeConnections.TryRemove(connection.ConnectionId, out _);
				connection.Dispose();
			}
		}

		/// <summary>Handles an incoming message from a connection and optionally returns a response.</summary>
		/// <param name="message">Incoming request or response message.</param>
		/// <param name="token">Cancellation token.</param>
		/// <returns>Response message or null for responses.</returns>
		private async Task<PipeMessage> HandleConnectionMessageAsync(PipeMessage message, CancellationToken token)
		{
			if(this.ResponseChannel.CompleteResponse(message, message))
				return null; // Don't send response to a response.

			return this.RequestReceived != null
				? await this.RequestReceived.Invoke(message, token)
				: null;
		}

		/// <inheritdoc/>
		public async Task SendRequestToWorkers(PipeMessage request, CancellationToken token)
		{
			ServerSideWorker[] workers = this._workers.Values.ToArray();
			foreach(ServerSideWorker worker in workers)
				await this.SendRequestToWorker(worker.WorkerId, request, token);
		}

		/// <inheritdoc/>
		public async Task<PipeMessage> SendRequestToWorker(String workerId, PipeMessage request, CancellationToken token)
		{
			if(!this._workers.TryGetValue(workerId, out var worker))
				throw new InvalidOperationException($"Worker {workerId} is not registered.");

			if(!this._activeConnections.TryGetValue(worker.ConnectionId, out var connection))
				throw new IOException($"Connection for worker {workerId} is no longer active.");

			Task<PipeMessage> responseTask = this.ResponseChannel.WaitForResponseAsync(request, TimeSpan.FromSeconds(30));

			try
			{
				await connection.SendMessageAsync(request, token);
			} catch(EndOfStreamException)
			{
				await this.UnregisterWorker(worker);
				this.ResponseChannel.FailResponse(request, new IOException("Failed to send to worker"));
				return null;
			}

			return await responseTask;
		}

		private async Task RegisterWorker(ServerSideWorker worker)
		{
			this._workers[worker.WorkerId] = worker;
			TraceLogic.TraceSource.TraceInformation("Registering worker {0} at pipe {1}. Total: {2:N0}", worker.WorkerId, worker.WorkerPipeName, this._workers.Count);

			if(this.WorkerConnected != null)
				await this.WorkerConnected.Invoke(worker.WorkerId);
		}

		private async Task UnregisterWorker(ServerSideWorker worker)
		{
			this._workers.Remove(worker.WorkerId);
			TraceLogic.TraceSource.TraceInformation("Unregistering worker {0} at pipe {1}. Total: {2:N0}", worker.WorkerId, worker.WorkerPipeName, this._workers.Count);

			if(this.WorkerDisconnected != null)
				await this.WorkerDisconnected.Invoke(worker.WorkerId);
		}

		/// <inheritdoc/>
		public async Task StopAsync()
		{
			if(!this._cts.IsCancellationRequested)
				this._cts.Cancel();

			var closeTimeout = Task.Delay(TimeSpan.FromSeconds(5));
			var allClosed = Task.Run(() =>
			{
				while(this._activeConnections.Count > 0)
					Thread.Sleep(10);
			});

			await Task.WhenAny(allClosed, closeTimeout);

			// Forcefully close any remaining connections
			foreach(var connection in this._activeConnections.Values.ToArray())
				connection.Dispose();

			this._activeConnections.Clear();
		}

		/// <summary>Disposes server resources and cancels operations.</summary>
		public void Dispose()
		{
			GC.SuppressFinalize(this);

			this._cts.Cancel();
			this._cts.Dispose();
		}
	}
}