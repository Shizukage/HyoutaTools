using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data.SQLite;
using System.IO;
using HyoutaUtils;
using EndianUtils = HyoutaUtils.EndianUtils;

namespace HyoutaTools.Tales.Vesperia.TO8CHTX {
	public struct ChatFileHeader {
		public UInt64 Identify;
		public UInt32 Filesize;
		public UInt32 Lines;
		public UInt32 Unknown;
		public UInt32 TextStart;
		public UInt64 Empty;
	}

	public class ChatFileLine {
		public int Location;

		public ulong NamePointer;
		public ulong[] TextPointers = new ulong[2];
		public uint Unknown;

		public string SName;
		public string[] STexts = new string[2];

		public ulong JPN { get { return TextPointers[0]; } set { TextPointers[0] = value; } }
		public ulong ENG { get { return TextPointers[1]; } set { TextPointers[1] = value; } }
		public string SJPN { get { return STexts[0]; } set { STexts[0] = value; } }
		public string SENG { get { return STexts[1]; } set { STexts[1] = value; } }

		// this field does not actually exist in the game files, used as a hotfix for matching up original and modified files in GenerateWebsite/Database
		public string SNameEnglishNotUsedByGame = null;
	}

	public class ChatFile {
		public ChatFileHeader Header;
		public ChatFileLine[] Lines;

		private EndianUtils.Endianness Endian;
		private TextUtils.GameTextEncoding Encoding;
		private BitUtils.Bitness Bits;
		private bool ReplaceAtWithSpace;

		public ChatFile( string filename, EndianUtils.Endianness endian, TextUtils.GameTextEncoding encoding, BitUtils.Bitness bits, int languageCount, bool replaceAtWithSpace = true ) {
			using ( Stream stream = new FileStream( filename, FileMode.Open, FileAccess.Read ) ) {
				LoadFile( stream, endian, encoding, bits, languageCount, replaceAtWithSpace );
			}
		}

		public ChatFile( Stream file, EndianUtils.Endianness endian, TextUtils.GameTextEncoding encoding, BitUtils.Bitness bits, int languageCount, bool replaceAtWithSpace = true ) {
			LoadFile( file, endian, encoding, bits, languageCount, replaceAtWithSpace );
		}

		private void LoadFile( Stream TO8CHTX, EndianUtils.Endianness endian, TextUtils.GameTextEncoding encoding, BitUtils.Bitness bits, int languageCount, bool replaceAtWithSpace ) {
			Endian = endian;
			Encoding = encoding;
			Bits = bits;
			ReplaceAtWithSpace = replaceAtWithSpace;

			Header = new ChatFileHeader();

			ulong pos = (ulong)TO8CHTX.Position;
			Header.Identify = TO8CHTX.ReadUInt64().FromEndian( endian );
			Header.Filesize = TO8CHTX.ReadUInt32().FromEndian( endian );
			Header.Lines = TO8CHTX.ReadUInt32().FromEndian( endian );
			Header.Unknown = TO8CHTX.ReadUInt32().FromEndian( endian );
			Header.TextStart = TO8CHTX.ReadUInt32().FromEndian( endian );
			Header.Empty = TO8CHTX.ReadUInt64().FromEndian( endian );

			Lines = new ChatFileLine[Header.Lines];

			int entrySize = (int)( 4 + ( languageCount + 1 ) * bits.NumberOfBytes() );
			for ( int i = 0; i < Header.Lines; i++ ) {
				Lines[i] = new ChatFileLine();
				Lines[i].Location = 0x20 + i * entrySize;
				Lines[i].NamePointer = TO8CHTX.ReadUInt( bits, endian );
				Lines[i].TextPointers = new ulong[languageCount];
				for ( int j = 0; j < languageCount; ++j ) {
					Lines[i].TextPointers[j] = TO8CHTX.ReadUInt( bits, endian );
				}
				Lines[i].Unknown = TO8CHTX.ReadUInt32().FromEndian( endian );

				Lines[i].SName = TO8CHTX.ReadNulltermStringFromLocationAndReset( (long)( pos + Lines[i].NamePointer + Header.TextStart ), encoding );
				Lines[i].STexts = new string[languageCount];
				for ( int j = 0; j < languageCount; ++j ) {
					string text = TO8CHTX.ReadNulltermStringFromLocationAndReset( (long)( pos + Lines[i].TextPointers[j] + Header.TextStart ), encoding );
					Lines[i].STexts[j] = ReplaceAtWithSpace ? text.Replace( '@', ' ' ) : text;
				}
			}
		}

