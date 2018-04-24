//
//  DecodeService.m
//  firstorder
//
//  Created by Jonathan O'Neill Browne on 8/26/17.
//  Copyright © 2017 Side Dish Productions. All rights reserved.
//


#import "DecodeService.h"
#include <memory>
#include <vector>


class TBuffer
{
public:
	TBuffer(uint8_t* Data,size_t Size) :
	mData	( Data ),
	mSize	( Size )
	{
	}
	virtual ~TBuffer()
	{
	}
	
	void		Copy(const TBuffer& Buffer);
	
public:
	uint8_t*	mData;
	size_t		mSize;
};

class TAllocatedBuffer : public TBuffer
{
public:
	TAllocatedBuffer(size_t Size) :
		TBuffer	( nullptr, 0 )
	{
		mData = static_cast<uint8_t*>( malloc( Size ) );
	}
	~TAllocatedBuffer()
	{
		free( mData );
		mData = nullptr;
	}
};

//	gr: move these to c# so platforms have same meta decoding
namespace H264
{
	namespace Nalu
	{
		enum TYPE
		{
			SPS = 7,
			PPS,
			Other,
		};
	}
	
	Nalu::TYPE	GetNaluType(TBuffer& Buffer);
}

H264::Nalu::TYPE H264::GetNaluType(TBuffer& Buffer)
{
	const uint8_t NaluMatch[4] = { 0,0,0,1 };
	
	for ( int i=0;	i<Buffer.mSize-5;	i++ )
	{
		auto Compare = memcmp( &Buffer.mData[i], NaluMatch, sizeof(NaluMatch) );
		if ( Compare != 0 )
			continue;
		
		auto TypeOffset = i + sizeof(NaluMatch);
		auto Type = Buffer.mData[TypeOffset] & 0x1F;
		auto NaluType = static_cast<H264::Nalu::TYPE>( Type );
		return NaluType;
	}
	
	return H264::Nalu::Other;
}

class TFrame
{
public:
	TFrame(size_t AllocSize) :
		mPixels	( AllocSize )
	{
	}
	
	size_t				GetBufferSize() const		{	return mPixels.mSize;	}
	TAllocatedBuffer	mPixels;
};


class TDecodeSession
{
public:
	TDecodeSession();
	
	size_t		PopFrame(TBuffer& Output);
	void		PushFrame(TBuffer& Pixels);
	size_t		GetNextFrameSize();
	
	std::vector<std::shared_ptr<TFrame>>	mDecodedFrames;
};

TDecodeSession gDecodeSession;


TDecodeSession::TDecodeSession()
{
}

size_t TDecodeSession::PopFrame(TBuffer& Output)
{
	if ( mDecodedFrames.size() == 0 )
		return 0;
	
	auto NextFrame = mDecodedFrames[0];
	mDecodedFrames.erase( mDecodedFrames.begin() );
	auto CopySize = NextFrame->mPixels.mSize;
	Output.Copy( NextFrame->mPixels );
	NextFrame.reset();
	return CopySize;
}

void TDecodeSession::PushFrame(TBuffer& Pixels)
{
	//	make a new frame
	std::shared_ptr<TFrame> NewFrame;
	NewFrame.reset( new TFrame(Pixels.mSize) );
	NewFrame->mPixels.Copy( Pixels );
	
	mDecodedFrames.push_back( NewFrame );
}

size_t TDecodeSession::GetNextFrameSize()
{
	if ( mDecodedFrames.size() == 0 )
		return 0;
	
	auto Size = mDecodedFrames[0]->GetBufferSize();
	return Size;
}


/*
bool TDecodeSession::Decode(TBuffer& Packet)
{
	auto Nalu = H264::GetNaluType(Packet);
	NSLog(@"Nalu = %d size =%d",Nalu, Packet.mSize);
	
	if ( Nalu == H264::Nalu::SPS )
	{
		CreateSps( Packet );
		return false;
		
	}
	else if(nt == 8)
	{
		if(!self.has_session)
		{
			[self create_pps:data Size:size];
			[self create_session];
		}
		return false;
		
	}
	else if(nt == 5 || nt == 1)   // type 5 is an IDR frame NALU.  The SPS and PPS NALUs should always be followed by an IDR (or IFrame) NALU  NALU type 1 is non-IDR (or PFrame) picture
	{
		NSLog(@"Nalu = %@",(nt==5)?@"IDR (IFrame)":@"non-IDR (PFrame)");
		[self create_frame:data Size:size];
		return true;
		
	}
	
	return true;
	
}
*/


