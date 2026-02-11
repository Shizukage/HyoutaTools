using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HyoutaUtils;
using EndianUtils = HyoutaUtils.EndianUtils;

namespace HyoutaTools.Tales.Vesperia.TO8CHTX {
	public static class TxtTool {
		public static int ExecuteExtract( List<string> args ) {
			if ( args.Count < 2 || args.Count > 3 ) {
				Console.WriteLine( "Usage: Tales.Vesperia.TO8CHTX.TxtExtract InputTO8CHTX OutputTxt [Encoding]" );
				Console.WriteLine( "  Encoding: ShiftJIS or UTF8 (default UTF8)" );
				return -1;
			}

			string input = args[0];
			string output = args[1];
			TextUtils.GameTextEncoding encoding = TextUtils.GameTextEncoding.UTF8;
			if ( args.Count >= 3 && !TryParseEncoding( args[2], out encoding ) ) {
				Console.WriteLine( "Unknown encoding: " + args[2] );
				return -1;
			}

			DetermineLayout( input, out BitUtils.Bitness bits, out int languageCount );
			ChatFile c = new ChatFile( input, EndianUtils.Endianness.BigEndian, encoding, bits, languageCount, false );

			using ( var writer = new StreamWriter( output, false, new UTF8Encoding( false ) ) ) {
				writer.WriteLine( "# TO8CHTX TXT v2" );
				writer.WriteLine( "# Encoding=" + encoding );
				writer.WriteLine( "# AutoDetectedBitness=" + bits );
				writer.WriteLine( "# AutoDetectedLanguageCount=" + languageCount );
				writer.WriteLine();

				for ( int i = 0; i < c.Lines.Length; ++i ) {
					var line = c.Lines[i];
					writer.WriteLine( "=== LINE " + i + " ===" );
					WriteBlock( writer, "NAME", line.SName );
					for ( int lang = 0; lang < line.STexts.Length; ++lang ) {
						WriteBlock( writer, string.Format( "L{0:D2}", lang ), line.STexts[lang] );
					}
					writer.WriteLine();
				}
			}

			return 0;
		}

		public static int ExecutePack( List<string> args ) {
			if ( args.Count < 3 || args.Count > 4 ) {
				Console.WriteLine( "Usage: Tales.Vesperia.TO8CHTX.TxtPack InputTO8CHTX InputTxt OutputTO8CHTX [Encoding]" );
				Console.WriteLine( "  Encoding: ShiftJIS or UTF8 (default UTF8)" );
				return -1;
			}

			string inputTo8chtx = args[0];
			string inputTxt = args[1];
			string outputTo8chtx = args[2];
			TextUtils.GameTextEncoding encoding = TextUtils.GameTextEncoding.UTF8;
			if ( args.Count >= 4 && !TryParseEncoding( args[3], out encoding ) ) {
				Console.WriteLine( "Unknown encoding: " + args[3] );
				return -1;
			}

			DetermineLayout( inputTo8chtx, out BitUtils.Bitness bits, out int languageCount );
			ChatFile c = new ChatFile( inputTo8chtx, EndianUtils.Endianness.BigEndian, encoding, bits, languageCount, false );

			ApplyTextFile( c, inputTxt );
			c.RecalculatePointers();
			File.WriteAllBytes( outputTo8chtx, c.Serialize() );

			return 0;
		}

		private static void DetermineLayout( string path, out BitUtils.Bitness bits, out int languageCount ) {
			using ( var fs = new FileStream( path, FileMode.Open, FileAccess.Read ) ) {
				if ( fs.Length < 0x20 ) {
					throw new Exception( "File too small for TO8CHTX header." );
				}

				string magic = fs.ReadAsciiNullterm( 8 );
				if ( magic != "TO8CHTX" ) {
					throw new Exception( "Not a TO8CHTX file." );
				}

				uint fileSizeInHeader = fs.ReadUInt32().FromEndian( EndianUtils.Endianness.BigEndian );
				uint lines = fs.ReadUInt32().FromEndian( EndianUtils.Endianness.BigEndian );
				fs.Position += 4; // unknown
				uint textStart = fs.ReadUInt32().FromEndian( EndianUtils.Endianness.BigEndian );

				if ( lines == 0 ) {
					throw new Exception( "TO8CHTX has 0 lines." );
				}
				if ( textStart < 0x20 ) {
					throw new Exception( "Invalid TO8CHTX text start." );
				}

				long tableSize = (long)textStart - 0x20;
				if ( tableSize <= 0 || tableSize % lines != 0 ) {
					throw new Exception( "Could not derive TO8CHTX layout from header/table." );
				}

				long entrySize = tableSize / lines;
				if ( TryDecodeLayout( entrySize, BitUtils.Bitness.B64, out languageCount ) ) {
					bits = BitUtils.Bitness.B64;
				} else if ( TryDecodeLayout( entrySize, BitUtils.Bitness.B32, out languageCount ) ) {
					bits = BitUtils.Bitness.B32;
				} else {
					throw new Exception( "Could not determine TO8CHTX bitness/language count from entry size." );
				}

				long headerFileSize = fileSizeInHeader;
				if ( headerFileSize > 0 && headerFileSize > fs.Length ) {
					throw new Exception( "TO8CHTX header filesize is larger than actual file." );
				}
			}
		}

