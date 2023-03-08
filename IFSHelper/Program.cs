using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Fclp;
using Fclp.Internals.Extensions;
using Fclp.Internals.Errors;
using Fclp.Internals.Parsing;
using Fclp.Internals.Validators;
using System.IO;
using IO = System.IO;

using Utils.LZO;
using LZO = Utils.LZO;

using System.Runtime.InteropServices;
using System.Collections.Specialized;
using BitField;

namespace IFSHelper
{
    public static class ExtensionMethods
    {
        public static IEnumerable<int> IndexOf<T>(this T[] haystack, T[] needle)
        {
            if ((needle != null) && (haystack.Length >= needle.Length))
            {
                for (int l = 0; l < haystack.Length - needle.Length + 1; l++)
                {
                    if (!needle.Where((data, index) => !haystack[l + index].Equals(data)).Any())
                    {
                        yield return l;
                    }
                }
            }
        }

        public static T[] SubArray<T>(this T[] data, int index, int length)
        {
            T[] result = new T[length];
            Array.Copy(data, index, result, 0, length);
            return result;
        }

        public static ushort Swap(this ushort x)
        {
            return (ushort)((ushort)((x & 0xff) << 8) | ((x >> 8) & 0xff));
        }

        public static UInt32 Swap(this UInt32 value)
        {
            return (value & 0x000000FFU) << 24 | (value & 0x0000FF00U) << 8 |
                   (value & 0x00FF0000U) >> 8 | (value & 0xFF000000U) >> 24;
        }

    }

    public class IFS
    {
        
        [Flags]
        public enum CompressionFlags: byte 
        { 
            STARTUP_HDR_FLAGS1_COMPRESS_MASK	= 0x1c,
            STARTUP_HDR_FLAGS1_COMPRESS_SHIFT	= 0x02,
            STARTUP_HDR_FLAGS1_COMPRESS_NONE	= 0x00,
            STARTUP_HDR_FLAGS1_COMPRESS_ZLIB	= 0x04,
            STARTUP_HDR_FLAGS1_COMPRESS_LZO		= 0x08,
            STARTUP_HDR_FLAGS1_COMPRESS_UCL		= 0x0c ,
        };

        [Flags]
        public enum HeaderFlags : byte
        {
            STARTUP_HDR_FLAGS1_VIRTUAL = 0x1, // implied that if not present, then it is an original
            STARTUP_HDR_FLAGS1_BIGENDIAN = 0x2
        };
     
        unsafe public struct STARTUP_HEADER
        {
            public UInt32 signature;
            public UInt16 version;
            public byte flags1;
            public byte flags2;
            public UInt16 header_size;
            public UInt16 machine;
            public UInt32 startup_vaddr;

            public UInt32 paddr_bias;

            public UInt32 image_paddr;
            public UInt32 ram_paddr;

            public UInt32 ram_size;

            public UInt32 startup_size;
            public UInt32 stored_size;
            public UInt32 imagefs_paddr;

            public UInt32 imagefs_size;
            public UInt16 preboot_size;
            public UInt16 zero0;
            public fixed UInt16 zero[3];
            public fixed UInt32 info[48];
        };

    };
    public class ApplicationArguments
    {
        public bool Compress { get; set; }
        public bool Decompress { get; set; }
        public string InFile { get; set; }
        public string OutFile { get; set; }
        public bool Verbose { get; set; }
        public bool Optimize { get; set; }
        public bool Split { get; set; }
        public bool Merge { get; set; }
        public bool Checksum { get; set; }
    }

    class Program
    {
        private static bool Verbose = false;
        private static bool Optimize = false;

        private static void WriteVerbose(string s, object arg0)
        {
            if (Verbose)
            {
                Console.WriteLine(s, arg0);
            }
        }
        private static void WriteVerbose(string s, params object[] args)
        {
            if (Verbose)
            {
                Console.WriteLine(s, args);
            }
        }

        private static void Dump(object myObj)
        {
            foreach (var prop in myObj.GetType().GetProperties())
            {
                Console.WriteLine(prop.Name + ": " + prop.GetValue(myObj, null));
            }

            foreach (var field in myObj.GetType().GetFields())
            {
                Console.WriteLine(field.Name + ": " + field.GetValue(myObj));
            }
        }

