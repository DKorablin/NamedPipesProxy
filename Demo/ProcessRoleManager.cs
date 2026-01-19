using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using AlphaOmega.IO;
using Demo.DTOs;

namespace Demo
{
	public sealed class ProcessRoleManager
	{
		private readonly CancellationToken _token;
		private TcpListener _leaderListener;

		public ProcessRoleManager(CancellationToken token)
			=> this._token = token;

		public async Task RunAsync()
		{
			while(!this._token.IsCancellationRequested)
			{
				Console.WriteLine("Detecting role...");

				if(RoleDetector.TryBecomeRegistry(out _leaderListener))
					await RunAsRegistryAsync(this._token);
				else
					await RunAsWorkerAsync(this._token);
			}
		}

		private async Task RunAsRegistryAsync(CancellationToken token)
		{
			Console.WriteLine($"ROLE = REGISTRY ({Process.GetCurrentProcess().Id})");

			var registerServer = new RegistryServer();

			var coordinator = new RequestCoordinator(registerServer);

			await coordinator.StartAsync(token);

			Task delayTask = Task.Delay(Timeout.Infinite, token);
			Task disconnectTask = registerServer.StartAsync(token);
			Task completedTask = await Task.WhenAny(delayTask, disconnectTask);

			if(completedTask == delayTask)
				Console.WriteLine("Registry shutting down");
			else
				Console.WriteLine("Registry server failed");
			await registerServer.StopAsync();
		}

		private Task WaitForWorkerDisconnectAsync(WorkerServer workerServer, CancellationToken token)
		{
			TaskCompletionSource<Object> tcs = new TaskCompletionSource<Object>();
			Func<Task> handler = null;
			handler = () =>
			{
				workerServer.ConnectionLost -= handler;
				tcs.TrySetResult(null);
				return Task.CompletedTask;
			};
			workerServer.ConnectionLost += handler;
			token.Register(() => tcs.TrySetCanceled());
			return tcs.Task;
		}

		private async Task RunAsWorkerAsync(CancellationToken token)
		{
			Console.WriteLine($"ROLE = WORKER ({Process.GetCurrentProcess().Id})");

			WorkerLogicImpl workerLogic = new WorkerLogicImpl(Process.GetCurrentProcess().Id.ToString());
			WorkerServer workerServer = new WorkerServer(workerLogic);

			try
			{
				Console.WriteLine("Starting worker server...");
				await workerServer.StartAsync(token);

				Task delayTask = Task.Delay(Timeout.Infinite, token);
				Task disconnectTask = this.WaitForWorkerDisconnectAsync(workerServer, token);
				Task completedTask = await Task.WhenAny(delayTask, disconnectTask);

				if(completedTask == delayTask)
					Console.WriteLine("Worker shutdown requested");
				else
					Console.WriteLine("Worker server disconnected");
			}
			finally
			{
				await workerServer.StopAsync();
			}
		}

		public class WorkerLogicImpl : IWorkerLogic
		{
			private readonly String _workerId;
			public WorkerLogicImpl(String workerId)
				=> this._workerId = workerId;
			public Task Heartbeat(HeartbeatRequest request)
			{
				Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Received heartbeat from worker {this._workerId} (timestamp: {request.Timestamp:HH:mm:ss})");
				return Task.CompletedTask;
			}
			public Task<GetPidResponse> GetPid(GetPidRequest request)
			{
				var pid = Process.GetCurrentProcess().Id;
				Console.WriteLine($"GetPid called on worker {this._workerId}, returning PID {pid}");
				return Task.FromResult(new GetPidResponse(pid));
			}

			public Task<GetPidResponse> GetPid2(Int32 currentProcessId, String additionalInfo)
			{
				var pid = Process.GetCurrentProcess().Id;
				Console.WriteLine($"GetPid2 called on worker {this._workerId} from process {currentProcessId}, additional info: {additionalInfo}, returning PID {pid}");
				return Task.FromResult(new GetPidResponse(pid));
			}
		}
	}
}