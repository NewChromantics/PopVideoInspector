using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;


//	nice reference; http://fabiensanglard.net/mobile_progressive_playback/index.php
public class Mp4Parser
{
	//	use of long = file position
	public struct TAtom
	{
		public const long HeaderSize = 8;
		public string Fourcc;
		public long FileOffset;
		public long DataSize;
		public int lvl;

		public void Set(byte[] Data8)
		{
			//Size = BitConverter.ToUInt32(new byte[] { Data8[0], Data8[1], Data8[2], Data8[3] }.Reverse().ToArray(), 0);
			int sz = Data8[3] << 0;
			sz += Data8[2] << 8;
			sz += Data8[1] << 16;
			sz += Data8[0] << 24;
			DataSize = (uint)sz;

			Fourcc = Encoding.ASCII.GetString(new byte[] { Data8[4], Data8[5], Data8[6], Data8[7] });
		}
	}

	public static string[] GetLvlStrings()
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

	static uint GetFourcc(string FourccString)
	{
		var Bytes = Encoding.ASCII.GetBytes(FourccString);
		return GetFourcc(Bytes[0], Bytes[1], Bytes[2], Bytes[3]);
	}

	static uint GetFourcc(byte a,byte b,byte c,byte d)
	{
		int sz = d << 0;
		sz += c << 8;
		sz += b << 16;
		sz += a << 24;
		return (uint)sz;
	}


	static string[] lvltypes = null;
	public static string[] GetFourccs()
	{
		if (lvltypes == null)
			lvltypes = GetLvlStrings();
		return lvltypes;
	}



	static bool StringContain(string String, char[] Fourcc)
	{
		for (int f = 0; f < Fourcc.Length; f++)
		{
			if (Fourcc[f] != String[f])
				return false;
		}
		return true;
	}
	
	static bool StringsContain(string[] Strings,char[] Fourcc)
	{
		for (int i = 0; i < Strings.Length;	i++ )
		{
			if (StringContain(Strings[i], Fourcc))
				return true;
		}
		return false;
	}

	static int? GetLvl(string AtomType)
	{
		var LvlTypes = GetLvlStrings();
		for (int lvl = 0; lvl < LvlTypes.Count(); lvl++)
		{
			var lvlxtypes = LvlTypes[lvl];
			if (lvlxtypes.Contains(AtomType))
				return lvl;
		}
		return null;
	}



	static void DecodeAtomRecursive(System.Action<TAtom> EnumAtom, TAtom Moov,byte[] FileData)
	{
		var AtomStart = Moov.FileOffset + TAtom.HeaderSize;
		while (true)
		{
			var NextAtom = GetNextAtom(FileData, (int)AtomStart,true);
			if (NextAtom == null)
				break;

			var Atom = NextAtom.Value;

			//	moov atom: The metadatas, containing codec description used in the mdata atom.
			//	It also contains sub-atoms "stco" and "co64" which are absolute pointers to keyframes in the mdata atom.
			EnumAtom(Atom);

			DecodeAtomRecursive(EnumAtom, Atom, FileData);

			AtomStart = Atom.FileOffset + Atom.DataSize;
		}
	}

	static void DecodeAtomChildren(System.Action<TAtom> EnumAtom, TAtom Moov, byte[] FileData)
	{
		//	decode moov children (mvhd, trak, udta)
		var MoovEnd = Moov.FileOffset + Moov.DataSize;
		for (var AtomStart=Moov.FileOffset+TAtom.HeaderSize; AtomStart <MoovEnd; AtomStart += 0)
		{
			var NextAtom = GetNextAtom(FileData, AtomStart, false);
			if (NextAtom == null)
				break;
			var Atom = NextAtom.Value;
			Debug.Log("Found " + Atom.Fourcc);
			try
			{
				EnumAtom(Atom);
			}
			catch(System.Exception e)
			{}
			AtomStart = Atom.FileOffset + Atom.DataSize;
		}
	}