        private static void DumpSignature(IFS.STARTUP_HEADER header)
        {
            Console.WriteLine("SIGNATURE: 0x{0:X8} VERSION: {1}", header.signature, header.version);
            IFS.CompressionFlags compressionFlags = (IFS.CompressionFlags)(header.flags1);
            Console.WriteLine("Compression: {0}", Enum.GetName(typeof(IFS.CompressionFlags), compressionFlags));
            Console.WriteLine("Image size: {0}", header.imagefs_size);
         

            //Console.WriteLine("Flags    : {0}", ((IFS.CompressionFlags)header.flags1).ToString());

        }

        private static UInt32 Checksum(byte[] bytes)
        {
            UInt32 checksum = 0;
            
            for (var i=0 ; i < bytes.Length ; i +=4)
            {
                if (bytes.Length - (i + 4) >= 0)
                {
                    checksum += BitConverter.ToUInt32(bytes, i);
                }
                else
                {
                    Console.Beep();
                }
            }

            return checksum;
        }

        private static bool Compress(string InFile, string OutFile, bool Optimize=false)
        {
            bool result = false;
            byte[] bytes = IO.File.ReadAllBytes(InFile);
            GCHandle pinnedBytes = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                IFS.STARTUP_HEADER header = (IFS.STARTUP_HEADER)Marshal.PtrToStructure(
                    pinnedBytes.AddrOfPinnedObject(),
                    typeof(IFS.STARTUP_HEADER));
                byte[] compressedIFS = new byte[header.stored_size];
                byte[] buffer = null;

                // copy header + checksum
                //Buffer.BlockCopy(bytes, 0, compressedIFS, 0, header.header_size + 4);
                Buffer.BlockCopy(bytes, 0, compressedIFS, 0, (int)header.startup_size);
                LZO.Compressor compressor = new LZO.Compressor(65536, 9);

                //int readPos = header.header_size + 4;
                int readPos = (int)header.startup_size;
                int writePos = readPos;

                UInt32 outLength = 0;
                var x = Console.CursorLeft;
                var y = Console.CursorTop;
                int count = 0;
                ushort elementSize = 0;
                int blockRead = 0;
                int blockWrite = 0;
                byte[] inBuffer = null;

                while (readPos < bytes.Length)
                {
                    count = Math.Min(65536, bytes.Length - readPos);
                    inBuffer = bytes.SubArray(readPos, count);
                    var r = compressor.Compress(inBuffer, (UInt32)count, ref buffer, ref outLength);
                    readPos += count;
                    blockRead++;

                    if (r)
                    {
                        //WriteVerbose("Compressed block from {0} to {1}", count, outLength);
                        
                        if (Optimize)
                        {
                            var b = compressor.Optimize(buffer, inBuffer);
                            if (! b)
                            {
                                WriteVerbose("Optimize failed");
                            }
                        }
                        elementSize = (ushort)outLength;
                        elementSize = elementSize.Swap();
                        
                        // write compressed block size
                        Buffer.BlockCopy(BitConverter.GetBytes(elementSize), 0, compressedIFS, writePos, 2);
                        writePos += 2;
                        
                        // write compressed data
                        Buffer.BlockCopy(buffer, 0, compressedIFS, writePos, (int)outLength);
                        writePos += (int)outLength;
                        blockWrite++;

                        //WriteVerbose("readPos: {0}/{1} writePos: {2}/{3} blockRead: {4} blockWrite: {5}", readPos, bytes.Length, writePos, header.stored_size, blockRead,blockWrite);
                    }
                    else
                    {
                        Console.WriteLine("Compression failure {0} occured! (this should not happen)", r);
                    }
                    Console.CursorLeft = x;
                    Console.CursorTop = y;
                    Console.Write("Compressing: {0:P0}", (double)readPos / bytes.Length);

                    
                }

                //WriteVerbose("Exited loop with readPos {0}/{1}", readPos, bytes.Length);
                //image_paddr + startup_size, stored_size - startup_size

//                checksum(image_paddr, startup_size);
//                checksum(image_paddr + startup_size, stored_size - startup_size);
                Console.WriteLine("");

                var storedStartupChecksum = BitConverter.ToUInt32(compressedIFS, (int)header.startup_size-4);
                WriteVerbose("Startup Checksum read      : 0x{0:X8}", storedStartupChecksum);
                var startupBytes = compressedIFS.SubArray(0, (int)header.startup_size-4);
                var startupChecksum = Checksum(startupBytes);

                if (startupChecksum + storedStartupChecksum == 0)
                {
                    WriteVerbose("Startup Checksum calculated: 0x{0:X8} (match)", storedStartupChecksum);
                }
                else
                {
                    var calcChecksum = 0 - startupChecksum;
                    WriteVerbose("Startup Checksum calculated: 0x{0:X8} (written)", calcChecksum);
                    Buffer.BlockCopy(BitConverter.GetBytes(calcChecksum), 0, compressedIFS, (int)(header.startup_size - 4), 4);
                }

                var storedTotalChecksum = BitConverter.ToUInt32(compressedIFS, compressedIFS.Length - 4);
                WriteVerbose("Overall Checksum read      : 0x{0:X8}", storedTotalChecksum);
                var totalChecksum = Checksum(compressedIFS.SubArray(0, compressedIFS.Length - 4));
                if (totalChecksum + storedTotalChecksum == 0)
                {
                    WriteVerbose("Overall Checksum calculated: 0x{0:X8} (match)", storedTotalChecksum);
                }
                else
                {
                    var calcChecksum = 0 - totalChecksum;
                    WriteVerbose("Overall Checksum calculated: 0x{0:X8} (written)", calcChecksum);
                    Buffer.BlockCopy(BitConverter.GetBytes(calcChecksum), 0, compressedIFS, compressedIFS.Length - 4, 4);
                }

                var storedImageChecksum = BitConverter.ToUInt32(compressedIFS, writePos - 7);
                WriteVerbose("Image Checksum read        : 0x{0:X8}", storedImageChecksum);
                var imageBytes = compressedIFS.SubArray((int)header.startup_size, (int)(compressedIFS.Length - header.startup_size));
                var imageChecksum = Checksum(imageBytes) - storedImageChecksum;

                if (imageChecksum + storedImageChecksum == 0)
                {
                    WriteVerbose("Image Checksum calculated  : 0x{0:X8} (match)", storedImageChecksum);
                }
                else
                {
                    var calcChecksum = 0 - imageChecksum;
                    WriteVerbose("Image Checksum calculated  : 0x{0:X8} (written)", calcChecksum);
                    Buffer.BlockCopy(BitConverter.GetBytes(calcChecksum), 0, compressedIFS, writePos - 7, 4);                   
                }


//                Console.WriteLine("Startup Checksum: 0x{0:X8}", startupChecksum);
//                Console.WriteLine("Image Checksum  : 0x{0:X8}", imageChecksum);
                //Console.WriteLine("Total Checksum  : 0x{0:X8}", totalChecksum);
                //Buffer.BlockCopy(BitConverter.GetBytes(imageChecksum), 0, compressedIFS, compressedIFS.Length - 4, 4);

                IO.File.WriteAllBytes(OutFile, compressedIFS);

            }
            
