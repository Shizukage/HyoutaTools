using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HyoutaTools.Tales.Vesperia.TO8CHTX;
using HyoutaUtils;

namespace HyoutaTools.GraceNote.Vesperia.TO8CHTXImport {
	class Program {
		public static int Execute( List<string> args ) {
			if ( args.Count != 3 && args.Count != 5 ) {
				Console.WriteLine( "Usage: TO8CHTX_GraceNote ChatFilename NewDBFilename GracesJapanese [Encoding] [LanguageCount]" );
				Console.WriteLine( "  Encoding: ShiftJIS or UTF8" );
				return -1;
			}

			String Filename = args[0];
			String NewDB = args[1];
			String GracesDB = args[2];
			TextUtils.GameTextEncoding encoding = TextUtils.GameTextEncoding.ShiftJIS;
			int languageCount = 2;

			if ( args.Count == 5 ) {
				if ( !TryParseEncoding( args[3], out encoding ) ) {
					Console.WriteLine( "Unknown encoding: " + args[3] );
					return -1;
				}

				if ( !int.TryParse( args[4], out languageCount ) || languageCount < 2 ) {
					Console.WriteLine( "LanguageCount must be an integer >= 2 for GraceNote import." );
					return -1;
				}
			}

			ChatFile c = new ChatFile( Filename, EndianUtils.Endianness.BigEndian, encoding, BitUtils.Bitness.B32, languageCount );

			GraceNoteUtil.GenerateEmptyDatabase( NewDB );

			List<GraceNoteDatabaseEntry> Entries = new List<GraceNoteDatabaseEntry>( c.Lines.Length * 2 );
			foreach ( ChatFileLine Line in c.Lines ) {

				String EnglishText;
				int EnglishStatus;
				if ( Line.SENG == "Dummy" || Line.SENG == "" ) {
					EnglishText = Line.SJPN;
					EnglishStatus = 0;
				} else {
					EnglishText = Line.SENG;
					EnglishStatus = 1;
				}

				Entries.Add( new GraceNoteDatabaseEntry( Line.SName, Line.SName, "", 1, Line.Location, "", 0 ) );
				Entries.Add( new GraceNoteDatabaseEntry( Line.SJPN, EnglishText, "", EnglishStatus, Line.Location + 4, "", 0 ) );
			}

			GraceNoteDatabaseEntry.InsertSQL( Entries.ToArray(), "Data Source=" + NewDB, "Data Source=" + GracesDB );
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
