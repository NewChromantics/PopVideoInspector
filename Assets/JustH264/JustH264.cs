#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX || UNITY_IOS
#define USING_PLUGIN
#endif
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.IO;
using System.Runtime.InteropServices;



[System.Serializable]
public class TFrameLimiter
{
	[Range(1,60)]
	public float		ExpectedFrameRate = 15;
	public float		MinFrameDelaySecs	{	get	{ return 1.0f / ExpectedFrameRate; }}
	float				LastFrameTime = 0;

	public bool			ReadyForNewFrame()
	{
		//	not long enough since last frame
		var SecsSinceLastFrame = Time.time - LastFrameTime;
		if (SecsSinceLastFrame < MinFrameDelaySecs)
			return false;

		return true;
	}

	//	returns frame rate
	public int			OnNewFrame()
	{
		//	note: do we'll lose the time taken between ReadyForNewFrame and now? or is Time.time unity-frame-time and doesnt change?
		var SecsSinceLastFrame = Time.time - LastFrameTime;

		var FrameRatef = 1.0f / SecsSinceLastFrame;
		var FrameRate = (int)FrameRatef;

		Debug.Log ("New Frame - framerate since last frame = " + FrameRate + " (SecsSinceLastFrame="+ SecsSinceLastFrame+")");
		LastFrameTime = Time.time;

		return FrameRate;
	}
};

[System.Serializable]
public class UnityEvent_NewFrameTexture : UnityEngine.Events.UnityEvent <Texture> {}


public class JustH264 : MonoBehaviour
{
	public UnityEvent_NewFrameTexture	OnNewFrame;
	public UnityEvent_int				OnNewFrameRate;
	public UnityEvent_String			OnNewFrameRateString;

	Texture2D VideoTexture = null;
	//	"pre-alloc" the data, but really we're using this to make sure we have the right amount of data the texture is expecting
	byte[]	VideoTextureBuffer = null;
	//	gr: if the decoder buffer is bigger than this, allocate it once (only applies to IOS/osx atm)
	byte[]	BiggerVideoTextureBuffer = null;

#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
private const string PluginName = "JustH264";
#elif UNITY_IOS
private const string PluginName = "__Internal";
#endif

#if USING_PLUGIN
    [DllImport(PluginName)]
    private static extern void VideoServiceDecodeFrame(byte[] H264Data, int size);

    [DllImport(PluginName)]
    private static extern int VideoServiceHasFrame(); //returns the size of frame

    [DllImport(PluginName)]
	private static extern int VideoServiceCopyFrame(byte[] RgbaData/*,int Size*/); //returns the size of of frame and copies png data
#endif

	JustH264Android	JavaDecoder;

	//	delay/smooth frame output by deffering calling (cross platform)
	public TFrameLimiter	FrameLimiter;

	public bool YuvMode_Osx = false;
	const string YUV_SHADER_KEYWORD = "YUV_SOURCE";

	public bool				DrawFrameCounters = false;
	int InputCount = 0;
	int OutputCount = 0;

	void init()
    {
		#if UNITY_ANDROID && !UNITY_EDITOR
		bool Yuv = true;
		#elif UNITY_EDITOR
		bool Yuv = YuvMode_Osx;
		#else
		bool Yuv = false;
		#endif

		if ( Yuv )
		{
			Shader.EnableKeyword(YUV_SHADER_KEYWORD);
			VideoTexture = new Texture2D(640, 720, TextureFormat.Alpha8, false);
			VideoTexture.filterMode = FilterMode.Point;
		}
		else
		{
			Shader.DisableKeyword(YUV_SHADER_KEYWORD);
			VideoTexture = new Texture2D(640, 480, TextureFormat.RGBA32, false);
		}

		VideoTextureBuffer = VideoTexture.GetRawTextureData ();

		#if UNITY_ANDROID && !UNITY_EDITOR
		Debug.Log("Creating android plugin");
		JavaDecoder = new JustH264Android();
		#endif
    }

