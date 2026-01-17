using System;
using System.Threading.Tasks;
using Demo.DTOs;

namespace Demo
{
	/// <summary>
	/// Interface defining all RPC methods that can be called on workers.
	/// This interface is used to create a dynamic proxy that intercepts calls
	/// and converts them to messages sent via named pipes.
	/// </summary>
	public interface IWorkerLogic
	{
		Task Heartbeat(HeartbeatRequest request);

		Task<GetPidResponse> GetPid(GetPidRequest request);

		Task<GetPidResponse> GetPid2(Int32 currentProcessId, String additionalInfo);
	}
}