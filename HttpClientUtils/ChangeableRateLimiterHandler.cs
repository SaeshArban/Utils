using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace HttpClientUtils
{
	public sealed class ChangeableRateLimiterHandler<T> : DelegatingHandler
	{
		private readonly SemaphoreContainer<T> _semaphoreContainer;

		public ChangeableRateLimiterHandler(SemaphoreContainer<T> semaphoreContainer)
		{
			_semaphoreContainer = semaphoreContainer ?? throw new ArgumentNullException(nameof(semaphoreContainer));
		}

		public void SetMaxRps(int max)
		{
			_semaphoreContainer.SemaphoreSlim.SetMax(max);
		}

		protected override async Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			var semaphoreSlim = _semaphoreContainer.SemaphoreSlim;
			await semaphoreSlim.WaitAsync(cancellationToken);
			var mainTask = base.SendAsync(request, cancellationToken);
#pragma warning disable 4014
			// ReSharper disable once MethodSupportsCancellation
			Task.Run(async () =>
#pragma warning restore 4014
			{
				try
				{
					// ReSharper disable once MethodSupportsCancellation
					await Task.WhenAll(mainTask, Task.Delay(TimeSpan.FromSeconds(1)));
				}
				finally
				{
					semaphoreSlim.Release();
				}
			});
			var response = await mainTask;
			return response;
		}
	}
}