using System;
using Microsoft.Extensions.DependencyInjection;

namespace HttpClientUtils
{
	public static class AddRateLimiterExtensions
	{
		public static IServiceCollection AddRateLimiter<T>(
			this IServiceCollection services,
			Func<IServiceProvider, int> getMaxCountPerSecond)
		{
			services
				.AddSingleton(x => new SemaphoreContainer<T>(getMaxCountPerSecond(x)))
				.AddTransient<ChangeableRateLimiterHandler<T>>();

			return services;
		}
	}
}