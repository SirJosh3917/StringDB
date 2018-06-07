﻿namespace StringDB {
#if DEBUG
	/// <summary>Constants</summary>
	public
#else
	internal
#endif
		static class Consts {

		/// <summary>Used for seperating indexes from data. This is why you can't have indexes with lengths more then 253.</summary>
		public const byte IndexSeperator = 0xFF;

		/// <summary>Used to tell if the next value is a byte</summary>
		public const byte IsByteValue = 0x01;

		/// <summary>Used to tell if the next value is a ushort</summary>
		public const byte IsUShortValue = 0x02;

		/// <summary>Used to tell if the next value is a uint</summary>
		public const byte IsUIntValue = 0x03;

		/// <summary>Used to tell if the next value is a ulong</summary>
		public const byte IsULongValue = 0x04;
	}
}