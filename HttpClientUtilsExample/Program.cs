using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HttpClientUtils;
using Microsoft.Extensions.DependencyInjection;
using Refit;

namespace HttpClientUtilsExample
{
	class Program
	{
		static async Task Main(string[] args)
		{
			var serviceCollection = new ServiceCollection();

			serviceCollection
				.AddRateLimiter<ITest>(serviceProvider => 3)
				.AddRefitClient<ITest>()
				.ConfigureHttpClient(c => { c.BaseAddress = new Uri("https://google.com/"); })
				.AddHttpMessageHandler<ChangeableRateLimiterHandler<ITest>>();

			var provider = serviceCollection.BuildServiceProvider();
			var client = provider.GetService<ITest>()
			             ?? throw new NullReferenceException();;
			var rateLimiter = provider.GetService<ChangeableRateLimiterHandler<ITest>>()
			                  ?? throw new NullReferenceException();

#pragma warning disable 4014
			Task.Run(async () =>
#pragma warning restore 4014
			{
				await Task.Delay(TimeSpan.FromSeconds(5));
				rateLimiter.SetMaxRps(10);
			});

#pragma warning disable 4014
			Task.Run(async () =>
#pragma warning restore 4014
			{
				await Task.Delay(TimeSpan.FromSeconds(10));
				rateLimiter.SetMaxRps(1);
			});

			var tasks = Enumerable.Range(0, 1000)
				.Select(
					async i =>
					{
						try
						{
							var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
							await client.Get(cts.Token);
						}
						catch
						{
							// 
						}

						Console.WriteLine(i);
					});
			await Task.WhenAll(tasks.ToArray());
		}
	}
}