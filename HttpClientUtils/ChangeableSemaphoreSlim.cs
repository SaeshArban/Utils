using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace HttpClientUtils
{
	/// <summary>
	/// Limits the number of threads that can access a resource or pool of resources concurrently.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The <see cref="ChangeableSemaphoreSlim"/> provides a lightweight semaphore class that doesn't
	/// use Windows kernel semaphores and provides ability to change max count on the fly.
	/// </para>
	/// <para>
	/// All public and protected members of <see cref="ChangeableSemaphoreSlim"/> are thread-safe and may be used
	/// concurrently from multiple threads, with the exception of Dispose, which
	/// must only be used when all other operations on the <see cref="ChangeableSemaphoreSlim"/> have
	/// completed.
	/// </para>
	/// </remarks>
	// ReSharper disable once UseNameofExpression
	[DebuggerDisplay("Current Count = {_mAvailableCount}")]
	public sealed class ChangeableSemaphoreSlim : IDisposable
	{
		#region Private Fields

		// The semaphore count, initialized in the constructor to the initial value, every release call incremetns it
		// and every wait call decrements it as long as its value is positive otherwise the wait will block.
		// Its value must be between the maximum semaphore value and zero
		private volatile int _mAvailableCount;

		// The maximum semaphore value, it is initialized to Int.MaxValue if the client didn't specify it. it is used 
		// to check if the count excceeded the maxi value or not.
		private volatile int _maxCount;

		private volatile int _asyncWaitCount;

		// Object used to synchronize access to state on the instance.  The contained
		// Boolean value indicates whether the instance has been disposed.
		private readonly StrongBox<bool> _mLockObjAndDisposed;

		// Head of list representing asynchronous waits on the semaphore.
		private TaskNode? _mAsyncHead;

		// Tail of list representing asynchronous waits on the semaphore.
		private TaskNode? _mAsyncTail;

		// A pre-completed task with Result==true
		private static readonly Task<bool> STrueTask =
			Task.FromResult(true);

		// Task in a linked list of asynchronous waiters
		private sealed class TaskNode : TaskCompletionSource<bool>
		{
			internal TaskNode? Prev, Next;
		}

		#endregion

		#region Public properties

		/// <summary>
		/// Gets the current count of the <see cref="ChangeableSemaphoreSlim"/>.
		/// </summary>
		/// <value>The current count of the <see cref="ChangeableSemaphoreSlim"/>.</value>
		public int AvailableCount
		{
			get
			{
				lock (_mLockObjAndDisposed)
				{
					var currentCount = _mAvailableCount;
					return currentCount;
				}
			}
		}

		/// <summary>
		/// Gets the current count of the <see cref="ChangeableSemaphoreSlim"/>.
		/// </summary>
		/// <value>The current count of the <see cref="ChangeableSemaphoreSlim"/>.</value>
		public int MaxCount
		{
			get
			{
				lock (_mLockObjAndDisposed)
				{
					var maxCount = _maxCount;
					return maxCount;
				}
			}
		}

		/// <summary>
		/// Gets the current count of the <see cref="ChangeableSemaphoreSlim"/>.
		/// </summary>
		/// <value>The current count of the <see cref="ChangeableSemaphoreSlim"/>.</value>
		public int AsyncWaitCount
		{
			get
			{
				lock (_mLockObjAndDisposed)
				{
					var asyncWaitCount = _asyncWaitCount;
					return asyncWaitCount;
				}
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// Initializes a new instance of the <see cref="ChangeableSemaphoreSlim"/> class, specifying
		/// the initial and maximum number of requests that can be granted concurrently.
		/// </summary>
		/// <param name="initialCount">The initial number of requests for the semaphore that can be granted
		/// concurrently.</param>
		/// <param name="maxCount">The maximum number of requests for the semaphore that can be granted
		/// concurrently.</param>
		/// <exception cref="T:System.ArgumentOutOfRangeException"> <paramref name="initialCount"/>
		/// is less than 0. -or-
		/// <paramref name="initialCount"/> is greater than <paramref name="maxCount"/>. -or-
		/// <paramref name="maxCount"/> is less than 0.</exception>
		public ChangeableSemaphoreSlim(int initialCount, int maxCount)
		{
			if (initialCount < 0 || initialCount > maxCount)
			{
				throw new ArgumentOutOfRangeException(
					nameof(initialCount),
					initialCount,
					$"{nameof(initialCount)} must be more or equals zero and less or equals {nameof(maxCount)}");
			}

			_maxCount = maxCount;
			_mAvailableCount = initialCount;
			_mLockObjAndDisposed = new StrongBox<bool>();
		}

		~ChangeableSemaphoreSlim()
		{
			Dispose(true);
		}

		#endregion

		#region Methods

		public void SetMax(int max)
		{
			if (max <= 0)
			{
				throw new ArgumentException("max value mast be greate then 0");
			}

			if (_maxCount > max)
			{
				while (_maxCount != max)
				{
					Interlocked.Decrement(ref _maxCount);
					Interlocked.Decrement(ref _mAvailableCount);
				}
			}
			else if (_maxCount < max)
			{
				while (_maxCount != max)
				{
					Interlocked.Increment(ref _maxCount);
					Release();
				}
			}
		}

		/// <summary>
		/// Asynchronously waits to enter the <see cref="ChangeableSemaphoreSlim"/>,
		/// using a 32-bit signed integer to measure the time interval, 
		/// while observing a <see cref="T:System.Threading.CancellationToken"/>.
		/// </summary>
		/// <param name="cancellationToken">The <see cref="T:System.Threading.CancellationToken"/> to observe.</param>
		/// <returns>
		/// A task that will complete with a result of true if the current thread successfully entered 
		/// the <see cref="ChangeableSemaphoreSlim"/>, otherwise with a result of false.
		/// </returns>
		/// <exception cref="T:System.ObjectDisposedException">The current instance has already been
		/// disposed.</exception>
		public Task<bool> WaitAsync(CancellationToken cancellationToken)
		{
			CheckDispose();

			// Bail early for cancellation
			if (cancellationToken.IsCancellationRequested)
				return Task.FromCanceled<bool>(cancellationToken);

			lock (_mLockObjAndDisposed)
			{
				// If there are counts available, allow this waiter to succeed.
				if (_mAvailableCount > 0)
				{
					Interlocked.Decrement(ref _mAvailableCount);
					return STrueTask;
				}

				// If there aren't, create and return a task to the caller.
				// The task will be completed either when they've successfully acquired
				// the semaphore or when the timeout expired or cancellation was requested.
				var asyncWaiter = CreateAndAddAsyncWaiter();
				return asyncWaiter.Task;
			}
		}

		/// <summary>Creates a new task and stores it into the async waiters list.</summary>
		/// <returns>The created task.</returns>
		private TaskNode CreateAndAddAsyncWaiter()
		{
			Interlocked.Increment(ref _asyncWaitCount);

			// Create the task
			var task = new TaskNode();

			// Add it to the linked list
			if (_mAsyncHead == null)
			{
				_mAsyncHead = task;
				_mAsyncTail = task;
			}
			else
			{
				if (_mAsyncTail != null)
				{
					_mAsyncTail.Next = task;
					task.Prev = _mAsyncTail;
				}

				_mAsyncTail = task;
			}

			// Hand it back
			return task;
		}

		/// <summary>Removes the waiter task from the linked list.</summary>
		/// <param name="task">The task to remove.</param>
		/// <returns>true if the waiter was in the list; otherwise, false.</returns>
		private void RemoveAsyncWaiter(TaskNode task)
		{
			Interlocked.Decrement(ref _asyncWaitCount);
			// Remove it from the linked list
			if (task.Next != null) task.Next.Prev = task.Prev;
			if (task.Prev != null) task.Prev.Next = task.Next;
			if (_mAsyncHead == task) _mAsyncHead = task.Next;
			if (_mAsyncTail == task) _mAsyncTail = task.Prev;

			// Make sure not to leak
			task.Next = task.Prev = null;
		}

		/// <summary>
		/// Exits the <see cref="ChangeableSemaphoreSlim"/> a specified number of times.
		/// </summary>
		public void Release()
		{
			try
			{
				CheckDispose();

				lock (_mLockObjAndDisposed)
				{
					// If the release count would result exceeding the maximum count, do nothing
					if (_mAvailableCount + 1 > _maxCount)
					{
						return;
					}

					Interlocked.Increment(ref _mAvailableCount);

					while (_mAvailableCount > 0 && _mAsyncHead != null)
					{
						Interlocked.Decrement(ref _mAvailableCount);

						// Get the next async waiter to release and queue it to be completed
						var waiterTask = _mAsyncHead;
						RemoveAsyncWaiter(waiterTask); // ensures waiterTask.Next/Prev are null
						waiterTask.TrySetResult(result: true);
					}
				}
			}
			catch (Exception
				exception)
			{
				Console.WriteLine(exception.Message);
				throw;
			}

			// And return the count
		}

		/// <summary>
		/// Releases all resources used by the current instance of <see
		/// cref="ChangeableSemaphoreSlim"/>.
		/// </summary>
		/// <remarks>
		/// Unlike most of the members of <see cref="ChangeableSemaphoreSlim"/>, <see cref="Dispose()"/> is not
		/// thread-safe and may not be used concurrently with other members of this instance.
		/// </remarks>
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		/// <summary>
		/// When overridden in a derived class, releases the unmanaged resources used 
		/// </summary>
		/// <param name="disposing">true to release both managed and unmanaged resources;
		/// false to release only unmanaged resources.</param>
		/// <remarks>
		/// Unlike most of the members of <see cref="ChangeableSemaphoreSlim"/>, <see cref="Dispose(bool)"/> is not
		/// thread-safe and may not be used concurrently with other members of this instance.
		/// </remarks>
		private void Dispose(bool disposing)
		{
			if (disposing)
			{
				_mLockObjAndDisposed.Value = true;

				lock (_mLockObjAndDisposed)
				{
					_mAsyncHead = null;
					_mAsyncTail = null;
				}
			}
		}

		/// <summary>
		/// Checks the dispose status by checking the lock object, if it is null means that object
		/// has been disposed and throw ObjectDisposedException
		/// </summary>
		private void CheckDispose()
		{
			if (_mLockObjAndDisposed.Value)
			{
				throw new ObjectDisposedException(nameof(ChangeableSemaphoreSlim), "Already disposed");
			}
		}

		#endregion
	}
}