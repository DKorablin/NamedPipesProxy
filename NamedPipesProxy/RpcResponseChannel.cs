using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using AlphaOmega.IO.DTOs;

namespace AlphaOmega.IO
{
	/// <summary>Manages asynchronous RPC (Remote Procedure Call) request-response correlations.</summary>
	public sealed class RpcResponseChannel
	{
		private readonly ConcurrentDictionary<Guid, TaskCompletionSource<PipeMessage>> _pendingResponses = new ConcurrentDictionary<Guid, TaskCompletionSource<PipeMessage>>();

		/// <summary>Registers a pending request and returns a task that completes when the response arrives.</summary>
		public async Task<PipeMessage> WaitForResponseAsync(PipeMessage message, TimeSpan timeout)
		{
			TaskCompletionSource<PipeMessage> tcs = new TaskCompletionSource<PipeMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

			if(!this._pendingResponses.TryAdd(message.MessageId, tcs))
				throw new InvalidOperationException($"Request already pending. Message={message.MessageId}");

			using(CancellationTokenSource cts = new CancellationTokenSource(timeout))
			using(cts.Token.Register(() => tcs.TrySetException(new TimeoutException($"RPC call timed out after {timeout.TotalSeconds} seconds."))))
			{
				try
				{
					return await tcs.Task.ConfigureAwait(false);
				} finally
				{
					this._pendingResponses.TryRemove(message.MessageId, out _);
				}
			}
		}
		/*public Task<PipeMessage> WaitForResponseAsync(PipeMessage message, TimeSpan timeout)
		{
			var tcs = new TaskCompletionSource<PipeMessage>();

			if(!this._pendingResponses.TryAdd(message.MessageId, tcs))
				throw new InvalidOperationException($"Request already pending. Message={message.MessageId}");

			var cts = new CancellationTokenSource(timeout);
			var registration = cts.Token.Register(() =>
			{
				this._pendingResponses.TryRemove(message.MessageId, out _);
				tcs.TrySetException(new TimeoutException($"RPC call timed out after {timeout.TotalSeconds} seconds"));
			});

			tcs.Task.ContinueWith(_ =>
			{
				registration.Dispose();
				cts.Dispose();
			}, TaskScheduler.Default);

			return tcs.Task;
		}*/

		/// <summary>Completes a pending request with a response.</summary>
		public Boolean CompleteResponse(PipeMessage message, PipeMessage response)
		{
			if(this._pendingResponses.TryRemove(message.MessageId, out var tcs))
			{
				tcs.TrySetResult(response);
				return true;
			}

			PipeServerBase.TraceSource.TraceEvent(System.Diagnostics.TraceEventType.Warning, 8, "No pending request found for response. Message={0}", message.ToString());
			return false;
		}

		/// <summary>Fails a pending request with an error.</summary>
		public void FailResponse(PipeMessage message, Exception ex)
		{
			if(this._pendingResponses.TryRemove(message.MessageId, out var tcs))
				tcs.TrySetException(ex);
		}
	}
}