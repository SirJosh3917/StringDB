﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace StringDB.Reader {
	/// <inheritdoc/>
	public class StreamReader : IReader {
		/// <summary>Create a new StreamReader.</summary>
		/// <param name="streamUse">The stream to read . You may need to call the Load() void to set the indexChain data.</param>
		public StreamReader(Stream streamUse) {
			this._stream = streamUse;
			this._br = new BinaryReader(this._stream);
		}

		/// <summary>Used for seperating indexes from data. This is why you can't have indexes with lengths more then 253.</summary>
		public const byte IndexSeperator = 0xFF;

		//public implementations of stuff

		#region public implementations
		/// <inheritdoc/>
		public string[] GetIndexes() => _Indexes();/// <inheritdoc/>

		public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => new ReaderEnumerator(this, this.FirstIndex());/// <inheritdoc/>
		IEnumerator IEnumerable.GetEnumerator() => new ReaderEnumerator(this, this.FirstIndex());/// <inheritdoc/>

		public string GetValueOf(IReaderInteraction r, bool doSeek = true) => GetValueOf(r.Index, doSeek, r.QuickSeek);/// <inheritdoc/>
		public string GetValueOf(string index, bool doSeek = true, ulong quickSeek = 0) => _ValueOf(index, doSeek, quickSeek);/// <inheritdoc/>

		public string[] GetValuesOf(IReaderInteraction r, bool doSeek = true) => GetValuesOf(r.Index, doSeek, r.QuickSeek);/// <inheritdoc/>
		public string[] GetValuesOf(string index, bool doSeek = true, ulong quickSeek = 0) => _ValuesOf(index, doSeek, quickSeek);/// <inheritdoc/>

		public bool IsIndexAfter(IReaderInteraction r, bool doSeek = true) => IsIndexAfter(r.Index, doSeek, r.QuickSeek);/// <inheritdoc/>
		public bool IsIndexAfter(string index, bool doSeek = true, ulong quickSeek = 0) => _IsIndexAfter(index, doSeek, quickSeek);/// <inheritdoc/>

		public IReaderInteraction IndexAfter(IReaderInteraction r, bool doSeek = true) => IndexAfter(r.Index, doSeek, r.QuickSeek);/// <inheritdoc/>
		public IReaderInteraction IndexAfter(string index, bool doSeek = true, ulong quickSeek = 0) => _IndexAfter(index, doSeek, quickSeek);/// <inheritdoc/>

		public IReaderInteraction FirstIndex() => IndexAfter(null, true, 0);/// <inheritdoc/>

		public IReaderChain GetReaderChain() => _ReadChain();
		#endregion

		//nitty gritty part

		private Stream _stream { get; set; }
		private BinaryReader _br { get; set; }

		private string _ValueOf(string index, bool doSeek, ulong quickSeek) {
			var _curPos = this._br.BaseStream.Position;

			var i = _ReadIndex(doSeek, quickSeek);

			while (i.Index != index) {
				i = _ReadIndex();
				if (i == null)
					return null;
			}

			return _ReadValue(i);
		}

		private string[] _ValuesOf(string index, bool doSeek, ulong quickSeek) {
			var _curPos = this._br.BaseStream.Position;

			var i = _ReadIndex(doSeek, quickSeek);

			var valuesOf = new List<string>();

			var lastpos = this._br.BaseStream.Position;

			if(i != null)
				if (i.Index == index)
					valuesOf.Add(_ReadValue(i));

			while (i.Index != null) {
				i = _ReadIndex(true, (ulong)lastpos);
				if (i == null)
					break;

				lastpos = this._br.BaseStream.Position;

				if (i.Index == index)
					valuesOf.Add(_ReadValue(i));
			}

			return valuesOf.ToArray();
		}

		private bool _IsIndexAfter(string index, bool doSeek, ulong quickSeek) {
			var _curPos = this._br.BaseStream.Position;

			var rs = _ReadIndex(doSeek, quickSeek);

			while (rs.Index != index) {
				rs = _ReadIndex();
				if (rs == null)
					return false;
			}

			rs = _ReadIndex();

			this._br.BaseStream.Seek(_curPos, SeekOrigin.Begin);

			return rs == null;
		}

		private IReaderInteraction _IndexAfter(string index, bool doSeek, ulong quickSeek) {
			var _curPos = this._br.BaseStream.Position;

			var rs = _ReadIndex(doSeek, quickSeek);

			if (rs == null)
				return null;

			if (index == null)
				return rs;

			while (rs.Index != index) {
				rs = _ReadIndex();
				if (rs == null)
					return null;
			}

			rs = _ReadIndex();

			this._br.BaseStream.Seek(_curPos, SeekOrigin.Begin);

			if (rs == null)
				return null;

			return rs;
		}

		private string[] _Indexes() {
			var _curPos = this._br.BaseStream.Position;

			var indexes = new List<string>();

			var rs = _ReadIndex(true, 0);

			while (rs != null) {
				indexes.Add(rs.Index);
				rs = _ReadIndex();
			}

			this._br.BaseStream.Seek(_curPos, SeekOrigin.Begin);

			return indexes.ToArray();
		}

		//the actual work part of it

		private IReaderInteraction _ReadIndex(bool restart = false, ulong start = 0) {
			if (restart)
				this._br.BaseStream.Seek((long)start, SeekOrigin.Begin);

			var b = this._br.ReadByte();

			while (b == IndexSeperator) { //hippety hoppity get off my property
				var seekTo = (long)(this._br.ReadUInt64());

				if (seekTo == 0)
					return null;

				this._br.BaseStream.Seek(seekTo, SeekOrigin.Begin);
				b = this._br.ReadByte();
			}

			var dataPos = this._br.ReadUInt64();
			var indexName = Encoding.UTF8.GetString(this._br.ReadBytes((int)b));

			return new ReaderInteraction(
					indexName, (ulong)this._br.BaseStream.Position, dataPos
				);
		}

		private string _ReadValue(IReaderInteraction readerInteraction) {
			this._br.BaseStream.Seek((long)readerInteraction.DataPos, SeekOrigin.Begin);

			return Encoding.UTF8.GetString(
					this._br.ReadBytes(this._br.ReadInt32())
				);
		}

		private string _ReadValue(ulong location) {
			this._br.BaseStream.Seek((long)location, SeekOrigin.Begin);

			return Encoding.UTF8.GetString(
					this._br.ReadBytes(this._br.ReadInt32())
				);
		}

		private IReaderChain _ReadChain() {
			this._br.BaseStream.Seek((long)0, SeekOrigin.Begin);

			ulong ic = 0;
			ulong icw = 0;

			var shouldContinueLook = true;
			var b = this._br.ReadByte();

			while (shouldContinueLook) {
				while (b == IndexSeperator) { //hippety hoppity get off my property
					var p = this._br.BaseStream.Position;
					var seekTo = (long)(this._br.ReadUInt64());

					if (seekTo == 0)
						shouldContinueLook = false;
					else {
						ic = (ulong)seekTo;
						icw = (ulong)p;

						this._br.BaseStream.Seek(seekTo, SeekOrigin.Begin);
					}

					b = this._br.ReadByte();
				}

				//stuff to pass the reader
				this._br.ReadUInt64();
				this._br.ReadBytes((int)b);

				if (this._br.BaseStream.Position == this._br.BaseStream.Length)
					break;

				b = this._br.ReadByte();
			}

			return new ReaderChain(ic, icw);
		}
	}
}