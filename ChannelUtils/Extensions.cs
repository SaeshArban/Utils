using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ChannelUtils
{
	public static class Extensions
	{
		/// <summary>
		/// Create channel and send all <see cref="data"/> into the channel
		/// </summary>
		/// <param name="data"></param>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public static ChannelReader<T> Create<T>(this IEnumerable<T> data)
		{
			var ch = Channel.CreateUnbounded<T>(new UnboundedChannelOptions
			{
				AllowSynchronousContinuations = true
			});

			Task.Run(async () =>
			{
				foreach (var item in data)
				{
					await ch.Writer.WriteAsync(item);
				}

				ch.Writer.Complete();
			});

			return ch.Reader;
		}

		/// <summary>
		/// Accumulate data from the channel into chunks and send it to the new channel
		/// </summary>
		/// <param name="data"></param>
		/// <param name="count"></param>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public static ChannelReader<IReadOnlyCollection<T>> Combine<T>(
			this ChannelReader<T> data,
			int count)
		{
			var ch = Channel.CreateUnbounded<IReadOnlyCollection<T>>();

			Task.Run(async () =>
			{
				var cts = new CancellationTokenSource();
				var tempCollection = new ConcurrentQueue<T>();
				var timeContainer = new TimeContainer
				{
					LastFlush = DateTime.Now
				};

				// We do not want to endlessly wait for the newest data in the channel if the channel is not completed
#pragma warning disable 4014
				Task.Run(async () =>
#pragma warning restore 4014
					{
						while (!cts.Token.IsCancellationRequested)
						{
							await Task.Delay(TimeSpan.FromMinutes(1), cts.Token);
							if (DateTime.Now - timeContainer.LastFlush < TimeSpan.FromMinutes(1)
							    || tempCollection.IsEmpty)
							{
								continue;
							}

							await ch.Writer.WriteAsync(tempCollection.DequeueAll(), cts.Token);
						}
					},
					cts.Token);

				while (await data.WaitToReadAsync(cts.Token))
				{
					if (!data.TryRead(out var channelData)) continue;
					tempCollection.Enqueue(channelData);
					if (tempCollection.Count != count) continue;
					await ch.Writer.WriteAsync(tempCollection.DequeueAll(), cts.Token);
					timeContainer.LastFlush = DateTime.Now;
				}

				await ch.Writer.WriteAsync(tempCollection.DequeueAll(), cts.Token);
				ch.Writer.Complete();
				cts.Cancel();
			});

			return ch.Reader;
		}

		/// <summary>
		/// We transform the data in the channels and send it to a new
		/// </summary>
		/// <param name="data"></param>
		/// <param name="asyncTransform"></param>
		/// <param name="exceptionalChannel"></param>
		/// <typeparam name="TInput"></typeparam>
		/// <typeparam name="TOutput"></typeparam>
		/// <returns></returns>
		public static ChannelReader<TOutput>[] Transform<TInput, TOutput>(
			this ChannelReader<TInput>[] data,
			Func<TInput, Task<TOutput>> asyncTransform,
			ChannelWriter<DataProcessException>? exceptionalChannel = null)
		{
			return data.Select(x => x.Transform(asyncTransform, exceptionalChannel)).ToArray();
		}

		/// <summary>
		///  We transform the data in the channel and send it to a new one
		/// </summary>
		/// <param name="data"></param>
		/// <param name="asyncTransform"></param>
		/// <param name="exceptionalChannel"></param>
		/// <typeparam name="TInput"></typeparam>
		/// <typeparam name="TOutput"></typeparam>
		/// <returns></returns>
		public static ChannelReader<TOutput> Transform<TInput, TOutput>(
			this ChannelReader<TInput> data,
			Func<TInput, Task<TOutput>> asyncTransform,
			ChannelWriter<DataProcessException>? exceptionalChannel = null)
		{
			var ch = Channel.CreateUnbounded<TOutput>(new UnboundedChannelOptions
			{
				AllowSynchronousContinuations = true
			});

			Task.Run(async () =>
			{
				while (await data.WaitToReadAsync())
				{
					if (!data.TryRead(out var channelData)) continue;
					try
					{
						var result = await asyncTransform(channelData);
						if (result is null)
						{
							continue;
						}

						await ch.Writer.WriteAsync(result);
					}
					catch (DataProcessException processException) when (exceptionalChannel != null)
					{
						await exceptionalChannel.WriteAsync(processException);
					}
					catch (Exception exception) when (exceptionalChannel != null)
					{
						await exceptionalChannel.WriteAsync(new DataProcessException(exception));
					}
					catch
					{
						// ignored
					}
				}

				ch.Writer.Complete();
			});

			return ch.Reader;
		}

		/// <summary>
		/// We process the data in the channels and send it into new
		/// </summary>
		/// <param name="data"></param>
		/// <param name="asyncAction"></param>
		/// <param name="exceptionalChannel"></param>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public static ChannelReader<T>[] Process<T>(
			this ChannelReader<T>[] data,
			Func<T, Task> asyncAction,
			ChannelWriter<DataProcessException>? exceptionalChannel = null)
		{
			return data.Select(x => x.Process(asyncAction, exceptionalChannel)).ToArray();
		}

		/// <summary>
		/// We process the data in the channel and send it into new one
		/// </summary>
		/// <param name="data"></param>
		/// <param name="asyncAction"></param>
		/// <param name="exceptionalChannel"></param>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public static ChannelReader<T> Process<T>(
			this ChannelReader<T> data,
			Func<T, Task> asyncAction,
			ChannelWriter<DataProcessException>? exceptionalChannel = null)
		{
			var ch = Channel.CreateUnbounded<T>(new UnboundedChannelOptions
			{
				AllowSynchronousContinuations = true
			});

			Task.Run(async () =>
			{
				while (await data.WaitToReadAsync())
				{
					if (!data.TryRead(out var channelData)) continue;
					try
					{
						await asyncAction(channelData);
						await ch.Writer.WriteAsync(channelData);
					}
					catch (DataProcessException processException) when (exceptionalChannel != null)
					{
						await exceptionalChannel.WriteAsync(processException);
					}
					catch (Exception exception) when (exceptionalChannel != null)
					{
						await exceptionalChannel.WriteAsync(new DataProcessException(exception));
					}
					catch
					{
						// ignored
					}
				}

				ch.Writer.Complete();
			});

			return ch.Reader;
		}

		/// <summary>
		/// The last action on the channels data
		/// </summary>
		/// <param name="data"></param>
		/// <param name="asyncAction"></param>
		/// <param name="exceptionalChannel"></param>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public static Task[] Action<T>(
			this ChannelReader<T>[] data,
			Func<T, Task> asyncAction,
			ChannelWriter<DataProcessException>? exceptionalChannel = null)
		{
			return data.Select(x => x.Action(asyncAction, exceptionalChannel)).ToArray();
		}

		/// <summary>
		/// The last action on the channel data
		/// </summary>
		/// <param name="data"></param>
		/// <param name="asyncAction"></param>
		/// <param name="exceptionalChannel"></param>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public static Task Action<T>(
			this ChannelReader<T> data,
			Func<T, Task> asyncAction,
			ChannelWriter<DataProcessException>? exceptionalChannel = null)
		{
			return Task.Run(async () =>
			{
				while (await data.WaitToReadAsync())
				{
					if (!data.TryRead(out var channelData)) continue;
					try
					{
						await asyncAction(channelData);
					}
					catch (DataProcessException processException) when (exceptionalChannel != null)
					{
						await exceptionalChannel.WriteAsync(processException);
					}
					catch (Exception exception) when (exceptionalChannel != null)
					{
						await exceptionalChannel.WriteAsync(new DataProcessException(exception));
					}
					catch
					{
						// ignored
					}
				}
			});
		}

		/// <summary>
		/// We created <see cref="n"/> new channels and send to it all data from
		/// <see cref="inputChannel"/> by round robin strategy
		/// </summary>
		/// <param name="inputChannel"></param>
		/// <param name="n"></param>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public static ChannelReader<T>[] Split<T>(this ChannelReader<T> inputChannel, int n)
		{
			var outputs = new Channel<T>[n];

			for (int i = 0; i < n; i++)
				outputs[i] = Channel.CreateUnbounded<T>();

			Task.Run(async () =>
			{
				var index = 0;
				await foreach (var item in inputChannel.ReadAllAsync())
				{
					await outputs[index].Writer.WriteAsync(item);
					index = (index + 1) % n;
				}

				foreach (var channel in outputs)
					channel.Writer.Complete();
			});

			return outputs.Select(ch => ch.Reader).ToArray();
		}

		/// <summary>
		/// Merging all data from <see cref="inputs"/> to new one
		/// </summary>
		/// <param name="inputs"></param>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public static ChannelReader<T> Merge<T>(this ChannelReader<T>[] inputs)
		{
			var output = Channel.CreateUnbounded<T>();

			Task.Run(async () =>
			{
				async Task RedirectLocal(ChannelReader<T> input)
				{
					while (await input.WaitToReadAsync())
					{
						if (!input.TryRead(out var channelData)) continue;
						await output.Writer.WriteAsync(channelData);
					}
				}

				await Task.WhenAll(inputs.Select(RedirectLocal).ToArray());
				output.Writer.Complete();
			});

			return output.Reader;
		}

		/// <summary>
		/// Flattens the sequences from input channel into one sequence into resulting channel
		/// </summary>
		/// <param name="data"></param>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public static ChannelReader<T>[] Flatten<T>(this ChannelReader<IEnumerable<T>>[] data)
		{
			return data.Select(Flatten).ToArray();
		}

		/// <summary>
		/// Flattens the sequences from input channel into one sequence into resulting channel
		/// </summary>
		/// <param name="data"></param>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public static ChannelReader<T> Flatten<T>(this ChannelReader<IEnumerable<T>> data)
		{
			var ch = Channel.CreateUnbounded<T>(new UnboundedChannelOptions
			{
				AllowSynchronousContinuations = true
			});

			Task.Run(async () =>
			{
				while (await data.WaitToReadAsync())
				{
					if (!data.TryRead(out var channelData)) continue;
					foreach (var i in channelData)
					{
						await ch.Writer.WriteAsync(i);
					}
				}

				ch.Writer.Complete();
			});

			return ch.Reader;
		}

		/// <summary>
		/// Merge all tasks into one
		/// </summary>
		/// <param name="tasks"></param>
		/// <returns></returns>
		public static Task Merge(this Task[] tasks) => Task.WhenAll(tasks);

		private static IReadOnlyCollection<T> DequeueAll<T>(this ConcurrentQueue<T> queue)
		{
			var tempCollection = new List<T>();
			while (queue.TryDequeue(out var element))
			{
				tempCollection.Add(element);
			}

			return tempCollection;
		}

		private class TimeContainer
		{
			public DateTime LastFlush { get; set; }
		}
	}
}