using System.Threading;
using System.Threading.Tasks;
using Refit;

namespace HttpClientUtilsExample
{
	public interface ITest
	{
		[Get("/")]
		public Task<string> Get(CancellationToken token);
	}
}