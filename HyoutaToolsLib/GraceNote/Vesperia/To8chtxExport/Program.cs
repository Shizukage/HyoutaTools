using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HyoutaTools.Tales.Vesperia.TO8CHTX;
using HyoutaUtils;

namespace HyoutaTools.GraceNote.Vesperia.TO8CHTXExport {
	class Program {
		public static int Execute( List<string> args ) {
			String Filename;
			String Database;
			String NewFilename;
			TextUtils.GameTextEncoding encoding = TextUtils.GameTextEncoding.ShiftJIS;
			int languageCount = 2;

			if ( args.Count != 3 && args.Count != 5 ) {
				Console.WriteLine( "Usage: GraceNote_TO8CHTX ChatFilename DBFilename NewChatFilename [Encoding] [LanguageCount]" );
				Console.WriteLine( "  Encoding: ShiftJIS or UTF8" );
				return -1;
			} else {
				Filename = args[0];
				Database = args[1];
				NewFilename = args[2];
			}

			if ( args.Count == 5 ) {
				if ( !TryParseEncoding( args[3], out encoding ) ) {
					Console.WriteLine( "Unknown encoding: " + args[3] );
					return -1;
				}

				if ( !int.TryParse( args[4], out languageCount ) || languageCount <= 0 ) {
					Console.WriteLine( "LanguageCount must be a positive integer." );
					return -1;
				}
			}

			ChatFile c = new ChatFile( Filename, EndianUtils.Endianness.BigEndian, encoding, BitUtils.Bitness.B32, languageCount );

			c.GetSQL( "Data Source=" + Database );

			c.RecalculatePointers();
			System.IO.File.WriteAllBytes( NewFilename, c.Serialize() );

			return 0;
		}

		private static bool TryParseEncoding( string encodingString, out TextUtils.GameTextEncoding encoding ) {
			switch ( encodingString.ToUpperInvariant() ) {
				case "SHIFTJIS":
				case "SJIS":
					encoding = TextUtils.GameTextEncoding.ShiftJIS;
					return true;
				case "UTF8":
				case "UTF-8":
					encoding = TextUtils.GameTextEncoding.UTF8;
					return true;
				default:
					encoding = TextUtils.GameTextEncoding.ShiftJIS;
					return false;
			}
		}
	}
}
