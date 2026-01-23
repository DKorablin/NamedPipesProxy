using System;
using System.Threading;
using System.Threading.Tasks;
using AlphaOmega.IO;
using NUnit.Framework;

namespace NamedPipesProxy.Tests
{
	/// <summary>Tests for RpcResponseChannel.</summary>
	[TestFixture]
	[Timeout(5000)]
	public class RpcResponseChannelTests
	{
		[Test]
		public async Task WaitForResponseAsync_CompletesWhenResponseArrives()
		{
			RpcResponseChannel channel = new RpcResponseChannel();
			PipeMessage request = new PipeMessage("Request", "Payload");
			PipeMessage response = new PipeMessage(request, "Response", "Reply");
			TimeSpan timeout = TimeSpan.FromSeconds(1);

			Task<PipeMessage> waitTask = channel.WaitForResponseAsync(request, timeout);

			Boolean completed = channel.CompleteResponse(request, response);
			PipeMessage result = await waitTask;

			Assert.IsTrue(completed);
			Assert.AreSame(response, result);
			Assert.IsFalse(channel.CompleteResponse(request, response));
		}

		[Test]
		public async Task WaitForResponseAsync_ThrowsWhenRequestAlreadyPending()
		{
			RpcResponseChannel channel = new RpcResponseChannel();
			PipeMessage request = new PipeMessage("Request", "Payload");
			TimeSpan timeout = TimeSpan.FromSeconds(1);

			Task<PipeMessage> pending = channel.WaitForResponseAsync(request, timeout);

			InvalidOperationException exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await channel.WaitForResponseAsync(request, timeout));
			Assert.IsNotNull(exception);

			channel.FailResponse(request, new InvalidOperationException("Failed"));
			Assert.ThrowsAsync<InvalidOperationException>(async () => await pending);
		}

		[Test]
		public async Task WaitForResponseAsync_TimesOutAndRemovesPendingEntry()
		{
			RpcResponseChannel channel = new RpcResponseChannel();
			PipeMessage request = new PipeMessage("Request", "Payload");
			TimeSpan timeout = TimeSpan.FromMilliseconds(50);

			TimeoutException exception = Assert.ThrowsAsync<TimeoutException>(async () => await channel.WaitForResponseAsync(request, timeout));
			Assert.IsNotNull(exception);

			PipeMessage response = new PipeMessage(request, "Response", "Late");
			Boolean completedAfterTimeout = channel.CompleteResponse(request, response);
			Assert.IsFalse(completedAfterTimeout);
		}

		[Test]
		public void CompleteResponse_ReturnsFalseWhenNoPendingRequest()
		{
			RpcResponseChannel channel = new RpcResponseChannel();
			PipeMessage request = new PipeMessage("Request", "Payload");
			PipeMessage response = new PipeMessage(request, "Response", "Payload");

			Boolean completed = channel.CompleteResponse(request, response);

			Assert.IsFalse(completed);
		}

		[Test]
		public void FailResponse_CompletesTaskWithException()
		{
			RpcResponseChannel channel = new RpcResponseChannel();
			PipeMessage request = new PipeMessage("Request", "Payload");
			TimeSpan timeout = TimeSpan.FromSeconds(1);
			InvalidOperationException failure = new InvalidOperationException("Failure");

			Task<PipeMessage> waitTask = channel.WaitForResponseAsync(request, timeout);

			channel.FailResponse(request, failure);

			InvalidOperationException exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await waitTask);
			Assert.AreSame(failure, exception);
		}
	}
}
