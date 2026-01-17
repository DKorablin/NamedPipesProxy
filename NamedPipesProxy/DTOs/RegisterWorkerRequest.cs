using System;

namespace AlphaOmega.IO.DTOs
{
	/// <summary>Represents a request to register a worker for named pipe communication.</summary>
	public sealed class RegisterWorkerRequest
	{
		/// <summary>Unique identifier of the worker to register.</summary>
		public String WorkerId { get; set; }

		/// <summary>Name of the named pipe used for communication.</summary>
		public String PipeName { get; set; }

		/// <summary>Initializes a new instance of the request to register a worker.</summary>
		public RegisterWorkerRequest(String workerId, String pipeName)
		{
			this.WorkerId = workerId;
			this.PipeName = pipeName;
		}
	}
}