@implementation DecodeService

-(id) init
{
    self = [super init];
    self.sps = 0;
    self.sps_size =0;
	self.pps=0;
	self.pps_size=0;
    self.has_session=false; //if we have a session no more need for PPS or sps frames
    self.busy=false;
    
    self.formatDesc=NULL;
    self.decompressionSession=NULL;
    self.sampleBuffer1=NULL;
    self.sampleBuffer2 = NULL;
    self.sampleBuffer3 = NULL;
    self.sampleBuffer4 = NULL;
    self.sampleBuffer5 = NULL;
    self.sampleBuffer6 = NULL;
    self.sample_buffer_current = 0;
    
    return self;
}

- (CMSampleBufferRef) next_buffer
{
    self.sample_buffer_current ++;
    if(self.sample_buffer_current == 6)
    {
        self.sample_buffer_current=0;
    }
    
    if(self.sample_buffer_current ==1) return self.sampleBuffer2;
    else if(self.sample_buffer_current ==2) return self.sampleBuffer3;
    else if(self.sample_buffer_current ==3) return self.sampleBuffer4;
    else if(self.sample_buffer_current ==4) return self.sampleBuffer5;
    else if(self.sample_buffer_current ==5) return self.sampleBuffer6;
    
    return self.sampleBuffer1;
}

/*-(void) screenshot:(UIImage *)image
{
    NSLog(@"GOT SCREENSHOT");
    
 
   
}*/

/*-(void) screenshotdata:(NSData *) data
{
    [self.delegate performSelectorOnMainThread:@selector(decodedData:) withObject:data waitUntilDone:YES];
}*/

- (void) create_sps:(const unsigned char *)data Size:(unsigned int)size
{
    NSLog(@"creating SPS (7)");
    if(self.sps)
    {
        free( self.sps);
        self.sps=0;
        self.sps_size=0;
    }
    
    //include the nalu type
    self.sps_size=size-4;
	self.sps = static_cast<uint8_t*>( malloc(self.sps_size) );
    memcpy(self.sps,&data[4],self.sps_size);
}

- (void) create_pps:(const unsigned char *)data Size:(unsigned int)size
{
    NSLog(@"creating PPS (8)");
    if(self.pps)
    {
        free( self.pps);
        self.pps=0;
        self.pps_size=0;
    }
    
    self.pps_size=size-4;
    self.pps = static_cast<uint8_t*>( malloc(self.pps_size) );
    memcpy(self.pps,&data[4],self.pps_size); //include the nali
    //we got a 7 and a 8  - todo to make sure of this
    uint8_t*  parameterSetPointers[2] = {self.sps, self.pps};
    size_t parameterSetSizes[2] = {self.sps_size, self.pps_size};
    
    OSStatus status = CMVideoFormatDescriptionCreateFromH264ParameterSets(kCFAllocatorDefault,
                                                                          2,
                                                                          (const uint8_t *const*)parameterSetPointers,
                                                                          parameterSetSizes,
                                                                          4,
                                                                          &_formatDesc);
    NSLog(@"\t\t Creation of CMVideoFormatDescription: %@", (status == noErr) ? @"successful!" : @"failed...");
    if(status != noErr) NSLog(@"\t\t Format Description ERROR type: %d", (int)status);
    
}

