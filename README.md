# c# Utils

## Features

- Channel extensions
- ChangeableSemaphoreSlim
- ChangeableRateLimiterHandler for httpClient


## Channel extensions usage

```c#
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
	.Transform(async x => Enumerable.Range(x * 10, x + 10))
	.Split(30) // equals then 30 threads
	.Flatten()
	.Transform(async x =>
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
```

produces

```console
40
60
0
1
2
30
....
82
90
67
Exception! 0
Exception! 1
2
52
....
```
## RateLimiter usage
### Add to http client

```c#
serviceCollection
	.AddRateLimiter<ITest>(serviceProvider => 3)
	.AddRefitClient<ITest>()
	.ConfigureHttpClient(c => { c.BaseAddress = new Uri("https://google.com/"); })
	.AddHttpMessageHandler<ChangeableRateLimiterHandler<ITest>>();
```

### Change max rps
``` c#
var rateLimiter = provider.GetService<ChangeableRateLimiterHandler<ITest>>()
			                  ?? throw new NullReferenceException();

Task.Run(async () =>
{
	await Task.Delay(TimeSpan.FromSeconds(5));
	rateLimiter.SetMaxRps(10);
});
```

## License

MIT