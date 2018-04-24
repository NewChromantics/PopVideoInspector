using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class JustH264Android
{
	const string JavaClassName = "com.newchromantics.justh264.H264Decoder";
	AndroidJavaObject	Decoder;
	public bool			BufferFrames = true;

	//	gr: make this a bytepool! 
	//	https://github.com/SoylentGraham/PopUnityCommon/blob/master/BytePool.cs
	List<byte[]>		FrameBuffers = new List<byte[]>();
	BytePool			BufferPool	{	get	{ return BytePool.Global; }}

	public JustH264Android()
	{
		Debug.Log ("creating decoder class " + JavaClassName);
		Decoder = new AndroidJavaObject (JavaClassName);
		var Result = Decoder.Call<bool> ("Init");
		if (!Result)
			throw new System.Exception ("init decoder failed");
		Debug.Log ("H264Decoder.init() okay");
	}

	public bool PushData(byte[] Data)
	{
		var Result = Decoder.Call<bool> ("DecodeData", Data);
		Debug.Log ("PushData() -> DecodeData result = " + Result);
		return Result;
	}


	public void FetchNextPixelBuffers()
	{
		while (true) {
			var NextPixelBuffer = GetNextPixelBuffer ();
			if (NextPixelBuffer == null)
				return;

			FrameBuffers.Add (NextPixelBuffer);
		}
	}

	byte[] GetNextPixelBuffer ()
	{
		if (BufferPool.IsFull) {
			Debug.Log ("GetNextPixeBuffer -> pool full");
			return null;
		}
		
		var ResultBufferObj = Decoder.Call<AndroidJavaObject> ("GetDecodedFrameArray");
		if (ResultBufferObj == null) {
			Debug.Log ("ResultBufferObj == null");
			return null;
		}

		var ResultBufferRawObj = ResultBufferObj.GetRawObject ();
		if (ResultBufferRawObj== System.IntPtr.Zero ) {
			Debug.Log ("ResultBufferRawObj == null");
			//ResultBufferObj.Dispose ();
			return null;
		}

		try
		{
			Debug.Log ("got result buffer array");
			var ResultBuffer = AndroidJNIHelper.ConvertFromJNIArray<byte[]>(ResultBufferRawObj);

			var PixelBuffer = BufferPool.Alloc (ResultBuffer);

			//Debug.Log ("GetDecodedFrame result = " + Result + " 0,0 = " + PixelBuffer [0] + "," + PixelBuffer [1] + "," + PixelBuffer [2]);

			//	gr: do we need this
			//ResultBufferObj.Dispose ();
			return PixelBuffer;
		}
		catch(System.Exception e) {
			//ResultBufferObj.Dispose ();
			Debug.LogException (e);
			return null;
		}
	}

	// Update is called once per frame
	public bool GetNextFrame(byte[] PixelBuffer)
	{
		byte[] NextFrame = null;

		if (BufferFrames) {
			FetchNextPixelBuffers ();
			if (FrameBuffers.Count > 0) {
				NextFrame = FrameBuffers [0];
				FrameBuffers.RemoveAt (0);
			}
			Debug.Log ("Frames Buffered = " + FrameBuffers.Count);
		} else {
			NextFrame = GetNextPixelBuffer ();
		}

		if (NextFrame == null)
			return false;

		var CopyLength = Mathf.Min (NextFrame.Length, PixelBuffer.Length);
		System.Array.Copy (NextFrame, PixelBuffer, CopyLength);

		BufferPool.Release (NextFrame);
		Debug.Log ("BufferPool size " + BufferPool.TotalAllocatedArrays);

		return true;
	}

	public void Release()
	{
		Decoder.Call("Release");

	}

}