- (void) create_session
{
	//	null=default/internal allocator
	CFAllocatorRef Allocator = nil;
	CFIndex DictionaryCapacity = 0;
	
	//	setup output params
	CFMutableDictionaryRef destinationPixelBufferAttributes = CFDictionaryCreateMutable( Allocator, DictionaryCapacity, &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks);
	
	//	force size output
	/*
	SInt32 Width = size_cast<SInt32>( Stream.mPixelMeta.GetWidth() );
	SInt32 Height = size_cast<SInt32>( Stream.mPixelMeta.GetHeight() );
	CFDictionarySetValue(destinationPixelBufferAttributes,kCVPixelBufferWidthKey, CFNumberCreate(NULL, kCFNumberSInt32Type, &Width));
	CFDictionarySetValue(destinationPixelBufferAttributes, kCVPixelBufferHeightKey, CFNumberCreate(NULL, kCFNumberSInt32Type, &Height));
	*/
	
	bool OpenglCompatible = false;
	CFDictionarySetValue(destinationPixelBufferAttributes, kCVPixelBufferOpenGLCompatibilityKey, OpenglCompatible ? kCFBooleanTrue : kCFBooleanFalse );
	
	//	gr: prefer RGBA for now on ios. OSX cannot do RGBA conversion
	//	todo: output meta (format, dimensions) back to unity
	OSType destinationPixelType = kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange;

#if (TARGET_OS_IPHONE==1)
	destinationPixelType = kCVPixelFormatType_32BGRA;
#endif
	
	/*	gr: from PopMovie
#if defined(TARGET_IOS)
	//	to get ios to use an opengl texture, we need to explicitly set the format to RGBA.
	//	None (auto) creates a non-iosurface compatible texture
	if ( OpenglCompatible )
	{
		//destinationPixelType = kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange;
		//destinationPixelType = kCVPixelFormatType_24RGB;
		destinationPixelType = kCVPixelFormatType_32BGRA;
	}
#endif
	*/
	CFDictionarySetValue(destinationPixelBufferAttributes,kCVPixelBufferPixelFormatTypeKey, CFNumberCreate(NULL, kCFNumberSInt32Type, &destinationPixelType));
	
	
	// Set the Decoder Parameters
	CFMutableDictionaryRef decoderParameters = CFDictionaryCreateMutable( Allocator, DictionaryCapacity, &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks);
	
	static bool AllowDroppedFrames = false;
	CFDictionarySetValue(decoderParameters,kVTDecompressionPropertyKey_RealTime, AllowDroppedFrames ? kCFBooleanTrue : kCFBooleanFalse );
	
	
	//const VTDecompressionOutputCallbackRecord callback = { OnDecompress, this };
	VTDecompressionOutputCallbackRecord* OnDecompressCallbackFunc = nil;
    OSStatus status =  VTDecompressionSessionCreate( Allocator,
                                                    self.formatDesc,
                                                    decoderParameters,
                                                    destinationPixelBufferAttributes,
                                                    OnDecompressCallbackFunc,
                                                    &_decompressionSession);
    NSLog(@"Video Decompression Session Create: \t %@", (status == noErr) ? @"successful!" : @"failed...");
    if ( status != noErr )
	{
		NSLog(@"\t\t VTD ERROR type: %d", (int)status);
	}
	else
	{
	    self.has_session = true;
	}
}


void CopyCGImageToBuffer(CGImageRef CgImage,unsigned char* Buffer,size_t BufferSize)
{
	CGColorSpaceRef ColorSpace = CGColorSpaceCreateDeviceRGB();
	auto Width = CGImageGetWidth(CgImage);
	auto Height = CGImageGetHeight(CgImage);

	auto BitsPerComponent = 8;
	auto ImageBytesPerPixel = 4;
	auto ImageSize = Width * Height * ImageBytesPerPixel;
	
	if ( BufferSize < ImageSize )
	{
		NSException* myException = [NSException
									exceptionWithName:@"Buffer too small"
									reason:@"Buffer too small"
									userInfo:nil];
		@throw myException;
	}
	
	
	/*CGImageAlphaInfo/CGBitmapInfo*/uint32_t BitmapFlags = 0;
	static bool SetByteOrder = false;
	if ( SetByteOrder )
		BitmapFlags |= (CGBitmapInfo)kCGBitmapByteOrder32Little;
	
	BitmapFlags = kCGImageAlphaPremultipliedLast;
	
	//	this writes to pixel buffers, don't need to do lots of conversion!
	auto RowPitchBytes = Width * ImageBytesPerPixel;
	CGContextRef Context = CGBitmapContextCreate( Buffer,
													 Width,
													 Height,
													 BitsPerComponent,
													 RowPitchBytes,
													 ColorSpace,
													 BitmapFlags);
	if ( !Context )
	{
		NSException* myException = [NSException
									exceptionWithName:@"Failed to create CGBitmapContext"
									reason:@"Failed to create CGBitmapContext"
									userInfo:nil];
		@throw myException;
	}
	
	CGContextDrawImage( Context, CGRectMake(0, 0, Width, Height), CgImage );
	CGColorSpaceRelease( ColorSpace );
	CGContextRelease( Context);

}