		private static bool TryDecodeLayout( long entrySize, BitUtils.Bitness bits, out int languageCount ) {
			long bytes = bits.NumberOfBytes();
			long rem = entrySize - 4;
			if ( rem <= 0 || rem % bytes != 0 ) {
				languageCount = 0;
				return false;
			}
			long pointerCount = rem / bytes;
			languageCount = (int)pointerCount - 1;
			if ( languageCount <= 0 ) {
				languageCount = 0;
				return false;
			}
			return true;
		}

		private static void ApplyTextFile( ChatFile c, string textPath ) {
			string[] lines = File.ReadAllLines( textPath, Encoding.UTF8 );
			int currentLineIndex = -1;
			string currentBlock = null;
			StringBuilder sb = new StringBuilder();

			Action flushBlock = () => {
				if ( currentLineIndex < 0 || currentBlock == null ) {
					return;
				}
				if ( currentLineIndex >= c.Lines.Length ) {
					throw new Exception( "Text file references line " + currentLineIndex + " but file only has " + c.Lines.Length + " lines." );
				}

				string text = sb.ToString();
				if ( currentBlock == "NAME" ) {
					c.Lines[currentLineIndex].SName = text;
				} else {
					if ( currentBlock.Length < 2 || currentBlock[0] != 'L' ) {
						throw new Exception( "Unknown block [" + currentBlock + "] on line " + currentLineIndex + "." );
					}
					if ( !int.TryParse( currentBlock.Substring( 1 ), out int languageIndex ) ) {
						throw new Exception( "Invalid language block [" + currentBlock + "] on line " + currentLineIndex + "." );
					}
					if ( languageIndex < 0 || languageIndex >= c.Lines[currentLineIndex].STexts.Length ) {
						throw new Exception( "Language index out of range in block [" + currentBlock + "] on line " + currentLineIndex + "." );
					}
					c.Lines[currentLineIndex].STexts[languageIndex] = text;
				}
			};

			for ( int i = 0; i < lines.Length; ++i ) {
				string line = lines[i];
				if ( line.StartsWith( "#" ) && currentBlock == null ) {
					continue;
				}
				if ( line.StartsWith( "=== LINE " ) && line.EndsWith( " ===" ) && currentBlock == null ) {
					currentLineIndex = int.Parse( line.Substring( "=== LINE ".Length, line.Length - "=== LINE ".Length - " ===".Length ) );
					continue;
				}

				if ( currentBlock == null ) {
					if ( line.StartsWith( "[" ) && line.EndsWith( "]" ) && !line.StartsWith( "[/" ) ) {
						currentBlock = line.Substring( 1, line.Length - 2 );
						sb.Clear();
						continue;
					} else if ( string.IsNullOrWhiteSpace( line ) ) {
						continue;
					} else {
						throw new Exception( "Unexpected line outside block at text line " + ( i + 1 ) + ": " + line );
					}
				} else {
					if ( line == "[/" + currentBlock + "]" ) {
						flushBlock();
						currentBlock = null;
						sb.Clear();
					} else {
						if ( sb.Length > 0 ) {
							sb.Append( "\n" );
						}
						sb.Append( line );
					}
				}
			}

			if ( currentBlock != null ) {
				throw new Exception( "Unclosed block [" + currentBlock + "] at end of text file." );
			}
		}

		private static void WriteBlock( StreamWriter writer, string label, string value ) {
			writer.WriteLine( "[" + label + "]" );
			if ( !string.IsNullOrEmpty( value ) ) {
				writer.WriteLine( value.Replace( "\r\n", "\n" ).Replace( "\r", "\n" ) );
			}
			writer.WriteLine( "[/" + label + "]" );
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
