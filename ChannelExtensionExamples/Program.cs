using System;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using ChannelUtils;

namespace ChannelExtensionExamples
{
	class Program
	{
		static async Task Main(string[] args)
		{
			var errorChannel = Channel.CreateUnbounded<DataProcessException>();

			var errorHandlingTask = errorChannel.Reader
				.Action(exception =>
				{
					Console.WriteLine($"Exception! {exception.Message}");
					return Task.CompletedTask;
				});

			await Enumerable
				.Range(0, 10)
				.Create()
				.Transform(x => Enumerable.Range(x * 10, x + 10))
				.InParallel(30) // equals then 30 threads
				.Flatten()
				.Transform(x =>
					{
						Console.WriteLine(x);
						return x.ToString();
					},
					errorChannel)
				.Process(s =>
					{
						if (s.Length < 2)
						{
							throw new Exception(s);
						}

						return Task.CompletedTask;
					},
					errorChannel)
				.Merge()
				.Action(x => Task.CompletedTask);

			await errorHandlingTask;
		}
	}
}