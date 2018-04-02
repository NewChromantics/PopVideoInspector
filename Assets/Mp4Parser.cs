using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

public class Mp4Parser
{
	public struct Header
	{
		public string Atom;
		public uint Offset;
		public uint Length;
		public int lvl;
	}
	
	public static string PrintHeader(string atomType, UInt32 size, UInt32 offset, int lvl)
	{
		string tab = "";
		for (int i = 0; i < lvl; i++)
		{
			tab += "\t";
		}
		return tab + "[" + atomType + ", size: " + size + ", offset: " + offset + "]";
	}

	public static string[] getTypes()
	{
		var types = new string[6];
		types[0] = "ftyp,moov,mdat";
		types[1] = "mvhd,trak,udta";
		types[2] = "tkhd,edts,mdia,meta,covr,©nam";
		types[3] = "mdhd,hdlr,minf";
		types[4] = "smhd,vmhd,dinf,stbl";
		types[5] = "stsd,stts,stss,ctts,stsc,stsz,stco";
		return types;
	}

	public void parserFunction(string path,System.Action<Header> EnumAtom)
	{
		Console.WriteLine("Start");
		var types = getTypes();
		byte[] bytes = File.ReadAllBytes(path);
		UInt32 length = Convert.ToUInt32(bytes.Length);
		UInt32 offset = 0;
		UInt32 j = 0;

		while ((j + 8) < length)
		{
			try
			{
				UInt32 i = j;
				UInt32 atomSize = BitConverter.ToUInt32(new byte[] { bytes[i], bytes[++i], bytes[++i], bytes[++i] }.Reverse().ToArray(), 0);
				string atomType = Encoding.ASCII.GetString(new byte[] { bytes[++i], bytes[++i], bytes[++i], bytes[++i] });
				for (int lvl = 0; lvl < 6; lvl++)
				{
					if ((atomSize < length) && types[lvl].Contains(atomType))
					{
						var Header = new Header();
						Header.Atom = atomType;
						Header.Offset = offset;
						Header.Length = length;
						Header.lvl = lvl;
						EnumAtom(Header);
					}
				}

			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.ToString());
			}
			j++;
			offset++;
		}
		Console.WriteLine("Parsing is finished");
	}
}
