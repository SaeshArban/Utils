using System;
using Microsoft.Extensions.DependencyInjection;
using Refit;

namespace ChannelForJobExample.Extensions.Yandex
{
	public static class AddYandexExtension
	{
		public static IServiceCollection AddYandex(this IServiceCollection collection)
		{
			collection
				.AddRefitClient<IYandex>()
				.ConfigureHttpClient(c => { c.BaseAddress = new Uri("https://yandex.ru/"); });
			return collection;
		}
	}
}