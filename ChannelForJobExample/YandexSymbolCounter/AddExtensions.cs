using Microsoft.Extensions.DependencyInjection;

namespace ChannelForJobExample.YandexSymbolCounter
{
	public static class AddExtensions
	{
		public static IServiceCollection AddJob(this IServiceCollection collection)
		{
			collection
				.AddHostedService<JobHostedService>()
				.AddSingleton<SymbolCounter>()
				.AddSingleton<EndlessCollector>();
			return collection;
		}
	}
}