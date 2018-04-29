using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;
using PopTimeline;
using UnityEditor;
using PopX;


public class VideoTimeline : MonoBehaviour {

	public VideoClip Video;

	[InspectorButton("ParseFile")]
	public bool _ParseFile;

	VideoBridge Data;

	public void ParseFile()
	{
		Data = null;

		var Filename = AssetDatabase.GetAssetPath(Video);
		var Sink = new VideoBridge(Filename);
		var Window = EditorWindow.GetWindow<DataViewWindow>(this.name);
		Window.SetBridge(Sink);
	}

}



public class VideoBridge : PopTimeline.DataBridge
{
	public class VideoStream
	{
		public string StreamName;
		public List<VideoPacket> _Packets;

		public virtual List<VideoPacket> Blocks
		{
			get
			{
				return _Packets;
			}
		}

		public VideoStream()
		{
		}

		public VideoPacket? GetNearestStreamDataLessThanEqual(PopTimeline.TimeUnit Time)
		{
			var Blocks = this.Blocks;
			if (Blocks.Count == 0)
				return null;

			System.Func<int, VideoPacket> GetAt = (Index) =>
			{
				return Blocks[Index];
			};
			System.Func<VideoPacket, BinaryChop.CompareDirection> Compare = (OtherBlock) =>
			{
				return OtherBlock.GetTimeDirection(Time);
			};
			int? Match;
			int? Nearest;
			BinaryChop.Search(0, Blocks.Count - 1, GetAt, Compare, out Nearest, out Match);

			//	nothing at/before this time
			if (Nearest.Value < 0)
				return null;

			return GetAt(Nearest.Value);
		}

		public VideoPacket? GetNearestStreamDataGreaterThanEqual(PopTimeline.TimeUnit Time)
		{
			var Blocks = this.Blocks;
			if (Blocks.Count == 0)
				return null;

			System.Func<int, VideoPacket> GetAt = (Index) =>
			{
				return Blocks[Index];
			};
			System.Func<VideoPacket, BinaryChop.CompareDirection> Compare = (OtherBlock) =>
			{
				return OtherBlock.GetTimeDirection(Time);
			};
			int? Match;
			int? Nearest;
			BinaryChop.Search(0, Blocks.Count - 1, GetAt, Compare, out Nearest, out Match);

			if (Match.HasValue)
				return GetAt(Match.Value);

			//	next must the one after prev
			var Next = Nearest.Value + 1;
			if (Next >= Blocks.Count)
				return null;

			return GetAt(Next);
		}

		void GetStreamDataIndex(PopTimeline.TimeUnit Time, out int? Match, out int? Nearest)
		{
			var Blocks = this.Blocks;
			if (Blocks.Count == 0)
			{
				Match = null;
				Nearest = null;
				return;
			}

			System.Func<int, VideoPacket> GetAt = (Index) =>
			{
				return Blocks[Index];
			};
			System.Func<VideoPacket, BinaryChop.CompareDirection> Compare = (OtherBlock) =>
			{
				return OtherBlock.GetTimeDirection(Time);
			};
			BinaryChop.Search(0, Blocks.Count - 1, GetAt, Compare, out Nearest, out Match);
		}

		public VideoPacket? GetStreamData(PopTimeline.TimeUnit Time)
		{
			int? Index;
			int? NearestIndex;
			GetStreamDataIndex(Time, out Index, out NearestIndex);
			if (!Index.HasValue)
				return null;
			return Blocks[Index.Value];
		}

		public void AddBlock(VideoPacket NewBlock)
		{
			//	need to insert at the correct time
			var Blocks = this.Blocks;

			System.Func<int, VideoPacket> GetAt = (Index) =>
			{
				return Blocks[Index];
			};
			System.Func<VideoPacket, BinaryChop.CompareDirection> Compare = (OtherBlock) =>
			{
				//return OtherBlock.GetLineIndexDirection(NewBlock.StartLine);
				return OtherBlock.GetTimeDirection(NewBlock.GetStartTime());
			};
			int? Match;
			int? NearestPrev;
			if (Blocks.Count == 0)
			{
				Match = null;
				NearestPrev = -1;
			}
			else
			{
				BinaryChop.Search(0, Blocks.Count - 1, GetAt, Compare, out NearestPrev, out Match);
				if (Match.HasValue)
					throw new System.Exception("Block already exists in stream");
			}
			Blocks.Insert(NearestPrev.Value + 1, NewBlock);
		}
	};



