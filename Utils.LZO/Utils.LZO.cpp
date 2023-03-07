// This is the main DLL file.

#include "stdafx.h"

#include "Utils.LZO.h"

namespace Utils
{
	namespace LZO
	{
		Compressor::Compressor()
		{
			this->Init();
		}
		Compressor::Compressor(UInt32 Blocksize, int Level)
		{
			this->BlockSize = Blocksize;
			this->Level = Level;

			this->Init();
		}

		UInt32 Compressor::BlockSize::get()
		{
			return m_BlockSize;
		}

		void Compressor::BlockSize::set(UInt32 value)
		{
			m_BlockSize = value;
		}

		int Compressor::Level::get()
		{
			return m_Level;
		}

		void Compressor::Level::set(int value)
		{
			m_Level = value;
		}


		bool Compressor::Init()
		{
			return lzo_init() == LZO_E_OK;
		}

		bool Compressor::Compress(array<System::Byte>^ Source, UInt32 SourceLength, array<System::Byte>^ %Destination, UInt32 %DestLength)
		{
			int r = 0;
			lzo_uint32 wrk_len = 0;
			lzo_byte *wrkmem = NULL;
			lzo_byte *in = NULL;
			lzo_byte *out = NULL;
			lzo_uint in_len;
			lzo_uint out_len;

			if (m_Level == 9)
				wrk_len = LZO1X_999_MEM_COMPRESS;
			else
				wrk_len = LZO1X_1_MEM_COMPRESS;

			wrkmem = (lzo_bytep)lzo_malloc(wrk_len);
			try
			{
				in_len = SourceLength;

				pin_ptr<System::Byte> p = &Source[0];
				in = reinterpret_cast<lzo_byte*>(p);

				out = (lzo_bytep)lzo_malloc(m_BlockSize + m_BlockSize / 64 + 16 + 3);
				try
				{
					if (m_Level == 9)
						r = lzo1x_999_compress(in, in_len, out, &out_len, wrkmem);
					else
						r = lzo1x_1_compress(in, in_len, out, &out_len, wrkmem);

					if (r != LZO_E_OK || out_len > in_len + in_len / 64 + 16 + 3)
					{
						/* this should NEVER happen */
						throw gcnew System::Exception(String::Format("internal error - compression failed: %d", r));
					}

					Destination = gcnew array< Byte >(out_len);
					Marshal::Copy((IntPtr)out, Destination, 0, out_len);
					DestLength = out_len;
				}
				finally
				{
					lzo_free(out);
				}
			}
			finally
			{
				lzo_free(wrkmem);
			}
			return r == LZO_E_OK;
		}

		bool Compressor::DeCompress(array<System::Byte>^ Source, UInt32 SourceLength, array<System::Byte>^ %Destination, UInt32 %DestLength)
		{
			int r = 0;
			lzo_byte *wrkmem = NULL;
			lzo_byte *in = NULL;
			lzo_byte *out = NULL;
			lzo_uint in_len;
			lzo_uint out_len;

			in_len = SourceLength;
			GCHandle pinnedSource = GCHandle::Alloc(Source, GCHandleType::Pinned);
			try
			{
				pin_ptr<System::Byte> p = &Source[0];
				in = reinterpret_cast<lzo_byte*>(p);

				out = (lzo_bytep)lzo_malloc(m_BlockSize + m_BlockSize / 64 + 16 + 3);
				try
				{
					r = lzo1x_decompress(in, in_len, out, &out_len, NULL);

					Destination = gcnew array< Byte >(out_len);
					Marshal::Copy((IntPtr)out, Destination, 0, out_len);
					DestLength = out_len;
				}
				
				finally
				{
					lzo_free(out);
				}
			}
			
			finally
			{
				pinnedSource.Free();
			}

			return r == LZO_E_OK;
		}

		bool Compressor::Optimize(array<System::Byte>^ PackedBytes, array<System::Byte>^ UnpackedBytes)
		{
			int r = 0;
			lzo_byte *in = NULL;
			lzo_byte *out = NULL;
			lzo_uint out_len;
			lzo_uint32 wrk_len = 0;
			//lzo_byte *wrkmem = NULL;

			if (m_Level == 9)
				wrk_len = LZO1X_999_MEM_COMPRESS;
			else
				wrk_len = LZO1X_1_MEM_COMPRESS;

			//wrkmem = (lzo_bytep)lzo_malloc(wrk_len);
			try
			{

				GCHandle pinnedPackedBytes = GCHandle::Alloc(PackedBytes, GCHandleType::Pinned);
				GCHandle pinnedUnpackedBytes = GCHandle::Alloc(UnpackedBytes, GCHandleType::Pinned);
				try
				{
					pin_ptr<System::Byte> p = &PackedBytes[0];
					in = reinterpret_cast<lzo_byte*>(p);

					pin_ptr<System::Byte> unPackedPtr = &UnpackedBytes[0];
					out = reinterpret_cast<lzo_byte*>(unPackedPtr);


					r = lzo1x_optimize(in, PackedBytes->Length, out, &out_len, NULL);
					
					return r == LZO_E_OK;// && out_len == PackedBytes->Length;

				}
				finally
				{
					pinnedPackedBytes.Free();
					pinnedUnpackedBytes.Free();
				}
			}
			finally
			{
				//lzo_free(wrkmem);
			}
		}
		
		FLAGS1^ Compressor::ParseFlags(Byte Flags)
		{
			Flags1 flags;
			flags.Value = Flags;

			FLAGS1^ result = gcnew FLAGS1();
			result->BigEndian = flags.Flags.BigEndian;
			result->BigEndian = flags.Flags.Virtual;
			result->Compression = flags.Flags.Compression;

			return result;

		}
	};
};
