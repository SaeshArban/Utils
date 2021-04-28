using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChannelUtils;
using FluentAssertions;
using Xunit;

namespace Tests
{
	public class ChannelExtensionsTests
	{
		[Fact]
		public async Task SimpleCreateTest()
		{
			var result = new List<string>();

			await Enumerable.Range(0, 10)
				.Create()
				.Action(i =>
				{
					result.Add(i.ToString());
					return Task.CompletedTask;
				});

			result.Should().BeEquivalentTo(Enumerable.Range(0, 10).Select(x => x.ToString()));
		}
	}
}