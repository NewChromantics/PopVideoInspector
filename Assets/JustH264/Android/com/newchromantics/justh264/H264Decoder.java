package com.newchromantics.justh264;

import java.io.FileInputStream;
import java.io.FileNotFoundException;
import java.io.IOException;
import java.nio.ByteBuffer;
import java.util.Arrays;

import android.content.Context;
import android.media.MediaCodec;
import android.media.MediaCodec.BufferInfo;
import android.media.MediaExtractor;
import android.media.MediaFormat;
import android.net.Uri;
import android.os.Build;
import android.util.Log;
import android.view.Surface;

public class H264Decoder
{
	private static final String VIDEO = "video/";
	private static final String TAG = "JustH264";
	private static final String MIME = "video/avc";
	
	private MediaCodec mDecoder;
	
	long startMs;

	public boolean Init()
	{
		try {
			String mimeType = MIME;
			MediaFormat format = MediaFormat.createVideoFormat(mimeType, 640, 480);
			//0000000 00 00 00 01 67 42 40 1f 96 54 05 01 ec 80 00 00
			//0000010 00 01 68 ce 38 80
			byte[] header_sps = { 0, 0, 0, 1, 0x67, 0x42, 0x40, 0x1f, (byte) 0x96, 0x54, 5, 1, (byte) 0xec, (byte)0x80};
			byte[] header_pps = { 0, 0, 0, 1, 0x68, (byte)0xce, 0x38, (byte) 0x80 };
			format.setByteBuffer("csd-0", ByteBuffer.wrap(header_sps));
			format.setByteBuffer("csd-1", ByteBuffer.wrap(header_pps));

			//	gr: no surface = pixel buffer backing
			Surface surface = null;
			
			startMs = 0;
			
			mDecoder = MediaCodec.createDecoderByType(mimeType);
			mDecoder.configure(format, surface, null, 0);
			mDecoder.start();

			return true;
			
		} catch (Exception e) {
			e.printStackTrace();
			return false;
		}
	}
	
	public boolean DecodeData(byte[] data)
	{
		int Timeout = 1;
		int inIndex = mDecoder.dequeueInputBuffer(Timeout);
		if (inIndex < 0 )
			return false;
		
		ByteBuffer buffer;
		/*
		if (Build.VERSION.SDK_INT < Build.VERSION_CODES.LOLLIPOP) {
			buffer = mDecoder.getInputBuffers()[inIndex];
			buffer.clear();
		}
		else*/
		{
			buffer = mDecoder.getInputBuffer(inIndex);
		}
		
		if ( buffer == null )
		{
			//	gr: my code restores buffer back to queue if data copy fails
			//mDecoder.queueInputBuffer(inIndex, 0, data.length, presentationTimeUs, 0);
			return false;
		}
		
		buffer.put(data);
		//long presentationTimeUs = System.currentTimeMillis() - startMs;
		startMs++;
		mDecoder.queueInputBuffer(inIndex, 0, data.length, startMs, 0);
		return true;
	}

	int search0001(byte[] sBuffer, int offset)
	{
		boolean bFound = false;
		for (int i = offset; i < sBuffer.length; i += 4){
			if(sBuffer.length - i >= 4) {
				if (sBuffer[i] == 0 && sBuffer[i + 1] == 0 && sBuffer[i + 2] == 0 && sBuffer[i + 3] == 1)
					return i;
			}
		}
		return 0;
	}

	public void Release() {
		mDecoder.stop();
		mDecoder.release();
	}
	
	public byte[] GetDecodedFrameArray()
	{
		//	output is always YUV so
		//	Y = 640 * 480 * 1
		//	UV = 320 * 240 * 2
		int Width = 640;
		int Height = 480;
		int HalfWidth = Width / 2;
		int HalfHeight = Height / 2;
		byte[] buffer = new byte[(Width*Height*1) + (HalfWidth*HalfHeight*2)];
		
		if ( !GetDecodedFrame(buffer) )
			return null;
		
		return buffer;
	}
	
	
	public boolean GetDecodedFrame(byte[] PixelBuffer)
	{
		//	see if there's an output buffer waiting
		int Timeout = 1;
		BufferInfo Info = new BufferInfo();
		int outIndex = mDecoder.dequeueOutputBuffer(Info, Timeout);
			
		switch (outIndex) {
			case MediaCodec.INFO_OUTPUT_BUFFERS_CHANGED:
				Log.d(TAG, "INFO_OUTPUT_BUFFERS_CHANGED");
				//	mDecoder.getOutputBuffers();
				return false;
				
			case MediaCodec.INFO_OUTPUT_FORMAT_CHANGED:
				Log.d(TAG, "INFO_OUTPUT_FORMAT_CHANGED format : " + mDecoder.getOutputFormat());
				return false;
				
			case MediaCodec.INFO_TRY_AGAIN_LATER:
				Log.d(TAG, "INFO_TRY_AGAIN_LATER");
				return false;
				
			default:
				break;
		}
		
		int Flags = Info.flags;
		int Offset = Info.offset;
		int Size = Info.size;
		//auto MediaFormat = JniMediaFormat( mCodec->CallObjectMethod("getOutputFormat","android.media.MediaFormat",outputBufferId) );
				
		ByteBuffer DecodedPixelBuffer = mDecoder.getOutputBuffer(outIndex);
		int DecodedPixelBuffer_length = DecodedPixelBuffer.remaining();
		
		int CopySize = java.lang.Math.min( Size, PixelBuffer.length );
		Log.d(TAG, "DecodedPixelBuffer offset=" + Info.offset + " size=" + Info.size + " bufferlength=" + DecodedPixelBuffer_length + " PixelBuffer.length="+ PixelBuffer.length + " copysize=" + CopySize + " presentationtime=" + Info.presentationTimeUs );
		
		DecodedPixelBuffer.get(PixelBuffer, Offset, CopySize);

		//	gr: my code always has false for pixel buffers
		//	put this in a catch as if we throw, it'll block the decoder
		boolean CopySurface = false;
		mDecoder.releaseOutputBuffer(outIndex, CopySurface);
		return true;
		
		/*
		 // All decoded frames have been rendered, we can stop playing now
		 if ((info.flags & MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0) {
		 Log.d(TAG, "OutputBuffer BUFFER_FLAG_END_OF_STREAM");
		 break;
		 }
		 
		 mDecoder.stop();
		 mDecoder.release();
		 */
	}
}
