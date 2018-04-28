using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/*	
	I've called this mpeg4, but it's all based on the "original" quicktime atom/basemediafile format
	https://developer.apple.com/library/content/documentation/QuickTime/QTFF/QTFFChap2/qtff2.html

	the generic atom stuff is in PopX.Atom
*/
namespace PopX
{
	public static class Mpeg4
	{
		public static int Get24(byte a, byte b, byte c) { return PopX.Atom.Get24(a, b, c); }
		public static int Get32(byte a, byte b, byte c, byte d) { return PopX.Atom.Get32(a, b, c,d); }
		public static int Get64(byte a, byte b, byte c, byte d, byte e, byte f, byte g, byte h) { return PopX.Atom.Get64(a, b, c,d,e,f,g,h); }


		//	known mpeg4 atoms
		/*
			types[0] = "ftyp,moov,mdat";
			types[1] = "mvhd,trak,udta";
			types[2] = "tkhd,edts,mdia,meta,covr,©nam";
			types[3] = "mdhd,hdlr,minf";
			types[4] = "smhd,vmhd,dinf,stbl";
			types[5] = "stsd,stts,stss,ctts,stsc,stsz,stco";
		*/
		public struct TSample
		{
			public long DataPosition;
			public long DataSize;
		};
		//	class to make it easier to pass around data
		public class TTrack
		{
			public List<TSample> Samples;

			public TTrack()
			{
				this.Samples = new List<TSample>();
			}
		};

		struct SampleMeta
		{
			//	each is 32 bit (4 bytes)
			public int FirstChunk;
			public int SamplesPerChunk;
			public int SampleDescriptionId;

			public SampleMeta(byte[] Data, int Offset)
			{
				FirstChunk = Atom.Get32(Data[Offset + 0], Data[Offset + 1], Data[Offset + 2], Data[Offset + 3]);
				SamplesPerChunk = Atom.Get32(Data[Offset + 4], Data[Offset + 5], Data[Offset + 6], Data[Offset + 7]);
				SampleDescriptionId = Atom.Get32(Data[Offset + 8], Data[Offset + 9], Data[Offset + 10], Data[Offset + 11]);
			}
		};

		static List<SampleMeta> GetSampleMetas(TAtom Atom, byte[] FileData)
		{
			var Metas = new List<SampleMeta>();
			var AtomData = new byte[Atom.DataSize];
			Array.Copy(FileData, Atom.FileOffset, AtomData, 0, AtomData.Length);

			//	https://developer.apple.com/library/content/documentation/QuickTime/QTFF/QTFFChap2/qtff2.html
			var Version = AtomData[8];
			var Flags = PopX.Atom.Get24(AtomData[9], AtomData[10], AtomData[11]);
			var EntryCount = PopX.Atom.Get32(AtomData[12], AtomData[13], AtomData[14], AtomData[15]);

			var MetaSize = 3 * 4;
			for (int i = 16; i < AtomData.Length; i += MetaSize)
			{
				var Meta = new SampleMeta(AtomData, i);
				Metas.Add(Meta);
			}
			if (Metas.Count() != EntryCount)
				Debug.LogWarning("Expected " + EntryCount + " sample metas, got " + Metas.Count());

			return Metas;
		}

		static List<long> GetChunkOffsets(TAtom? Offset32sAtom, TAtom? Offset64sAtom, byte[] FileData)
		{
			int OffsetSize;
			TAtom Atom;
			if (Offset32sAtom.HasValue)
			{
				OffsetSize = 32 / 8;
				Atom = Offset32sAtom.Value;
			}
			else if (Offset64sAtom.HasValue)
			{
				OffsetSize = 64 / 8;
				Atom = Offset64sAtom.Value;
			}
			else
			{
				throw new System.Exception("Missing offset atom");
			}

			var Offsets = new List<long>();
			var AtomData = new byte[Atom.DataSize];
			Array.Copy(FileData, Atom.FileOffset, AtomData, 0, AtomData.Length);

			var Version = AtomData[8];
			var Flags = Get24(AtomData[9], AtomData[10], AtomData[11]);
			var EntryCount = Get32(AtomData[12], AtomData[13], AtomData[14], AtomData[15]);
			if (OffsetSize <= 0)
				throw new System.Exception("Invalid offset size: " + OffsetSize);
			for (int i = 16; i < AtomData.Length; i += OffsetSize)
			{
				if (OffsetSize * 8 == 32)
				{
					var Offset = Get32(AtomData[i + 0], AtomData[i + 1], AtomData[i + 2], AtomData[i + 3]);
					Offsets.Add(Offset);
				}
				else if (OffsetSize * 8 == 64)
				{
					var Offset = Get64(AtomData[i + 0], AtomData[i + 1], AtomData[i + 2], AtomData[i + 3], AtomData[i + 4], AtomData[i + 5], AtomData[i + 6], AtomData[i + 7]);
					Offsets.Add(Offset);
				}
			}
			if (Offsets.Count() != EntryCount)
				Debug.LogWarning("Expected " + EntryCount + " chunks, got " + Offsets.Count());

			return Offsets;
		}

