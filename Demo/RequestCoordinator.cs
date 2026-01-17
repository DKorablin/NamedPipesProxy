using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlphaOmega.IO.DTOs;
using AlphaOmega.IO.Interfaces;
using Demo.DTOs;

namespace Demo
{
	public sealed class RequestCoordinator
	{
		private readonly IRegistryServer _registerServer;
		private CancellationTokenSource _heartbeatCts;

		public RequestCoordinator(IRegistryServer registerServer)
			=> this._registerServer = registerServer ?? throw new ArgumentNullException(nameof(registerServer));

		public async Task StartAsync(CancellationToken token)
		{
			this._registerServer.RequestReceived += (request, ct) => HandleClientRequestAsync(request, ct);
			this._registerServer.WorkerConnected += (workerId) => HandleWorkerConnected(workerId);

			// Start heartbeat loop
			this._heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(token);
			_ = Task.Run(() => SendHeartbeatLoopAsync(this._heartbeatCts.Token), token);
		}

		private async Task HandleWorkerConnected(String workerId)
		{
			var workerLogic = this._registerServer.CreateProcessingLogic<IWorkerLogic>(workerId);
			var request = new GetPidRequest();
			var response = await workerLogic.GetPid2(123, "ExtraInfo");
			Console.WriteLine($"[Worker] Id={workerId} Pid={response.Pid}");
		}

		public Task StopAsync()
		{
			this._heartbeatCts?.Cancel();
			this._heartbeatCts?.Dispose();
			return Task.CompletedTask;
		}

		/// <summary>Sends heartbeat messages to all registered workers every 5 seconds.</summary>
		private async Task SendHeartbeatLoopAsync(CancellationToken token)
		{
			const Int32 HeartbeatIntervalMs = 1000;

			try
			{
				IWorkerLogic processingLogic = null;
				while(!token.IsCancellationRequested)
				{
					try
					{
						await Task.Delay(HeartbeatIntervalMs, token);

						if(this._registerServer.ConnectedWorkerIDs.Any())
						{
							var request = new HeartbeatRequest();
							Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sending heartbeat to workers");

							if(processingLogic == null)
								processingLogic = this._registerServer.CreateProcessingLogic<IWorkerLogic>();

							await processingLogic.Heartbeat(request);
						}
					} catch(OperationCanceledException)
					{
						break;
					} catch(Exception ex)
					{
						Console.WriteLine($"Error sending heartbeat: {ex}");
					}
				}
			} catch(Exception ex)
			{
				Console.WriteLine($"Heartbeat loop error: {ex}");
			}
		}

		private async Task<PipeMessage> HandleClientRequestAsync(PipeMessage request, CancellationToken token)
		{//TODO: Add real message processing here. From TCP connection and from workers.
			Console.WriteLine($"WARNING: This is a bad logic and should not been used! Message:{request.ToString()}");
			return null;

			/*PipeMessage response = TryHandleLocally(request);
			if(response != null)
				return response;

			var responseTask = this._registerServer.ResponseChannel.WaitForResponseAsync(request, TimeSpan.FromSeconds(30));

			await this._registerServer.SendRequestToWorkers(request, true, token);

			try
			{
				return await responseTask;
			} catch(TimeoutException ex)
			{
				String errorMessage = $"Request {request.RequestId} timed out: {ex.Message}";
				Console.WriteLine(errorMessage);
				return new PipeMessage(request, PipeMessageType.Error.ToString(), new ErrorResponse(errorMessage));
			}*/
		}
	}
}