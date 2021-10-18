using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChannelForJobExample.YandexSymbolCounter
{
	public class JobHostedService: IHostedService
	{
		private readonly ILogger<JobHostedService> _logger;
		private readonly SymbolCounter _symbolCounter;
		private readonly CancellationTokenSource _mainTokenSource = new();

		public JobHostedService(
			ILogger<JobHostedService> logger,
			SymbolCounter symbolCounter)
		{
			_logger = logger;
			_symbolCounter = symbolCounter;
		}

		public Task StartAsync(CancellationToken stoppingToken)
		{
			_logger.LogInformation("The ExportHostedService started.");
#pragma warning disable 4014
			Task.Run(DoWork, stoppingToken);
#pragma warning restore 4014
			return Task.CompletedTask;
		}

		private async Task DoWork()
		{
			_mainTokenSource.Token.ThrowIfCancellationRequested();

			_logger.LogInformation("The ExportHostedService DoWork");
			await _symbolCounter.Start(_mainTokenSource.Token);
		}

		public Task StopAsync(CancellationToken stoppingToken)
		{
			_logger.LogInformation("The ExportHostedService stopped.");
			_mainTokenSource.Cancel();

			return Task.CompletedTask;
		}
	}
}