﻿using JetBrains.Annotations;

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace StringDB.IO.Compatibility
{
	public sealed class StringDB10_0_0LowlevelDatabaseIODevice : ILowlevelDatabaseIODevice
	{
		private static class Constants
		{
			public const byte EOF = 0x00;
			public const byte IndexSeparator = 0xFF;
			public const byte MaxIndexSize = IndexSeparator;
		}

		private readonly BinaryReader _br;
		private readonly BinaryWriter _bw;
		private readonly StreamCacheMonitor _stream;

		private readonly byte[] _buffer;
		private bool _disposed;
		private object _disposeLock = new object();

		public StringDB10_0_0LowlevelDatabaseIODevice
		(
			[NotNull] Stream stream,
			bool leaveStreamOpen = false
		)
		{
			// use a buffer when performing single byte writes since writing a single byte
			// allocates a new byte array every time, and that's a very costly operation.
			// the size of this buffer is artificial.
			_buffer = new byte[16 + Constants.MaxIndexSize];

			// We wrap the stream in this so that lookups to Position and Length are quick and snappy.
			// This is to prevent a performance concern regarding EOF using excessive amounts of time.
			// This has the issue of being cached, but calling IODevice.Reset should fix it right up.
			// Of course, this has bad implications and might be reverted later, but it definitely
			// fixes the performance gap without making the code ugly.
			_stream = new StreamCacheMonitor(stream);
			_br = new BinaryReader(_stream, Encoding.UTF8, leaveStreamOpen);
			_bw = new BinaryWriter(_stream, Encoding.UTF8, leaveStreamOpen);

			JumpPos = ReadBeginning();
		}

		~StringDB10_0_0LowlevelDatabaseIODevice() => Dispose();

		public void Dispose()
		{
			// a race condition can occur
			// if both the finalizer and dispose get called
			// ( and it has happened once in testing, so... )
			lock (_disposeLock)
			{
				if (_disposed)
				{
					return;
				}

				_disposed = true;
			}

			Seek(0);

			// write the jump position at the beginning
			_bw.Write(JumpPos);

			Flush();

			_bw.Dispose();
			_br.Dispose();
		}

		public Stream InnerStream => _stream;

		public int JumpOffsetSize { get; } = sizeof(byte) + sizeof(int);

		public long JumpPos { get; set; }

		// As it turns out, this is a major performance concern
		// when using a FileStream.
		// Thus, the chosen solution was to wrap the given Stream into a StreamCacheMonitor.
		// This makes these lookups quick and snappy.
		private bool EOF => GetPosition() >= _stream.Length;

		public NextItemPeek Peek(out byte peekResult)
		{
			var result = PeekByte();
			peekResult = result;

			switch (result)
			{
				case Constants.EOF: return NextItemPeek.EOF;
				case Constants.IndexSeparator: return NextItemPeek.Jump;
				default: return NextItemPeek.Index;
			}
		}

		public LowLevelDatabaseItem ReadIndex(byte peekResult)
		{
			if (EOF)
			{
				throw new NotSupportedException("Cannot read past EOF.");
			}

			return new LowLevelDatabaseItem
			{
				DataPosition = ReadDownsizedLong(),
				Index = _br.ReadBytes(count: peekResult)
			};
		}

		public long ReadJump() => ReadDownsizedLong();

		public byte[] ReadValue(long dataPosition)
		{
			Seek(dataPosition);
			var length = ReadVariableLength();

			if (length > int.MaxValue)
			{
				throw new NotSupportedException($"Cannot read a value outside the integer bounds: {length}");
			}

			return _br.ReadBytes((int)length);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int CalculateIndexOffset(byte[] key)
			=> sizeof(byte)
			+ sizeof(int)
			+ key.Length;

		public void WriteIndex(byte[] key, long dataPosition)
		{
			_buffer[0] = GetIndexSize(key.Length);

			// we add 1 to the data position since we expect to add the position
			// to the current position in the stream, however since we're not advancing
			// the stream at all (we're writing to a buffer) we need to artificially move
			// the position where the data is being stored.
			WriteUInt(1, GetJumpSize(dataPosition - 1));

			_bw.Write(_buffer, 0, sizeof(byte) + sizeof(uint));
			_bw.Write(key);
		}

		public void WriteJump(long jumpTo)
		{
			_buffer[0] = Constants.IndexSeparator;

			// this is to cope with the DatabaseIODevice
			// it's pretty much a hacky workaround :v (the ternary operator)

			// in addition, see the comment in WriteIndex for why we subtract 1 to GetJumpSize
			WriteUInt(1, jumpTo == 0 ? 0u : GetJumpSize(jumpTo - 1));

			_bw.Write(_buffer, 0, sizeof(byte) + sizeof(uint));
		}

		public void WriteValue(byte[] value)
		{
			WriteVariableLength((uint)value.Length);
			_bw.Write(value);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int CalculateValueOffset(byte[] value)
			=> CalculateVariableSize((uint)value.Length)
			+ value.Length;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public long GetPosition() => _stream.Position;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Reset()
		{
			_stream.UpdateCache();
			Seek(sizeof(long));
		}

		public void Flush()
		{
			_bw.Flush();
			_stream.Flush();
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Seek(long position) => _stream.Seek(position, SeekOrigin.Begin);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void SeekEnd() => _stream.Seek(0, SeekOrigin.End);

		private int CalculateVariableSize(uint value)
		{
			var result = 0;
			var currentValue = value;

			do
			{
				currentValue >>= 7;
				result++;
			}
			while (currentValue != 0);

			return result;
		}

		private byte GetIndexSize(int length)
		{
			if (length >= Constants.MaxIndexSize)
			{
				throw new ArgumentException($"Didn't expect length to be longer than {Constants.MaxIndexSize}", nameof(length));
			}

			if (length < 1)
			{
				throw new ArgumentException("Didn't expect length shorter than 1", nameof(length));
			}

			return (byte)length;
		}

		private uint GetJumpSize(long jumpTo)
		{
			var result = jumpTo - GetPosition();

			if (result > uint.MaxValue || result < uint.MinValue)
			{
				throw new ArgumentException
				(
					$"Attempting to jump too far: {jumpTo}, and currently at {GetPosition()} (resulting in a jump of {result})",
					nameof(result)
				);
			}

			return (uint)result;
		}

		private byte PeekByte()
		{
			if (EOF)
			{
				return Constants.EOF;
			}

			return _br.ReadByte();
		}

		private long ReadBeginning()
		{
			Seek(0);

			if (_stream.Length >= 8)
			{
				return _br.ReadInt64();
			}

			// we will create it if it doesn't exist
			_bw.Write(0L);
			return 0;
		}

		private long ReadDownsizedLong() => GetPosition() + _br.ReadInt32();

		private uint ReadVariableLength()
		{
			var bytesRead = 0;
			var totalResult = 0u;
			byte current;

			do
			{
				current = _br.ReadByte();
				var value = (uint)(current & 0b01111111);

				totalResult |= value << 7 * bytesRead;

				bytesRead++;

				if (bytesRead > 5)
				{
					throw new FormatException("Not expected to read more than 5 numbers.");
				}
			}
			while ((current & 0b10000000) != 0);

			return totalResult;
		}

		// https://wiki.vg/Data_types#VarInt_and_VarLong
		// the first bit tells us if we need to read more
		// the other 7 are used to encode the value
		private void WriteVariableLength(uint value)
		{
			var currentValue = value;
			var bufferIndex = 0;

			do
			{
				var read = (byte)(currentValue & 0b01111111);

				currentValue >>= 7;

				if (currentValue != 0)
				{
					read |= 0b10000000;
				}

				_buffer[bufferIndex++] = read;
			}
			while (currentValue != 0);

			_bw.Write(_buffer, 0, bufferIndex);
		}

		// https://source.dot.net/#System.Private.CoreLib/shared/System/IO/BinaryWriter.cs,297
		private void WriteUInt(int offset, uint value)
		{
			_buffer[offset] = (byte)value;
			_buffer[1 + offset] = (byte)(value >> 8);
			_buffer[2 + offset] = (byte)(value >> 16);
			_buffer[3 + offset] = (byte)(value >> 24);
		}
	}
}