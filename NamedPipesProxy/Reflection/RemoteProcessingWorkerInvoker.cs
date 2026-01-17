using System;
using System.Threading;
using System.Threading.Tasks;
using AlphaOmega.IO.DTOs;
using AlphaOmega.IO.Interfaces;

namespace AlphaOmega.IO.Reflection
{
	/// <summary>Invokes remote processing logic on a specific worker via named pipes.</summary>
	public class RemoteProcessingWorkerInvoker : RemoteProcessingLogicInvoker
	{
		private String _workerId;

		/// <summary>Initializes a new instance of the invoker.</summary>
		public RemoteProcessingWorkerInvoker() { }

		/// <summary>Initializes a new instance of the invoker with a processing interface type.</summary>
		/// <param name="interfaceType">Processing logic interface type.</param>
		public RemoteProcessingWorkerInvoker(Type interfaceType)
			: base(interfaceType)
		{
		}

		/// <summary>Initializes the invoker for a specific worker and registers the server.</summary>
		/// <typeparam name="T">Processing logic interface type.</typeparam>
		/// <param name="registerServer">Registry server instance.</param>
		/// <param name="workerId">Target worker identifier.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		public void Initialize<T>(IRegistryServer registerServer, String workerId, CancellationToken cancellationToken) where T : class
		{
			if(String.IsNullOrWhiteSpace(workerId))
				throw new ArgumentNullException(nameof(workerId));

			this._workerId = workerId;
			this.Initialize<T>(registerServer, cancellationToken);
		}

		/// <summary>Sends a request to the target worker and returns the deserialized response.</summary>
		/// <param name="request">Request message to send.</param>
		/// <param name="responseType">Expected response type.</param>
		/// <returns>Deserialized response object.</returns>
		/// <exception cref="InvalidOperationException">Thrown if invoker is not initialized or worker returns an error.</exception>
		protected override async Task<Object> SendRequestAndGetResponseAsync(PipeMessage request, Type responseType)
		{
			if(String.IsNullOrWhiteSpace(this._workerId))
				throw new InvalidOperationException("Proxy not properly initialized");

			PipeMessage response = await this.RegisterServer.SendRequestToWorker(this._workerId, request, this.CancellationToken);
			if(response.Type == PipeMessageType.Error.ToString())
			{
				var error = response.Deserialize<ErrorResponse>();
				throw new InvalidOperationException(error.Message);
			}

			Object result = response.Deserialize(responseType);
			return result;
		}
	}
}