	bool GetNextFrame()
	{
		if ( VideoTextureBuffer == null )
			init();

		#if USING_PLUGIN
		//	any waiting data?
		int size = VideoServiceHasFrame();
		if (size <= 0)
			return false;
		
		//	data mis-match, so lets just alloc what we need
		byte[] FrameBuffer = VideoTextureBuffer;

		if ( size > FrameBuffer.Length )
		{
			if ( BiggerVideoTextureBuffer == null || BiggerVideoTextureBuffer.Length < size )
				BiggerVideoTextureBuffer = new byte[size];
			FrameBuffer = BiggerVideoTextureBuffer;
		}

		//	pop frame data
		VideoServiceCopyFrame( FrameBuffer );

		if ( FrameBuffer != VideoTextureBuffer )
		{
			var CopySize = Math.Min (size, VideoTextureBuffer.Length);
			Array.Copy (FrameBuffer, VideoTextureBuffer, CopySize);
		}

		#else
		if ( JavaDecoder == null )
			return false;

		PushPendingBuffers();

		if ( !JavaDecoder.GetNextFrame( VideoTextureBuffer) )
			return false;
		#endif

		OutputCount++;
		DrawCounters (VideoTextureBuffer, VideoTexture);

		VideoTexture.LoadRawTextureData (VideoTextureBuffer);
		VideoTexture.Apply ();

		return true;
	}

    void Update()
    {
		if (!FrameLimiter.ReadyForNewFrame ())
			return;

		if (!GetNextFrame ())
			return;

		var FrameRate = FrameLimiter.OnNewFrame ();
		OnNewFrame.Invoke (VideoTexture);
		OnNewFrameRate.Invoke (FrameRate);
		OnNewFrameRateString.Invoke (""+FrameRate);
	 }

	List<byte[]> PendingBuffers;
	void PushPendingBuffers()
	{
		if (JavaDecoder == null)
			return;
	
		Debug.Log("PushData()");

        if (PendingBuffers == null || PendingBuffers.Count == 0)
            return;

		var NextBuffer = PendingBuffers [0];
        if (JavaDecoder.PushData(NextBuffer))
        {
			InputCount++;
            PendingBuffers.RemoveAt(0);
            PushPendingBuffers();
        }
	}

	public void writeH264(byte[] b)
	{
		#if USING_PLUGIN
		VideoServiceDecodeFrame(b, b.Length);
		InputCount++;
		#else
		if ( PendingBuffers == null )
			PendingBuffers = new List<byte[]>();
		PendingBuffers.Add(b);
		PushPendingBuffers();
		#endif
	}

	void DrawCounters(byte[] VideoTextureBuffer,Texture2D VideoTexture)
	{
		System.Action<int,int,int,int> DrawSquarePixels = (x, y, size,Components) => {
			int w = size;
			int h = size;
			int Stride = VideoTexture.width;
		
			int ByteCount = w * Components;

			for ( int yy=0;	yy<h;	yy++ )
			{
				int i = x + ((y+yy) * Stride);

				i *= Components;
				i = Math.Min( i, VideoTextureBuffer.Length-ByteCount);
				for ( int b=0;	b<ByteCount;	b++ )
					VideoTextureBuffer[i+b] = 0xff;
			}

		};

		System.Action<int,int,int> DrawSquare = (x, y, size) => {
			if ( VideoTexture.format == TextureFormat.RGBA32 )
				DrawSquarePixels.Invoke(x,y,size, 4);
			else if ( VideoTexture.format == TextureFormat.Alpha8 )
				DrawSquarePixels.Invoke(x,y,size,1);
		};

		System.Action<int> DrawCounter = (Counter) => {
			int Size = 10;
			int Cols = VideoTexture.width / Size;
			int Row = Counter / Cols;
			int Col = Counter % Cols;

			DrawSquare.Invoke (Col*Size,Row*Size, Size);
		};

		DrawCounter.Invoke( InputCount );
		DrawCounter.Invoke( OutputCount );
	}

}
