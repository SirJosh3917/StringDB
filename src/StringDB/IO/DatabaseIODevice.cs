﻿using System.Collections.Generic;

namespace StringDB.IO
{
	public sealed class DatabaseIODevice : IDatabaseIODevice
	{
		private readonly ILowlevelDatabaseIODevice _lowlevelDBIOD;

		public DatabaseIODevice(ILowlevelDatabaseIODevice lowlevelDBIOD) => _lowlevelDBIOD = lowlevelDBIOD;

		public void Reset() => _lowlevelDBIOD.Reset();

		public byte[] ReadValue(long position) => _lowlevelDBIOD.ReadValue(position);

		public DatabaseItem ReadNext()
		{
			// handle EOFs/Jumps
			var peek = _lowlevelDBIOD.Peek();

			ExecuteJumps(ref peek);

			if (peek == NextItemPeek.EOF)
			{
				return new DatabaseItem
				{
					EndOfItems = true
				};
			}

			// peek HAS to be an Index at this point

			var item = _lowlevelDBIOD.ReadIndex();

			return new DatabaseItem
			{
				Key = item.Index,
				DataPosition = item.DataPosition,
				EndOfItems = false
			};
		}

		private void ExecuteJumps(ref NextItemPeek peek)
		{
			while (peek == NextItemPeek.Jump)
			{
				var jump = _lowlevelDBIOD.ReadJump();
				_lowlevelDBIOD.Seek(jump);
				peek = _lowlevelDBIOD.Peek();
			}
		}

		public void Insert(KeyValuePair<byte[], byte[]>[] items)
		{
			var offset = _lowlevelDBIOD.GetPosition();

			// we need to calculate the total offset of all the indexes
			// then we write every index & increment the offset by the offset of each value
			// and then we write the values

			// phase 1: calculating total offset

			foreach (var kvp in items)
			{
				offset += _lowlevelDBIOD.CalculateIndexOffset(kvp.Key);
			}

			// the jump offset is important, we will be jumping after
			offset += _lowlevelDBIOD.JumpOffsetSize;

			// phase 2: writing each key
			//			and incrementing the offset by the value

			foreach (var kvp in items)
			{
				_lowlevelDBIOD.WriteIndex(kvp.Key, offset);

				offset += _lowlevelDBIOD.CalculateValueOffset(kvp.Value);
			}

			// phase 3: writing each value sequentially

			foreach (var kvp in items)
			{
				_lowlevelDBIOD.WriteValue(kvp.Value);
			}
		}

		public void Dispose() => _lowlevelDBIOD.Dispose();
	}
}