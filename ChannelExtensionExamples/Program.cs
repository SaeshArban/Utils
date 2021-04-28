using System;
using System.Linq;
using System.Threading.Tasks;
using ChannelUtils;

namespace ChannelExtensionExamples
{
	class Program
	{
		static async Task Main(string[] args)
		{
			await Enumerable
				.Range(0, 10)
				.Create()
				.Transform(async x => Enumerable.Range(x * 10, x + 10))
				.Split(30) // equals then 30 threads
				.Flatten()
				.Transform(async x =>
				{
					Console.WriteLine(x);
					return x.ToString();
				})
				.Merge()
				.Action(x => Task.CompletedTask);
		}
	}
}