const char* GetErrorString(OSStatus Error)
{
	switch ( Error )
	{
		case kVTPropertyNotSupportedErr:				return "kVTPropertyNotSupportedErr";
		case kVTPropertyReadOnlyErr:					return "kVTPropertyReadOnlyErr";
		case kVTParameterErr:							return "kVTParameterErr";
		case kVTInvalidSessionErr:						return "kVTInvalidSessionErr";
		case kVTAllocationFailedErr:					return "kVTAllocationFailedErr";
		case kVTPixelTransferNotSupportedErr:			return "kVTPixelTransferNotSupportedErr";
		case kVTCouldNotFindVideoDecoderErr:			return "kVTCouldNotFindVideoDecoderErr";
		case kVTCouldNotCreateInstanceErr:				return "kVTCouldNotCreateInstanceErr";
		case kVTCouldNotFindVideoEncoderErr:			return "kVTCouldNotFindVideoEncoderErr";
		case kVTVideoDecoderBadDataErr:					return "kVTVideoDecoderBadDataErr";
		case kVTVideoDecoderUnsupportedDataFormatErr:	return "kVTVideoDecoderUnsupportedDataFormatErr";
		case kVTVideoDecoderMalfunctionErr:				return "kVTVideoDecoderMalfunctionErr";
		case kVTVideoEncoderMalfunctionErr:				return "kVTVideoEncoderMalfunctionErr";
		case kVTVideoDecoderNotAvailableNowErr:			return "kVTVideoDecoderNotAvailableNowErr";
		case kVTImageRotationNotSupportedErr:			return "kVTImageRotationNotSupportedErr";
		case kVTVideoEncoderNotAvailableNowErr:			return "kVTVideoEncoderNotAvailableNowErr";
		case kVTFormatDescriptionChangeNotSupportedErr:	return "kVTFormatDescriptionChangeNotSupportedErr";
		case kVTInsufficientSourceColorDataErr:			return "kVTInsufficientSourceColorDataErr";
		case kVTCouldNotCreateColorCorrectionDataErr:	return "kVTCouldNotCreateColorCorrectionDataErr";
		case kVTColorSyncTransformConvertFailedErr:		return "kVTColorSyncTransformConvertFailedErr";
		case kVTVideoDecoderAuthorizationErr:			return "kVTVideoDecoderAuthorizationErr";
		case kVTVideoEncoderAuthorizationErr:			return "kVTVideoEncoderAuthorizationErr";
		case kVTColorCorrectionPixelTransferFailedErr:	return "kVTColorCorrectionPixelTransferFailedErr";
		case kVTMultiPassStorageIdentifierMismatchErr:	return "kVTMultiPassStorageIdentifierMismatchErr";
		case kVTMultiPassStorageInvalidErr:				return "kVTMultiPassStorageInvalidErr";
		case kVTFrameSiloInvalidTimeStampErr:			return "kVTFrameSiloInvalidTimeStampErr";
		case kVTFrameSiloInvalidTimeRangeErr:			return "kVTFrameSiloInvalidTimeRangeErr";
		case kVTCouldNotFindTemporalFilterErr:			return "kVTCouldNotFindTemporalFilterErr";
		case kVTPixelTransferNotPermittedErr:			return "kVTPixelTransferNotPermittedErr";
		case kVTColorCorrectionImageRotationFailedErr:	return "kVTColorCorrectionImageRotationFailedErr";
		default:
			return nullptr;
	}

}

	int BufferCopyCount = 0;

CMBlockBufferRef AllocBufferCopy(uint8_t* Data,size_t DataLength)
{
	CFAllocatorRef Allocator = kCFAllocatorDefault;
	CFAllocatorRef Deallocator = kCFAllocatorNull;
	
	//	make a buffer mapped to the data provided
	CMBlockBufferRef DataBuffer = NULL;
	OSStatus Result = CMBlockBufferCreateWithMemoryBlock(Allocator,
														 Data,  // memoryBlock to hold buffered data
														 DataLength,  // block length of the mem block in bytes.
														 Deallocator,
														 NULL,
														 0, // offsetToData
														 DataLength,
														 0,
														 &DataBuffer);
	if ( Result != noErr )
		return nil;
	
	//	alloc non heap backed memory
	CMBlockBufferRef CopyBuffer = NULL;
	CMBlockBufferFlags Flags = kCMBlockBufferAlwaysCopyDataFlag;
	Result = CMBlockBufferCreateContiguous( Allocator, DataBuffer, Allocator, nil, 0, DataLength, Flags, &CopyBuffer );

	CFRelease(DataBuffer);

	BufferCopyCount++;
	return CopyBuffer;
}
	
void FreeBufferCopy(CMBlockBufferRef Buffer)
{
	BufferCopyCount--;
	CFRelease(Buffer);
}

