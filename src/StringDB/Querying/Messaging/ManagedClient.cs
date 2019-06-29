﻿using System;
using System.Threading;
using System.Threading.Tasks;

namespace StringDB.Querying.Messaging
{
	/// <summary>
	/// This is a client that manages itself. It runs a thread
	/// in the background to continously process results.
	/// </summary>
	public class ManagedClient<TMessage> : IMessageClient<TMessage>
	{
		private readonly IMessageClient<TMessage> _client = new LightweightClient<TMessage>();
		private readonly CancellationTokenSource _dispose = new CancellationTokenSource();
		private readonly TaskCompletionSource<object> _threadDeath = new TaskCompletionSource<object>();

		public ManagedClient
		(
			Func<IMessageClient<TMessage>, CancellationToken, Task> worker,
			CancellationToken cancellationToken = default
		)
		{
			_cancellationToken = cancellationToken;

			_thread = Task.Run(async () =>
			{
				try
				{
					await worker(this, CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken, _dispose.Token).Token)
						.ConfigureAwait(false);
				}
				catch (OperationCanceledException)
				{
				}
				finally
				{
					_threadDeath.SetResult(null);
				}
			});
		}

		private readonly Task _thread;
		private readonly CancellationToken _cancellationToken;

		public Task<Message<TMessage>> Receive(CancellationToken cancellationToken) => _client.Receive(cancellationToken);

		public void Queue(Message<TMessage> message) => _client.Queue(message);

		public void Dispose()
		{
			_dispose.Cancel();

			Task.WhenAny(_threadDeath.Task, Task.Delay(TimeSpan.FromSeconds(10)))
				.GetAwaiter().GetResult();

			if (!_threadDeath.Task.IsCompleted)
			{
				throw new TimeoutException("Waited too long for worker task to complete before disposing.");
			}
		}
	}
}