            finally
            {
                pinnedBytes.Free();
            }

            Console.WriteLine("\nFinished."); 
            
            return result;
        }

        private static void DoChecksum(string InFile)
        {
            IFS.STARTUP_HEADER header;
            int offset = 0;
            byte[] bytes = IO.File.ReadAllBytes(InFile);
            GCHandle pinnedBytes = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {

                while (offset + 256 < bytes.Length)
                {
                    header = (IFS.STARTUP_HEADER)Marshal.PtrToStructure(
                        pinnedBytes.AddrOfPinnedObject() + offset,
                        typeof(IFS.STARTUP_HEADER));

                    if (header.signature == 0x00FF7EEB)
                    {
                        var storedStartupChecksum = BitConverter.ToUInt32(bytes, (int)header.startup_size - 4);
                        WriteVerbose("Startup Checksum read      : 0x{0:X8}", storedStartupChecksum);

                        var startupBytes = bytes.SubArray(0, (int)header.startup_size - 4);
                        var startupChecksum = Checksum(startupBytes);

                        if (startupChecksum + storedStartupChecksum == 0)
                        {
                            WriteVerbose("Startup Checksum calculated: 0x{0:X8} (match)", storedStartupChecksum);
                        }
                        else
                        {
                            var calcChecksum = 0 - startupChecksum;
                            WriteVerbose("Startup Checksum calculated: 0x{0:X8}", calcChecksum);
                        }

                        var checksumPos = bytes.Length - 4;
                        byte b = 0x0;
                        while (b != 0x11)
                        {
                            checksumPos--;
                            b = bytes[checksumPos];
                        }

                        checksumPos -= 4;
                        var storedImageChecksum = BitConverter.ToUInt32(bytes, checksumPos);
                        WriteVerbose("Image Checksum read        : 0x{0:X8}", storedImageChecksum);
                        
                        var imageBytes = bytes.SubArray((int)header.startup_size, (int)(bytes.Length - header.startup_size));
                        var imageChecksum = Checksum(imageBytes) - storedImageChecksum;

                        if (imageChecksum + storedImageChecksum == 0)
                        {
                            WriteVerbose("Image Checksum calculated  : 0x{0:X8} (match)", storedImageChecksum);
                        }
                        else
                        {
                            var calcChecksum = 0 - imageChecksum;
                            WriteVerbose("Image Checksum calculated  : 0x{0:X8}", calcChecksum);
                        }

                        var storedOverallChecksum = BitConverter.ToUInt32(bytes, bytes.Length - 4);
                        WriteVerbose("Overall Checksum read      : 0x{0:X8}", storedOverallChecksum);

                        var overallBytes = bytes.SubArray(0, bytes.Length - 4);
                        var overallChecksum = Checksum(overallBytes);

                        if (overallChecksum + storedOverallChecksum == 0)
                        {
                            WriteVerbose("Overall Checksum calculated: 0x{0:X8} (match)", storedOverallChecksum);
                        }
                        else
                        {
                            var calcChecksum = 0 - overallChecksum;
                            WriteVerbose("Overall Checksum calculated: 0x{0:X8}", calcChecksum);
                        }


                        break;
                    }
                    
                    offset += 4;
                }

            }
            finally
            {
                pinnedBytes.Free();
            }


        }