- (void) create_frame:(const unsigned char *)data Size:(unsigned int)size
{
	//	gr: this changes code from NALU split to annex A data (with a size prefix instead of 0001)
	CMBlockBufferRef BlockBuffer = nil;
	{
		//	copy the data
		uint8_t* Buffer = static_cast<uint8_t*>( malloc(size) );
		memcpy( Buffer, data, size );

		//	replace nalu header with size
		uint32_t DataSize = htonl( size - 4 );	//	data-NALU
		memcpy( &Buffer[0], &DataSize, sizeof(DataSize) );

		BlockBuffer = AllocBufferCopy( Buffer, size );

		free (Buffer);
	}
	
	
// NSLog(@"\t\t BlockBufferCreation: \t %@", (status == kCMBlockBufferNoErr) ? @"successful!" : @"failed...");
    CMSampleBufferRef sampleBuffer = [self next_buffer];

	{
		//	gr: we're allocating a new sample buffer here... may need to free the old one?
		const size_t sampleSize = CMBlockBufferGetDataLength(BlockBuffer);
		OSStatus Result = CMSampleBufferCreate(kCFAllocatorDefault, BlockBuffer, true, NULL, NULL, _formatDesc, 1, 0, NULL, 1, &sampleSize, &sampleBuffer);

		if(Result != noErr)
		{
			CFRelease(BlockBuffer);
			return;
		}
		// NSLog(@"\t\t SampleBufferCreate: \t %@", (status == noErr) ? @"successful!" : @"failed...");
	}
    
    {
        self.busy = true;
		
		VTDecodeFrameFlags DecodeFlags = 0;
		
		//	output in display order
		DecodeFlags |= kVTDecodeFrame_EnableTemporalProcessing;
		DecodeFlags |= kVTDecodeFrame_EnableAsynchronousDecompression;
		
		//	gr: its possible if DecodeFlags != 0 that we are deleting the backing memory for the callback before it's used
		//	change the BlockBuffer allocation!
		//	gr: ditto for the sample buffer, could releasing early
		//	gr: synchronous locks up main thread though, so not an option
		//DecodeFlags = 0;
		
		
        VTDecompressionSessionDecodeFrameWithOutputHandler
        (_decompressionSession,
         sampleBuffer,
         DecodeFlags,
         NULL,
         ^(OSStatus status,
           VTDecodeInfoFlags infoFlags,
           CVImageBufferRef  _Nullable ImageBuffer,
           CMTime presentationTimeStamp,
           CMTime presentationDuration)
          {
             
              if (status != noErr)
              {
				  NSString* ErrorStr = [NSString stringWithFormat:@"%d", (int)status];

				  auto* ErrorString = GetErrorString(status);
				  if ( ErrorString != nullptr )
					ErrorStr = [NSString stringWithFormat:@"%s", ErrorString];

                 // NSError *error = [NSError errorWithDomain:NSOSStatusErrorDomain code:status userInfo:nil];
                  NSLog(@"Decompressed error: %@", ErrorStr);
			  }
              else
              {
				  auto Result = CVPixelBufferLockBaseAddress( ImageBuffer, kCVPixelBufferLock_ReadOnly );
				  //Avf::IsOkay( Result, "CVPixelBufferLockBaseAddress" );
				  auto* Pixels = (uint8_t*)( CVPixelBufferGetBaseAddress(ImageBuffer) );
				  //Soy::Assert( Pixels, "Missing pixels from CVPixelBufferGetBaseAddress");
				  auto DataSize = CVPixelBufferGetDataSize( ImageBuffer );
				  
				  NSData* NewPixelData = [[NSData alloc] initWithBytes:Pixels length:DataSize];
				  auto* NewPixelDataBytes = static_cast<const uint8_t*>( NewPixelData.bytes );
				  TBuffer NewPixelBuffer( const_cast<uint8_t*>( NewPixelDataBytes ), DataSize );
				  gDecodeSession.PushFrame( NewPixelBuffer );
				  
				  //	gr: was this the leak? unlock before copy?
				  Result = CVPixelBufferUnlockBaseAddress( ImageBuffer, kCVPixelBufferLock_ReadOnly );
				  //Avf::IsOkay( Result, "CVPixelBufferUnlockBaseAddress" );
				
				 // [self.delegate decodedData:NewPixelData];
				  //	gr: no need to have this on the main thread, just lock and make a buffer of buffers!
				 // [self.delegate performSelectorOnMainThread:@selector(decodedData:) withObject:NewPixelData waitUntilDone:YES];
			   }
				  
              
              self.busy=false;
            
         });
        
        CFRelease(sampleBuffer);
        sampleBuffer = NULL;
		FreeBufferCopy( BlockBuffer );
    }
	
}