	class VideoStreamMeta : PopTimeline.DataStreamMeta
	{
		public int StreamIndex;

		public VideoStreamMeta(string Name, int StreamIndex) : base(Name)
		{
			this.StreamIndex = StreamIndex;
		}
	};

	//	parsed blocks, sorted by time
	List<VideoStream> VideoStreams;

	public override List<PopTimeline.DataStreamMeta> Streams { get { return GetStreamMetas(); } }

	public class EmptyLineException : System.Exception
	{
	};

	public struct VideoPacket : PopTimeline.StreamDataItem
	{
		public Mpeg4.TSample Sample;
		//public int StartTimeMs;
		//public int DurationMs;
		public int StartTimeMs	{ get { return (int)(Sample.DataPosition); }}
		public int DurationMs { get { return (int)(Sample.DataSize-1); } }

		//	is this time before,inside,or after this block
		public BinaryChop.CompareDirection GetTimeDirection(PopTimeline.TimeUnit Time)
		{
			if (Time.Time < StartTimeMs)
				return BinaryChop.CompareDirection.Before;
			if (Time.Time > StartTimeMs + DurationMs)
				return BinaryChop.CompareDirection.After;
			return BinaryChop.CompareDirection.Inside;
		}

		public PopTimeline.TimeUnit GetStartTime()
		{
			return new PopTimeline.TimeUnit(StartTimeMs);
		}

		public PopTimeline.TimeUnit GetEndTime()
		{
			return new PopTimeline.TimeUnit(StartTimeMs + DurationMs);
		}

		public PopTimeline.DataState GetStatus()
		{
			return PopTimeline.DataState.Loaded;
		}

	}


	//	maybe change this to direct IO?
	public VideoBridge(string Filename)
	{
		VideoStreams = new List<VideoStream>();

		int TrackCount = 0;
		System.Action<PopX.Mpeg4.TTrack> EnumTrack = (Track) =>
		{
			var SampleStreamName = "Track " + TrackCount + " samples";
			var ChunkStreamName = "Track " + TrackCount + " chunks";
			TrackCount++;

			foreach ( var Sample in Track.Samples)
			{
				try
				{
					PushPacket(Sample, SampleStreamName);
				}
				catch(System.Exception e)
				{
					Debug.LogException(e);
				}
			}

			/*
			foreach (var Sample in Track.Chunks)
			{
				try
				{
					PushPacket(Sample, ChunkStreamName);
				}
				catch (System.Exception e)
				{
					Debug.LogException(e);
				}
			}
			*/
		};

		PopX.Mpeg4.Parse(Filename, EnumTrack);
	}

