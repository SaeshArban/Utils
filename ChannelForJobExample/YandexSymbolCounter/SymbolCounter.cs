using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ChannelForJobExample.Extensions.Yandex;
using ChannelUtils;

namespace ChannelForJobExample.YandexSymbolCounter
{
	public class SymbolCounter
	{
		private readonly EndlessCollector _endlessCollector;

		public SymbolCounter(EndlessCollector endlessCollector)
		{
			_endlessCollector = endlessCollector;
		}

		public async Task Start(CancellationToken token)
		{
			var exceptionsChannel = Channel
				.CreateUnbounded<DataProcessException>();
			var exceptionsTask = exceptionsChannel.Reader
				.Action(x =>
				{
					Console.WriteLine(x.Message);
					_endlessCollector.DecrementProcessedCount();
				});

			await _endlessCollector
				.Start(exceptionsChannel, token)
				.InParallel(32)
				.Transform(x => x.Split(' ').Length)
				.Merge()
				.CreateChunks(20)
				.Action(x =>
				{
					Console.WriteLine(string.Join(", ", x));
					_endlessCollector.DecrementProcessedCount(x.Count);
				});
			await exceptionsTask;
		}
	}
}