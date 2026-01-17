using System;

namespace Demo.DTOs
{
	public sealed class HeartbeatRequest
	{
		public DateTime Timestamp { get; set; }

		public HeartbeatRequest()
		{
			this.Timestamp = DateTime.UtcNow;
		}
	}
}