	/*
	int? GetPrevEqualBlockStartLineIndex(int LineIndex)
	{
		var AllLines = DataLines;
		for (int li = LineIndex; li >= 0; li--)
		{
			//	does this line start with a time code? if not, go back
			var Line = AllLines[li];
			if (Line.StartsWith(LineBlock.PacketTypePrefix))
				return li;
		}
		return null;
	}
	int? GetNextEqualBlockStartLineIndex(int LineIndex)
	{
		var AllLines = DataLines;
		for (int li = LineIndex; li < AllLines.Length; li++)
		{
			//	does this line start with a time code? if not, go back
			var Line = AllLines[li];
			if (Line.StartsWith(LineBlock.PacketTypePrefix))
				return li;
		}
		return null;
	}


	LineBlock? GetPrevEqualBlock(int LineIndex)
	{
		var AllLines = DataLines;

		int Tries = 1000;
		var Error = new System.Exception("Failed to GetPrevEqualBlock(" + LineIndex + ") after " + Tries + " tries");
		//	loop so we skip over bad data
		while (Tries-- > 0)
		{
			int? FirstLine = GetPrevEqualBlockStartLineIndex(LineIndex);
			int? NextBlockFirstLine = GetNextEqualBlockStartLineIndex(LineIndex + 1);
			if (!FirstLine.HasValue)
				return null;
			//	if no next block, we go to the end of the file
			if (!NextBlockFirstLine.HasValue)
				NextBlockFirstLine = AllLines.Length;
			var LastLine = NextBlockFirstLine.Value - 1;

			try
			{
				var Block = ParseBlock(FirstLine.Value, LastLine);
				return Block;
			}
			catch (System.Exception e)
			{
				Debug.Log("Error parsing block " + FirstLine.Value + "..." + LastLine + "; " + e.Message);
				LineIndex = FirstLine.Value - 1;
			}
		}
		throw Error;
	}

	LineBlock? GetNextEqualBlock(int LineIndex)
	{
		var AllLines = DataLines;
		int Tries = 1000;
		var Error = new System.Exception("Failed to GetNextEqualBlock(" + LineIndex + ") after " + Tries + " tries");
		//	loop so we skip over bad data
		while (Tries-- > 0)
		{
			int? FirstLine = GetNextEqualBlockStartLineIndex(LineIndex);
			//	if no first line we got to the end of the file
			if (!FirstLine.HasValue)
				return null;
			int? NextBlockFirstLine = GetNextEqualBlockStartLineIndex(FirstLine.Value + 1);
			//	if no next block, we go to the end of the file
			if (!NextBlockFirstLine.HasValue)
				NextBlockFirstLine = AllLines.Length;
			var LastLine = NextBlockFirstLine.Value - 1;

			try
			{
				var Block = ParseBlock(FirstLine.Value, LastLine);
				return Block;
			}
			catch (System.Exception e)
			{
				Debug.Log("Error parsing block " + FirstLine.Value + "..." + LastLine + "; " + e.Message);
				LineIndex = NextBlockFirstLine.Value + 0;
			}
		}
		throw Error;
	}

	LineBlock ParseBlock(int FirstLineIndex, int LastLineIndex)
	{
		var AllLines = DataLines;

		//	todo: find all json blocks properly
		var JsonBlocks = new List<string>();
		for (int i = FirstLineIndex; i <= LastLineIndex; i++)
		{
			var Line = AllLines[i];
			if (IsAllWhitespace(Line))
				continue;
			JsonBlocks.Add(Line);
		}

		if (JsonBlocks.Count > 2)
		{
			throw new System.Exception("Unexpected json block count " + JsonBlocks.Count + " line " + FirstLineIndex + " ... " + LastLineIndex + "\n\n" + string.Join("\n", JsonBlocks.ToArray()));
		}

		//	first line should be timecode
		var Timecode = TimecodeMarker.FromJson(JsonBlocks[0]);

		var NewBlock = new LineBlock();
		NewBlock.EndLine = LastLineIndex;
		NewBlock.StartLine = FirstLineIndex;
		NewBlock.Timecode = Timecode;
		NewBlock.Json = JsonBlocks[1];  //	+ the others

		//	cache
		PushBlock(NewBlock);

		return NewBlock;
	}

*/

	public PopTimeline.DataStreamMeta GetStreamMeta(string Name)
	{
		var Metas = GetStreamMetas();
		foreach (var Meta in Metas)
			if (Meta.Name == Name)
				return Meta;
		return null;
	}


	VideoStream GetStream(string StreamName)
	{
		//	return existing...
		Pop.AllocIfNull(ref VideoStreams);
		foreach (var bs in VideoStreams)
			if (bs.StreamName == StreamName)
				return bs;

		//	new!
		var NewBlockStream = new VideoStream();
		NewBlockStream._Packets = new List<VideoPacket>();
		NewBlockStream.StreamName = StreamName;
		VideoStreams.Add(NewBlockStream);
		return VideoStreams[VideoStreams.Count - 1];
	}

	public void PushPacket(Mpeg4.TSample Sample,string StreamName)
	{
		var Stream = GetStream(StreamName);
		var Packet = new VideoPacket();
		Packet.Sample = Sample;
		Stream.AddBlock(Packet);
	}