        private static bool Decompress(string InFile, string OutFile)
        {
            Console.WriteLine("Decompressing {0} to {1}", InFile, OutFile);
            bool result = false;

            byte[] bytes = IO.File.ReadAllBytes(InFile);
            GCHandle pinnedBytes = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                IFS.STARTUP_HEADER header = (IFS.STARTUP_HEADER)Marshal.PtrToStructure(
                    pinnedBytes.AddrOfPinnedObject(),
                    typeof(IFS.STARTUP_HEADER));
                
                Console.WriteLine("SIGNATURE: 0x{0:X8} VERSION: {1}", header.signature, header.version);
                IFS.CompressionFlags compressionFlags = (IFS.CompressionFlags)(header.flags1);
                byte c = (byte)(header.flags1 & 0x1C);

                Console.WriteLine("Compression: {0}", Enum.GetName(typeof(IFS.CompressionFlags), c));
                Console.WriteLine("Stored size: {0} (Uncompressed: {1})", header.stored_size, header.imagefs_size);

                UInt32 startupChecksum = BitConverter.ToUInt32(bytes, header.header_size);
                UInt32 startupChecksumCalculated = Checksum(bytes.SubArray(header.header_size, (int)(header.stored_size - header.header_size)));
                Console.WriteLine("Startup Checksum: 0x{0:X8} (Calculated: 0x{0:X8})", startupChecksum, startupChecksumCalculated);

                ushort elementSize = 0;
                //int pos = header.header_size + 4;
                int pos = (int)header.startup_size;
                //int i = header.header_size + 4;
                int i = pos;
                byte[] outBuffer = null;
                //byte[] uncompressedIFS = new byte[header.imagefs_size + header.header_size + 4];
                byte[] uncompressedIFS = new byte[header.imagefs_size + header.startup_size];
                uint outLength = 0;

                // copy IFS header and checksum
                //Buffer.BlockCopy(bytes, 0, uncompressedIFS, 0, header.header_size + 4);
                Buffer.BlockCopy(bytes, 0, uncompressedIFS, 0, (int)header.startup_size);

                var x = Console.CursorLeft;
                var y = Console.CursorTop;

                LZO.Compressor compressor = new LZO.Compressor(65536, 9);
                while (true)
                {
                    elementSize = BitConverter.ToUInt16(bytes, i).Swap();
                    if (elementSize == 0)
                    {
                        break;
                    }
                    
                    i += 2;
                    var r = compressor.DeCompress(bytes.SubArray(i, elementSize), elementSize, ref outBuffer, ref outLength);
                    Buffer.BlockCopy(outBuffer, 0, uncompressedIFS, pos, (int)outLength);
                    pos += (int)outLength;

                    i += (int)elementSize;

                    Console.CursorLeft = x;
                    Console.CursorTop = y;
                    Console.Write("Decompressing: {0:P0}", (double)pos / uncompressedIFS.Length);
                }

                File.WriteAllBytes(OutFile, uncompressedIFS);
                Console.WriteLine("\nFinished.");
            }
            finally
            {
                pinnedBytes.Free();
            }