	static void DecodeAtomMoov(System.Action<TAtom> EnumAtom, TAtom Moov, byte[] FileData)
	{
		DecodeAtomChildren(EnumAtom, Moov, FileData);
	}
		
	static void DecodeAtom(System.Action<TAtom> EnumAtom, TAtom Atom,byte[] FileData)
	{
		if ( Atom.Fourcc == "moov" )
		{
			DecodeAtomMoov(EnumAtom, Atom,FileData);
		}
	}

	static TAtom? GetNextAtom(byte[] Data,long Start,bool WalkOverData)
	{
		var AtomData = new byte[TAtom.HeaderSize];
		for (var i = Start; i <Data.Length-TAtom.HeaderSize; i++)
		{
			//	let it throw(TM)
			var Atom = new TAtom();
			for (int ad = 0; ad < AtomData.Count(); ad++)
				AtomData[ad] = Data[i + ad];
			Atom.Set(AtomData);

			//	if valid fourcc
			var lvl = GetLvl(Atom.Fourcc);
			if (!lvl.HasValue)
			{
				if (!WalkOverData)
					break;
				continue;
			}

			Atom.FileOffset = (uint)i;
			Atom.FileOffset = (uint)i;
			Atom.lvl = lvl.Value;
			return Atom;
		}
		return null;
	}

	//	parse as tree
	public void ParseTree(string path, System.Action<TAtom> EnumAtom)
	{
		byte[] FileData = File.ReadAllBytes(path);
		var Length = FileData.Length;


		//	read first atom
		long i = 0;
		while ( i < FileData.Length )
		{
			var NextAtom = GetNextAtom(FileData, i,true);
			if (NextAtom == null)
				break;

			var Atom = NextAtom.Value;
			try
			{
				EnumAtom(Atom);

				DecodeAtom(EnumAtom,Atom,FileData);
			}
			catch(System.Exception e)
			{
				Debug.LogException(e);
			}

			if (Atom.DataSize == 1)
				throw new System.Exception("Extended Atom size found, not yet handled");
			//i = (int)(Atom.Offset + Atom.Length + 1);
			var NextPosition = Atom.FileOffset + Atom.DataSize;
			if (i == NextPosition)
				throw new System.Exception("Infinite loop averted");
			i = (int)NextPosition;
		}
	}




	public void parserFunction(string path,System.Action<TAtom> EnumAtom)
	{
		byte[] bytes = File.ReadAllBytes(path);
		UInt32 length = Convert.ToUInt32(bytes.Length);
		int Skipped = 0;
		int Found = 0;


		var AtomData = new byte[TAtom.HeaderSize];
		for (long i=0;	i<length-TAtom.HeaderSize;	i++)
		{
			//	let it throw(TM)
			var Atom = new TAtom();
			try
			{
				for (int ad=0;	ad<AtomData.Count();	ad++)
					AtomData[ad]=bytes[i + ad];
				Atom.Set(AtomData);


				//	if va
				var lvl = GetLvl(Atom.Fourcc);
				if ( !lvl.HasValue )
				{
					//	invalid atom
					//	missing lvl
					Skipped++;
					continue;
				}

				//	not always 8 aligned...
				if ((i % TAtom.HeaderSize) != 0)
					Debug.Log("i offset: " + (i % TAtom.HeaderSize));
			
				Found++;
				if ( Atom.DataSize < length )
				{
					Atom.FileOffset = i;
					Atom.lvl = lvl.Value;
					EnumAtom(Atom);
				}

				//	dont re-read these bytes
				i += TAtom.HeaderSize - 1;
			}
			catch (Exception ex)
			{
				Debug.LogException(ex);
				Debug.LogError("Tried to add "+Atom.Fourcc +" offset="+i);
			}

		}

		Debug.Log("Found=" + Found + " skipped=" + Skipped);
	}
}
