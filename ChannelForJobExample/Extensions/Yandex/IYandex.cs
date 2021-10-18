using System.Threading;
using System.Threading.Tasks;
using Refit;

namespace ChannelForJobExample.Extensions.Yandex
{
	public interface IYandex
	{
		[Get("/")]
		public Task<string> Get(CancellationToken token);
	}
}