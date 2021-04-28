namespace HttpClientUtils
{
	// For DI difference
	// ReSharper disable once UnusedTypeParameter
	public class SemaphoreContainer<T>
	{
		public SemaphoreContainer(int maxCountPerSecond)
		{
			SemaphoreSlim = new ChangeableSemaphoreSlim(maxCountPerSecond, maxCountPerSecond);
		}

		public ChangeableSemaphoreSlim SemaphoreSlim { get; }
	}
}