		static List<long> GetSampleSizes(TAtom Atom, byte[] FileData)
		{
			var Sizes = new List<long>();
			var AtomData = new byte[Atom.DataSize];
			Array.Copy(FileData, Atom.FileOffset, AtomData, 0, AtomData.Length);

			//	https://developer.apple.com/library/content/documentation/QuickTime/QTFF/QTFFChap2/qtff2.html
			var Version = AtomData[8];
			var Flags = Get24(AtomData[9], AtomData[10], AtomData[11]);
			var SampleSize = Get32(AtomData[12], AtomData[13], AtomData[14], AtomData[15]);
			var EntryCount = Get32(AtomData[16], AtomData[17], AtomData[18], AtomData[19]);

			//	if size specified, they're all this size
			if (SampleSize != 0)
			{
				for (int i = 0; i < EntryCount; i++)
					Sizes.Add(SampleSize);
				return Sizes;
			}

			//	each entry in the table is the size of a sample (and one chunk can have many samples)
			var SampleSizeStart = 20;
			//	gr: docs don't say size, but this seems accurate...
			SampleSize = (AtomData.Length - SampleSizeStart) / EntryCount;
			for (int i = SampleSizeStart; i < AtomData.Length; i += SampleSize)
			{
				if (SampleSize == 3)
				{
					var Size = Get24(AtomData[i + 0], AtomData[i + 1], AtomData[i + 2]);
					Sizes.Add(Size);
				}
				else if (SampleSize == 4)
				{
					var Size = Get32(AtomData[i + 0], AtomData[i + 1], AtomData[i + 2], AtomData[i + 3]);
					Sizes.Add(Size);
				}
			}
			if (Sizes.Count() != EntryCount)
				Debug.LogWarning("Expected " + EntryCount + " sample sizes, got " + Sizes.Count());

			return Sizes;
		}

		static void DecodeAtomTrack(System.Action<TAtom> EnumAtom, TAtom Trak, byte[] FileData, string TrackName)
		{
			TAtom? TrackChunkOffsets32Atom = null;
			TAtom? TrackChunkOffsets64Atom = null;
			TAtom? TrackSampleSizesAtom = null;
			TAtom? TrackSampleToChunkAtom = null;


			System.Action<TAtom> EnumStblAtom = (Atom) =>
			{
				//	http://mirror.informatimago.com/next/developer.apple.com/documentation/QuickTime/REF/Streaming.35.htm
				if (Atom.Fourcc == "stco")
					TrackChunkOffsets32Atom = Atom;
				if (Atom.Fourcc == "co64")
					TrackChunkOffsets64Atom = Atom;
				if (Atom.Fourcc == "stsz")
					TrackSampleSizesAtom = Atom;
				if (Atom.Fourcc == "stsc")
					TrackSampleToChunkAtom = Atom;
			};
			System.Action<TAtom> EnumMinfAtom = (Atom) =>
			{
				//EnumAtom(Atom);
				if (Atom.Fourcc == "stbl")
					PopX.Atom.DecodeAtomChildren(EnumStblAtom, Atom, FileData);
			};
			System.Action<TAtom> EnumMdiaAtom = (Atom) =>
			{
				//EnumAtom(Atom);
				if (Atom.Fourcc == "minf")
					PopX.Atom.DecodeAtomChildren(EnumMinfAtom, Atom, FileData);
			};
			System.Action<TAtom> EnumTrakChild = (Atom) =>
			{
				//EnumAtom(Atom);
				if (Atom.Fourcc == "mdia")
					PopX.Atom.DecodeAtomChildren(EnumMdiaAtom, Atom, FileData);
			};

			//	go through the track
			PopX.Atom.DecodeAtomChildren(EnumTrakChild, Trak, FileData);

			//	work out samples from atoms!
			if (TrackSampleSizesAtom == null)
				throw new System.Exception("Track missing sample sizes atom");
			if (TrackChunkOffsets32Atom == null && TrackChunkOffsets64Atom == null)
				throw new System.Exception("Track missing chunk offset atom");
			if (TrackSampleToChunkAtom == null)
				throw new System.Exception("Track missing sample-to-chunk table atom");

			var SampleMetas = GetSampleMetas(TrackSampleToChunkAtom.Value, FileData);
			var ChunkOffsets = GetChunkOffsets(TrackChunkOffsets32Atom, TrackChunkOffsets64Atom, FileData);
			var SampleSizes = GetSampleSizes(TrackSampleSizesAtom.Value, FileData);

			for (int i = 0; i < ChunkOffsets.Count(); i++)
			{
				var Chunk = new TAtom();
				Chunk.FileOffset = ChunkOffsets[i];
				Chunk.Fourcc = "Chunk_" + TrackName;
				Chunk.DataSize = 1024;  //	count sizes in samples for this chunk
				EnumAtom(Chunk);
			}

			int SampleIndex = 0;
			for (int i = 0; i < SampleMetas.Count(); i++)
			{
				var SampleMeta = SampleMetas[i];
				var ChunkIndex = SampleMeta.FirstChunk;
				var ChunkOffset = ChunkOffsets[ChunkIndex];

				for (int s = 0; s < SampleMeta.SamplesPerChunk; s++)
				{
					var Sample = new TAtom();
					Sample.FileOffset = ChunkOffset;
					Sample.Fourcc = "Sample_" + TrackName;
					Sample.DataSize = SampleSizes[SampleIndex];
					EnumAtom(Sample);

					ChunkOffset += Sample.DataSize;
					SampleIndex++;
				}
			}
		}

