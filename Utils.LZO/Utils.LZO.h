// Utils.LZO.h

#pragma once
#include <lzo1x.h>
#include <lutil.h>
#pragma comment(lib, "lzo.lib")

using namespace System;
using namespace System::Runtime::InteropServices;

namespace Utils
{
	namespace LZO
	{
		typedef struct
		{
			Byte Virtual : 1;
			Byte BigEndian : 1;
			Byte Compression : 4;
			Byte Padding : 2;
		} BITFIELD;

		typedef struct Flags1
		{
			union {
				BITFIELD Flags;
				Byte Value;
			};
		};

		public ref struct FLAGS1
		{
			bool Virtual;
			bool BigEndian;
			int Compression;
		};

		public ref class Compressor
		{
		private:
			static UInt32 m_BlockSize;// = 65536;
			static int m_Level = 9;
			static bool Init();
		public:
			property UInt32 BlockSize { UInt32 get(); void set(UInt32 value); }
			property int Level { int get(); void set(int value); }
			Compressor(UInt32 Blocksize, int Level);
			Compressor();

			bool Compress(array<System::Byte>^ Source, UInt32 SourceLength, array<System::Byte>^ %Destination, UInt32 %DestLength);
			bool DeCompress(array<System::Byte>^ Source, UInt32 SourceLength, array<System::Byte>^ %Destination, UInt32 %DestLength);
			bool Optimize(array<System::Byte>^ PackedBytes, array<System::Byte>^ UnpackedBytes);

			FLAGS1^ ParseFlags(Byte Flags);
		};
	};
};