		public void GetSQL( String ConnectionString ) {
			SQLiteConnection Connection = new SQLiteConnection( ConnectionString );
			Connection.Open();

			using ( SQLiteTransaction Transaction = Connection.BeginTransaction() )
			using ( SQLiteCommand Command = new SQLiteCommand( Connection ) ) {
				Command.CommandText = "SELECT english, PointerRef FROM Text ORDER BY PointerRef";
				SQLiteDataReader r = Command.ExecuteReader();
				while ( r.Read() ) {
					String SQLText;

					try {
						SQLText = r.GetString( 0 ).Replace( "''", "'" );
					} catch ( System.InvalidCastException ) {
						SQLText = null;
					}

					int PointerRef = r.GetInt32( 1 );

					if ( !String.IsNullOrEmpty( SQLText ) ) {
						if ( PointerRef % 16 == 0 ) {
							int i = ( PointerRef / 16 ) - 2;
							Lines[i].SName = SQLText;
						} else if ( PointerRef % 16 == 4 ) {
							int i = ( ( PointerRef - 4 ) / 16 ) - 2;
							Lines[i].SENG = SQLText;
						}
					}
				}

				Transaction.Rollback();
			}
			return;
		}

		public byte[] Serialize() {
			List<byte> Serialized = new List<byte>( (int)Header.Filesize );

			Serialized.AddRange( SerializeUInt64( Header.Identify ) );
			Serialized.AddRange( SerializeUInt32( Header.Filesize ) );
			Serialized.AddRange( SerializeUInt32( Header.Lines ) );
			Serialized.AddRange( SerializeUInt32( Header.Unknown ) );
			Serialized.AddRange( SerializeUInt32( Header.TextStart ) );
			Serialized.AddRange( SerializeUInt64( Header.Empty ) );

			foreach ( ChatFileLine Line in Lines ) {
				Serialized.AddRange( SerializeUInt( Line.NamePointer ) );
				for ( int i = 0; i < Line.TextPointers.Length; ++i ) {
					Serialized.AddRange( SerializeUInt( Line.TextPointers[i] ) );
				}
				Serialized.AddRange( SerializeUInt32( Line.Unknown ) );
			}

			byte ByteNull = 0x00;

			foreach ( ChatFileLine Line in Lines ) {
				Serialized.AddRange( StringToBytes( Line.SName ) );
				Serialized.Add( ByteNull );
				for ( int i = 0; i < Line.STexts.Length; ++i ) {
					Serialized.AddRange( StringToBytes( Line.STexts[i] ) );
					Serialized.Add( ByteNull );
				}
			}

			return Serialized.ToArray();
		}

		public void RecalculatePointers() {
			uint Size = Header.TextStart;
			for ( int i = 0; i < Lines.Length; i++ ) {
				Lines[i].NamePointer = Size - Header.TextStart;
				Size += (uint)StringToBytes( Lines[i].SName ).Length;
				Size++;
				for ( int j = 0; j < Lines[i].TextPointers.Length; ++j ) {
					Lines[i].TextPointers[j] = Size - Header.TextStart;
					Size += (uint)StringToBytes( Lines[i].STexts[j] ).Length;
					Size++;
				}
			}

			Header.Filesize = Size;
		}

		private byte[] StringToBytes( string s ) {
			if ( s == null ) {
				s = string.Empty;
			}

			switch ( Encoding ) {
				case TextUtils.GameTextEncoding.ShiftJIS:
					return TextUtils.StringToBytesShiftJis( s );
				case TextUtils.GameTextEncoding.UTF8:
					return System.Text.Encoding.UTF8.GetBytes( s );
				case TextUtils.GameTextEncoding.ASCII:
					return System.Text.Encoding.ASCII.GetBytes( s );
				default:
					throw new Exception( "Unsupported encoding for TO8CHTX serialization: " + Encoding );
			}
		}

		private byte[] SerializeUInt( ulong value ) {
			switch ( Bits ) {
				case BitUtils.Bitness.B64:
					return SerializeUInt64( value );
				case BitUtils.Bitness.B32:
					return SerializeUInt32( (uint)value );
				default:
					throw new Exception( "Unsupported bitness for TO8CHTX serialization: " + Bits );
			}
		}

		private byte[] SerializeUInt32( uint value ) {
			return System.BitConverter.GetBytes( Endian == EndianUtils.Endianness.BigEndian ? EndianUtils.SwapEndian( value ) : value );
		}

		private byte[] SerializeUInt64( ulong value ) {
			return System.BitConverter.GetBytes( Endian == EndianUtils.Endianness.BigEndian ? EndianUtils.SwapEndian( value ) : value );
		}
	}
}