		static void DecodeAtomMoov(System.Action<TAtom> EnumAtom, TAtom Moov, byte[] FileData)
		{
			int TrackCounter = 0;
			System.Action<TAtom> EnumMoovChildAtom = (Atom) =>
			{
				if (Atom.Fourcc == "trak")
				{
					Atom.Fourcc += "#" + TrackCounter;
					EnumAtom(Atom);
					DecodeAtomTrack(EnumAtom, Atom, FileData, "#" + TrackCounter);
					TrackCounter++;
				}
				else
				{
					EnumAtom(Atom);
				}
			};
			PopX.Atom.DecodeAtomChildren(EnumMoovChildAtom, Moov, FileData);
		}

		static void DecodeAtom(System.Action<TAtom> EnumAtom, TAtom Atom, byte[] FileData)
		{
			if (Atom.Fourcc == "moov")
			{
				DecodeAtomMoov(EnumAtom, Atom, FileData);
			}
		}

		static TAtom? GetNextAtom(byte[] Data, long Start)
		{
			//	no more data!
			if (Start >= Data.Length)
				return null;
			
			var AtomData = new byte[TAtom.HeaderSize];
			Array.Copy( Data, Start, AtomData, 0, AtomData.Length);

			//	let it throw(TM)
			var Atom = new TAtom();
			Atom.Set(AtomData);

			//	todo: can we verify the fourcc? lets check the spec if the characters have to be ascii or something
			//	verify size
			var EndPos = Start + Atom.DataSize;
			if (EndPos > Data.Length)
				throw new System.Exception("Atom end position " + (EndPos) + " out of range of data " + Data.Length);

			Atom.FileOffset = (uint)Start;
			return Atom;
		}

		public static void Parse(string Filename, System.Action<TTrack> EnumTrack)
		{
			var FileData = File.ReadAllBytes(Filename);
			Parse(FileData, EnumTrack);
		}

		public static void Parse(byte[] FileData, System.Action<TTrack> EnumTrack)
		{
			var Length = FileData.Length;

			//	decode the header atoms
			TAtom? ftypAtom = null;
			TAtom? moovAtom = null;
			TAtom? mdatAtom = null;

			System.Action<TAtom> EnumRootAtoms = (Atom) =>
			{
				if (Atom.Fourcc == "ftyp") ftypAtom = Atom;
				else if (Atom.Fourcc == "moov") moovAtom = Atom;
				else if (Atom.Fourcc == "mdat") mdatAtom = Atom;
				else
					Debug.Log("Ignored atom: " + Atom.Fourcc);
			};

			//	read the root atoms
			PopX.Atom.Parse(FileData, EnumRootAtoms);

			//	dont even need mdat!
			var Errors = new List<string>();
			if (ftypAtom == null)
				Errors.Add("Missing ftyp atom");
			if (moovAtom == null)
				Errors.Add("Missing moov atom");
			if (Errors.Count > 0)
				throw new System.Exception(String.Join(", ",Errors.ToArray()));

			//	decode moov (tracks, sample data etc)
			List<TTrack> Tracks;
			DecodeAtomMoov(out Tracks, moovAtom.Value, FileData);
			foreach (var t in Tracks)
				EnumTrack(t);
		}