	public override void GetTimeRange(out PopTimeline.TimeUnit Min, out PopTimeline.TimeUnit Max)
	{
		int? MinTime = null;
		int? MaxTime = null;
		Pop.AllocIfNull(ref VideoStreams);


		foreach (var bs in VideoStreams)
		{
			var Blocks = bs.Blocks;
			if (Blocks == null || Blocks.Count == 0)
				continue;
			var bsmin = Blocks[0].GetStartTime().Time;
			var bsmax = Blocks[Blocks.Count - 1].GetEndTime().Time;

			MinTime = MinTime.HasValue ? Mathf.Min(MinTime.Value, bsmin) : bsmin;
			MaxTime = MaxTime.HasValue ? Mathf.Max(MaxTime.Value, bsmax) : bsmax;
		}

		if (!MinTime.HasValue)
			throw new System.Exception("No time range (no data)");

		Min = new PopTimeline.TimeUnit(MinTime.Value);
		Max = new PopTimeline.TimeUnit(MaxTime.Value);
	}
	/*
	public override int GetDataCount(PopTimeline.DataStreamMeta _StreamMeta)
	{
		var StreamMeta = (BlockStreamMeta)_StreamMeta;
		var Stream = BlockStreams[StreamMeta.StreamIndex];
		return Stream.Blocks.Count;
	}
*/
	List<PopTimeline.DataStreamMeta> GetStreamMetas()
	{
		var StreamColours = new Color[]
		{
			new Color32(55, 175, 198, 255),	//	turqoise
			new Color32(167, 198, 55, 255),	//	lime
			new Color32(198, 55, 136, 255),	//	magenta
			new Color32(229, 126, 22, 255),	//	orange
			new Color32(221, 32, 22, 255),	//	red
		};

		var Metas = new List<PopTimeline.DataStreamMeta>();
		for (int s = 0; s < VideoStreams.Count; s++)
		{
			var BlockStream = VideoStreams[s];
			var Meta = new VideoStreamMeta(BlockStream.StreamName, s);
			Meta.Colour = StreamColours[s % StreamColours.Length];
			Metas.Add(Meta);
		}
		return Metas;
	}

	public override List<PopTimeline.StreamDataItem> GetStreamData(PopTimeline.DataStreamMeta _StreamMeta, PopTimeline.TimeUnit MinTime, PopTimeline.TimeUnit MaxTime)
	{
		var StreamMeta = (VideoStreamMeta)_StreamMeta;
		var BlockStream = VideoStreams[StreamMeta.StreamIndex];
		var Data = new List<PopTimeline.StreamDataItem>();
		var Blocks = BlockStream.Blocks;

		if (Blocks.Count == 0)
			return Data;

		//	find start with binary chop. 
		//	find TAIL and work backwards as nearestprev will be last time, but if we do min we can start out of range
		System.Func<int, VideoPacket> GetAt = (Index) =>
		{
			return Blocks[Index];
		};
		System.Func<VideoPacket, BinaryChop.CompareDirection> Compare = (OtherBlock) =>
		{
			return OtherBlock.GetTimeDirection(MaxTime);
		};
		int? MaxMatch;
		int? MaxNearestPrev;
		BinaryChop.Search(0, Blocks.Count - 1, GetAt, Compare, out MaxNearestPrev, out MaxMatch);
		int Last = MaxNearestPrev.Value;

		//	go earlier
		for (int b = Last; b >= 0; b--)
		{
			var Block = Blocks[b];
			var CompareDir = Block.GetTimeDirection(MinTime);
			//	if min time is AFTER block, block is before min
			if (CompareDir == BinaryChop.CompareDirection.After)
				break;
			Data.Insert(0, Block);
		}

		return Data;
	}

	public override PopTimeline.StreamDataItem GetNearestOrPrevStreamData(PopTimeline.DataStreamMeta _StreamMeta, ref PopTimeline.TimeUnit Time)
	{
		var StreamMeta = (VideoStreamMeta)_StreamMeta;
		var BlockStream = VideoStreams[StreamMeta.StreamIndex];
		var PrevData = BlockStream.GetNearestStreamDataLessThanEqual(Time);
		var Prev = PrevData.Value;
		Time = Prev.GetStartTime();
		return Prev;
	}

	public override PopTimeline.StreamDataItem GetNearestOrNextStreamData(PopTimeline.DataStreamMeta _StreamMeta, ref PopTimeline.TimeUnit Time)
	{
		var StreamMeta = (VideoStreamMeta)_StreamMeta;
		var BlockStream = VideoStreams[StreamMeta.StreamIndex];
		var NextData = BlockStream.GetNearestStreamDataGreaterThanEqual(Time);
		var Next = NextData.Value;
		Time = Next.GetStartTime();
		return Next;
	}


}

