using System;

namespace Demo.DTOs
{
	public sealed class GetPidResponse
	{
		public Int32 Pid { get; set; } = 0;

		public GetPidResponse(Int32 pid)
			=> this.Pid = pid;
	}
}