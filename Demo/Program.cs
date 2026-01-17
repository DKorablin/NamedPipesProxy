using System;
using System.Threading;
using System.Threading.Tasks;

namespace Demo
{
	internal class Program
	{
		private async static Task Main(String[] args)
		{
			using(var cts = new CancellationTokenSource())
			{
				Console.CancelKeyPress += (_, e) =>
				{
					e.Cancel = true;
					cts.Cancel();
					Console.WriteLine("Shutdown requested...");
				};

				var roleManager = new ProcessRoleManager(cts.Token);
				await roleManager.RunAsync();
			}

			/*if(RoleDetector.TryBecomeRegistry(out var tcpListener))
			{
				Console.WriteLine("Started as Registry");

				var registerServer = new RegistryServer("ipc.registry");
				registerServer.ClientRequestReceived += async (request, token) =>
				{
					// Coordinator logic will live here later
					Console.WriteLine($"Request: {request.Type}");
				};

				await registerServer.StartAsync(cts.Token);

				//await RegistryServer.RunAsync(cts.Token);
				tcpListener!.Stop();
			} else
			{
				Int32 pid = Environment.ProcessId;
				Console.WriteLine($"Started as Worker PID: {pid}");

				var worker = new WorkerServer(
					workerId: $"worker-{pid}",
					pipeName: $"worker_pipe_{pid}");

				worker.RequestReceived += async (msg, token) =>
				{
					switch(msg.Type)
					{
					case PipeMessageType.GetPidRequest:
						return new PipeMessage(msg.RequestId, new GetPidResponsePayload(Environment.ProcessId));
					default:
						return null;
					}
				};

				await worker.StartAsync(cts.Token);
				//await WorkerServer.RunAsync(cts.Token);
			}*/
		}
	}
}