using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ChannelForJobExample.Extensions.Yandex;
using ChannelUtils;
using Microsoft.Extensions.Logging;

namespace ChannelForJobExample.YandexSymbolCounter
{
	public class EndlessCollector
	{
		private readonly ILogger<EndlessCollector> _logger;
		private readonly IYandex _yandex;

		private volatile int _processedNow;

		public EndlessCollector(ILogger<EndlessCollector> logger, IYandex yandex)
		{
			_logger = logger;
			_yandex = yandex;
		}

		public ChannelReader<string> Start(
			ChannelWriter<DataProcessException> exceptionChannel,
			CancellationToken token)
		{
			var channel = Channel.CreateUnbounded<string>();

			Task.Run(async () =>
				{
					while (!token.IsCancellationRequested)
					{
						if (_processedNow > 300)
						{
							await Task.Delay(TimeSpan.FromSeconds(5), token);
							continue;
						}

						try
						{
							var rows = (await _yandex.Get(token)).Split(new[] {'\n', '\r', ',', ';'},
								StringSplitOptions.RemoveEmptyEntries);
							if (rows.Length == 0)
							{
								await Task.Delay(TimeSpan.FromMinutes(15), token);
								continue;
							}

							foreach (var row in rows)
							{
								await channel.Writer.WriteAsync(row, token);
								Interlocked.Increment(ref _processedNow);
							}
						}
						catch (Exception exception)
						{
							_logger.LogError(exception, exception.Message);
							await exceptionChannel.WriteAsync(new DataProcessException(exception), token);
						}
					}
				},
				token);

			return channel;
		}

		public void DecrementProcessedCount()
		{
			if (_processedNow <= 0)
			{
				return;
			}

			Interlocked.Decrement(ref _processedNow);
		}

		public void DecrementProcessedCount(int count)
		{
			if (_processedNow <= 0)
			{
				return;
			}

			for (int i = 0; i < count; i++)
			{
				Interlocked.Decrement(ref _processedNow);
			}
		}

		public int GetCountInProcess() => _processedNow;
	}
}