		static void DecodeAtomMoov(out List<TTrack> Tracks,TAtom Moov, byte[] FileData)
		{
			var NewTracks = new List<TTrack>();
			System.Action<TAtom> EnumMoovChildAtom = (Atom) =>
			{
				if (Atom.Fourcc == "trak")
				{
					var Track = new TTrack();
					DecodeAtomTrack( ref Track, Atom, FileData );
					NewTracks.Add(Track);
				}
			};
			Atom.DecodeAtomChildren(EnumMoovChildAtom, Moov, FileData);
			Tracks = NewTracks;
		}

		static void DecodeAtomTrack(ref TTrack Track,TAtom Trak, byte[] FileData)
		{
			TAtom? TrackChunkOffsets32Atom = null;
			TAtom? TrackChunkOffsets64Atom = null;
			TAtom? TrackSampleSizesAtom = null;
			TAtom? TrackSampleToChunkAtom = null;


			System.Action<TAtom> EnumStblAtom = (Atom) =>
			{
				//	http://mirror.informatimago.com/next/developer.apple.com/documentation/QuickTime/REF/Streaming.35.htm
				if (Atom.Fourcc == "stco")
					TrackChunkOffsets32Atom = Atom;
				if (Atom.Fourcc == "co64")
					TrackChunkOffsets64Atom = Atom;
				if (Atom.Fourcc == "stsz")
					TrackSampleSizesAtom = Atom;
				if (Atom.Fourcc == "stsc")
					TrackSampleToChunkAtom = Atom;
			};
			System.Action<TAtom> EnumMinfAtom = (Atom) =>
			{
				//EnumAtom(Atom);
				if (Atom.Fourcc == "stbl")
					PopX.Atom.DecodeAtomChildren(EnumStblAtom, Atom, FileData);
			};
			System.Action<TAtom> EnumMdiaAtom = (Atom) =>
			{
				//EnumAtom(Atom);
				if (Atom.Fourcc == "minf")
					PopX.Atom.DecodeAtomChildren(EnumMinfAtom, Atom, FileData);
			};
			System.Action<TAtom> EnumTrakChild = (Atom) =>
			{
				//EnumAtom(Atom);
				if (Atom.Fourcc == "mdia")
					PopX.Atom.DecodeAtomChildren(EnumMdiaAtom, Atom, FileData);
			};

			//	go through the track
			PopX.Atom.DecodeAtomChildren(EnumTrakChild, Trak, FileData);

			//	work out samples from atoms!
			if (TrackSampleSizesAtom == null)
				throw new System.Exception("Track missing sample sizes atom");
			if (TrackChunkOffsets32Atom == null && TrackChunkOffsets64Atom == null)
				throw new System.Exception("Track missing chunk offset atom");
			if (TrackSampleToChunkAtom == null)
				throw new System.Exception("Track missing sample-to-chunk table atom");

			var SampleMetas = GetSampleMetas(TrackSampleToChunkAtom.Value, FileData);
			var ChunkOffsets = GetChunkOffsets(TrackChunkOffsets32Atom, TrackChunkOffsets64Atom, FileData);
			var SampleSizes = GetSampleSizes(TrackSampleSizesAtom.Value, FileData);
			/*
			for (int i = 0; i < ChunkOffsets.Count(); i++)
			{
				var Chunk = new TAtom();
				Chunk.FileOffset = ChunkOffsets[i];
				Chunk.Fourcc = "Chunk_" + TrackName;
				Chunk.DataSize = 1024;  //	count sizes in samples for this chunk
				EnumAtom(Chunk);
			}
			*/
			int SampleIndex = 0;
			for (int i = 0; i < SampleMetas.Count(); i++)
			{
				var SampleMeta = SampleMetas[i];
				var ChunkIndex = SampleMeta.FirstChunk;
				var ChunkOffset = ChunkOffsets[ChunkIndex];

				for (int s = 0; s < SampleMeta.SamplesPerChunk; s++)
				{
					var Sample = new TSample();
					Sample.DataPosition = ChunkOffset;
					Sample.DataSize = SampleSizes[SampleIndex];
					Track.Samples.Add(Sample);

					ChunkOffset += Sample.DataSize;
					SampleIndex++;
				}
			}
		}

	}
}