            return result;
        }

        private static bool Split(string InFile, string OutFile)
        {
            
            byte[] inBytes = File.ReadAllBytes(InFile);
            uint pos = 0;

            GCHandle pinnedBytes = GCHandle.Alloc(inBytes, GCHandleType.Pinned);
            try
            {
                byte[] signatureBytes = BitConverter.GetBytes(0x00ff7eeb);
                byte[] buffer = null;
                var offsets = inBytes.IndexOf(signatureBytes).ToList();

                if (offsets.Count() == 0)
                {
                    WriteVerbose("No IFS offsets found...");

                    return false;
                }

                var i = 1;
                foreach (var offset in offsets)
                { 
                    WriteVerbose("Possible IFS offset: 0x{0} ({0})", offset);
                    IFS.STARTUP_HEADER header;

                    if (inBytes.Length - offset > 256)
                    {
                        if (i ==1 && offset > 0)
                        {
                            buffer = new byte[offset];
                            Buffer.BlockCopy(inBytes, 0, buffer, 0, offset);
                            var filename = Path.Combine(OutFile, "ifs0.bin");
                            WriteVerbose("Dumping header bytes to {0}", filename);
                            File.WriteAllBytes(filename, buffer);
                            pos += (uint)buffer.Length;
                        }

                        WriteVerbose("Enough length for STARTUP_HEADER");
                        header = (IFS.STARTUP_HEADER)Marshal.PtrToStructure(
                            pinnedBytes.AddrOfPinnedObject() + offset,
                            typeof(IFS.STARTUP_HEADER));
                        
                        if (header.stored_size <= inBytes.Length - offset)
                        {
                            WriteVerbose("Enough length for stored size {0}", header.stored_size);
                            buffer = new byte[header.stored_size];
                            Buffer.BlockCopy(inBytes, offset, buffer, 0, (int)header.stored_size);
                            pos += header.stored_size;

                            var filename = Path.Combine(OutFile, String.Format("ifs{0}.ifs", i));
                            WriteVerbose("Dumping IFS to {0}", filename);
                            File.WriteAllBytes(filename, buffer);
                            
                            if (i < offsets.Count)
                            { 
                                var nextOffset = offsets[i];
                                if (pos < nextOffset)
                                {
                                    buffer = new byte[nextOffset - pos];
                                    Buffer.BlockCopy(inBytes, (int)pos, buffer, 0, buffer.Length);
                                    filename = Path.Combine(OutFile, String.Format("ifs{0}.bin", i+1));
                                    WriteVerbose("Dumping {0} empty bytes to {1}", buffer.Length, filename);
                                    File.WriteAllBytes(filename, buffer);
                                    pos += (uint)buffer.Length;
                                }
                            }
                            else
                            {
                                var footerSize = inBytes.Length - pos;
                                WriteVerbose("Footer size: {0}", footerSize);
                                if (footerSize > 0)
                                {
                                    buffer = new byte[footerSize];
                                    Buffer.BlockCopy(inBytes, (int)pos, buffer, 0, buffer.Length);
                                    filename = Path.Combine(OutFile, String.Format("ifs{0}.bin", i+1));
                                    WriteVerbose("Dumping {0} footer bytes to {1}", buffer.Length, filename);
                                    File.WriteAllBytes(filename, buffer);
                                    pos += (uint)buffer.Length;
                                }
                            }
                            i++;
                        }
                        else
                        {
                            WriteVerbose("not enough space for STARTUP_HEADER");
                        }
                    }
                }
            }
            finally
            {
                pinnedBytes.Free();
            }
            
            return true;
        }

        private static bool Merge(string InFile, string OutFile)
        {
            byte[] buffer;
            int pos = 0;
           
            // get total size
            var files = Directory.GetFiles(InFile, "ifs?.*").ToList();
            files.Sort();

            long totalSize = 0;
            files.ForEach(x => totalSize += new FileInfo(x).Length);

            // allocate out buffer
            byte[] outBuffer = new byte[totalSize];
            WriteVerbose("file            offset          size");
            WriteVerbose("------------------------------------");
            foreach (var file in files)
            {
                buffer = File.ReadAllBytes(file);

                WriteVerbose("{0}{1,14:#,#}{2,14:#,#}", Path.GetFileName(file), pos, buffer.Length);
                Buffer.BlockCopy(buffer, 0, outBuffer, pos, buffer.Length);
                pos += buffer.Length;
            }

            WriteVerbose("\nMerged to {0} with filesize {1:#,#}", Path.GetFileName(OutFile), outBuffer.Length);
            File.WriteAllBytes(OutFile, outBuffer);
            
            return true;
        }

        static int Main(string[] args)
        {
            Console.WriteLine("IFSHelper 0.6 (c) 2016 Remko Weijnen");
            // create a generic parser for the ApplicationArguments type
            var p = new FluentCommandLineParser<ApplicationArguments>();

            // specify which property the value will be assigned too.
            p.Setup(arg => arg.InFile)
            .As(CaseType.CaseInsensitive, "i", "InFile")
            .WithDescription("Input filename (IFS)")
            .Required();

            p.Setup(arg => arg.OutFile)
             .As(CaseType.CaseInsensitive, "o", "OutFile")
            .WithDescription("Output filename (IFS)");

            p.Setup(arg => arg.Optimize)
             .As(CaseType.CaseInsensitive, "Optimize")
            .WithDescription("Optimize LZO compressions");

            p.Setup(arg => arg.Checksum)
             .As(CaseType.CaseInsensitive, "Checksum")
            .WithDescription("Read and calculate IFS checksums");

            p.Setup(arg => arg.Compress)
            .As(CaseType.CaseInsensitive, "c", "Compress")
            .WithDescription("Compress IFS with LZO compression");

            p.Setup(arg => arg.Decompress)
            .As(CaseType.CaseInsensitive, "d", "Decompress")
            .WithDescription("DeCompress an LZO compressed IFS");

            p.Setup(arg => arg.Verbose)
            .As(CaseType.CaseInsensitive, "v", "Verbose")
            .WithDescription("Verbose output");

            p.Setup(arg => arg.Split)
            .As(CaseType.CaseInsensitive, "s", "Split")
            .WithDescription("Split IFS Files");

            p.Setup(arg => arg.Merge)
            .As(CaseType.CaseInsensitive, "m", "Merge")
            .WithDescription("Merge 2 IFS Files");


            p.SetupHelp("?", "help")
            .Callback(text => Console.WriteLine("Compresses or decompresses an IFS file.\n" + text));

            var result = p.Parse(args);

            LZO.Compressor c = new LZO.Compressor();
            var r1 = c.ParseFlags(0x8);
            var r2 = c.ParseFlags(0x9);
            var r3 = c.ParseFlags(13);
            if (result.HasErrors)
            {
                p.HelpOption.ShowHelp(p.Options);
                return 1;
            }

            Console.WriteLine("Input file : {0}", p.Object.InFile);
            Console.WriteLine("Output file: {0}", p.Object.OutFile);
            Console.WriteLine("Compress   : {0}", p.Object.Compress);
            Console.WriteLine("Decompress : {0}", p.Object.Decompress);

            Verbose = p.Object.Verbose;
            Optimize = p.Object.Optimize;
            if (!System.IO.File.Exists(p.Object.InFile) && !p.Object.Merge)
            {
                Console.WriteLine("Input file {0} not found", p.Object.InFile);
                return 2;
            }

            if (p.Object.Compress)
            {
                Compress(p.Object.InFile, p.Object.OutFile, Optimize);
            }
            else if (p.Object.Decompress)
            {
                Decompress(p.Object.InFile, p.Object.OutFile);
            }
            else if (p.Object.Split)
            {
                Split(p.Object.InFile, p.Object.OutFile);
            }
            else if (p.Object.Merge)
            {
                Merge(p.Object.InFile, p.Object.OutFile);
            }
            else if (p.Object.Checksum)
            {
                DoChecksum(p.Object.InFile);
            }

            //Console.WriteLine("Press any key to continue...");
            //Console.ReadKey();

            return 0;
        }
    }
}