- (bool) is_sps_or_pps:(const unsigned char *)data Size:(unsigned int)size
{
    int nt=[self nalu:data Size:size];
    if(nt== 7 || nt == 8)
    {
        return true;
    }
    return false;
}

- (bool) is_i_frame:(const unsigned char *)data Size:(unsigned int)size
{
    int nt=[self nalu:data Size:size];
    if(nt== 5 )
    {
        return true;
    }
    return false;
}


- (bool) frame:(const unsigned char *)data Size:(unsigned int)size
{
    int nt=[self nalu:data Size:size];
    NSLog(@"Nalu = %d size =%d",nt, size);
    
    if(nt == 7)
    {
        if(!self.has_session)
        {
            [self create_sps:data Size:size];
        }
        return false;
    }
    else if(nt == 8)
    {
        if(!self.has_session)
        {
            [self create_pps:data Size:size];
            [self create_session];
        }
        return false;
        
    }
    else if(nt == 5 || nt == 1)   // type 5 is an IDR frame NALU.  The SPS and PPS NALUs should always be followed by an IDR (or IFrame) NALU  NALU type 1 is non-IDR (or PFrame) picture
    {
        NSLog(@"Nalu = %@",(nt==5)?@"IDR (IFrame)":@"non-IDR (PFrame)");
        [self create_frame:data Size:size];
        return true;
        
    }
  
    return true;
    
}


- (int) nalu:(const unsigned char *)b Size:(unsigned int)size
{
    int i;
    int t=0;
    for(i=0; i< size-5; i++)
    {
        //look for start code
        if(b[i]==0 && b[i+1] ==0 && b[i+2]==0 && b[i+3]==1)
        {
            int nt = (int) (b[i+4]& 0x1F);
            return nt;
        }
    }
    return t;
}




@end





//C functions for Unity plugin
void VideoServiceDecodeFrame(void * b, int size)
{
	NSLog(@"VideoServiceDecodeFrame called  size = %d",size);
	
	if ( b == nil )
	{
		NSLog(@"VideoServiceDecodeFrame null data");
		return;
	}
	
	[[VideoService videoservice] decode:b Size:size];
}

//returns 0 if there isn't any data and the size if there is , so that mmemory can be allocated in C#
int VideoServiceHasFrame()
{
	auto NextFrameSize = static_cast<int>( gDecodeSession.GetNextFrameSize() );
	return NextFrameSize;
}

int VideoServiceCopyFrame(unsigned char* buffer)
{
	//	for safety we should pass this size
	size_t BufferSize = 640 * 480 * 4;
	TBuffer Buffer( buffer, BufferSize );

	auto CopiedSize = gDecodeSession.PopFrame( Buffer );
	return CopiedSize;
}


//this is so that internal ViewContoller can use the same methods as plugin
void VideoServiceSetDelegate(id obj)
{
	[VideoService videoservice].delegate = obj;
}


@implementation VideoService

+ (VideoService*) videoservice
{
	static VideoService *videoservice_instance = nil;
	if( !videoservice_instance )
		videoservice_instance = [[VideoService alloc] init];
	return videoservice_instance;
}
-(id) init
{
	self = [super init];
	self.decoder = [[DecodeService alloc] init];
	self.decoder.delegate=self;
	self.decode_q = [[NSMutableArray alloc] init];
	self.completed_q = [[NSMutableArray alloc] init];
	return self;
	
}



- (bool ) next
{
	if([self.decode_q count] > 0)
	{
		NSData * nextData = [self.decode_q objectAtIndex:0];
		[self.decode_q removeObjectAtIndex:0];
		[self.decoder frame:(const unsigned char *)nextData.bytes Size:(unsigned int)nextData.length];
		return true;
	}
	
	return false;
	
}

- (void) decode:(void *) b Size:(int) size
{
	NSLog(@"VideoService dqc = %d  size = %d and busy = %@",
		  (int)[self.decode_q count],
		  size,
		  self.decoder.busy?@"TRUE":@"FALSE");
	
	//	gr: this can be removed, get rid of the decoder queue - maybe it's required in case SPS/PPS isn't definitely going to be first
	
	auto* Data = (const uint8_t*)b;
	
	if(self.decoder.has_session && [self.decoder is_sps_or_pps:Data Size:size])
	{
		[self next];
		
		return;
	}
	
	[self.decoder frame:(const unsigned char *)b Size:(unsigned int)size];
}


@end


