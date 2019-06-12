﻿using JetBrains.Annotations;

using System;
using System.Collections.Concurrent;
using System.Threading;

namespace StringDB.Querying
{
	/// <summary>
	/// Caches the results of an <see cref="TrainEnumerable{T}"/> to allow
	/// for less locking and performance while multiple entires enumerate over one.
	/// This will allow one multiple threads to feel like they're accessing an array,
	/// and can automatically populate future values and remove old ones, so it's
	/// light on memory and can support lots of values.
	///
	/// WARNING: elongated usage may run into issues if you read more values than a long.
	/// A future version will fix this, but in the meantime, be aware that reading for
	/// a long time is a bad idea.
	/// </summary>
	/// <typeparam name="T">The type of item to cache.</typeparam>
	public class EnumeratorTrainCache<T>
	{
		private readonly ConcurrentDictionary<long, TrainCache<T>> _cache = new ConcurrentDictionary<long, TrainCache<T>>();
		private readonly object _top = new object();

		private long _numTop;
		private long _numLast;

		public long Top => _numTop;
		public long Last => _numLast;

		public int Participants;

		public void AppendItem([NotNull] T item)
		{
			var trainCache = new TrainCache<T>
			{
				Lock = new object(),
				Accessors = 0,
				Item = item,
			};

			lock (_top)
			{
				_cache[_numTop] = trainCache;
				Interlocked.Increment(ref _numTop);
			}
		}

		[NotNull]
		public T this[long index]
		{
			get
			{
				var trainCache = _cache[index];

				lock (trainCache.Lock)
				{
					trainCache.Accessors++;

					if (trainCache.Accessors >= Participants)
					{
						Interlocked.Increment(ref _numLast);
						_cache.TryRemove(index, out _);
					}

					return trainCache.Item;
				}
			}
		}

		[NotNull]
		public T Get(long index, [NotNull] Func<T> factory)
		{
			if (index >= _numTop)
			{
				lock (_top)
				{
					// if the index is still lower than the top, we don't
					// want to append an item. some other thread could've
					// locked and we don't want to repeat their work here
					if (index >= _numTop)
					{
						AppendItem(factory());
					}
				}
			}

			return this[index];
		}

		public int InviteParticipant() => Interlocked.Increment(ref Participants);

		public int ExitParticipant() => Interlocked.Decrement(ref Participants);
	}
}