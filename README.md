# ifshelper

Tool to unpack VW / VAG MIB QNX files, includes functionality to split joined IFS, remerge them, decompress and compress
```
IFSHelper 0.6 (c) 2016 Remko Weijnen
Compresses or decompresses an IFS file.

        [i, ]:[InFile, ]                Input filename (IFS)
        [o, ]:[OutFile, ]               Output filename (IFS)
        [Optimize, ]            Optimize LZO compressions
        [Checksum, ]            Read and calculate IFS checksums
        [c, ]:[Compress, ]              Compress IFS with LZO compression
        [d, ]:[Decompress, ]            DeCompress an LZO compressed IFS
        [v, ]:[Verbose, ]               Verbose output
        [s, ]:[Split, ]         Split IFS Files
        [m, ]:[Merge, ]         Merge 2 IFS Files
```
Examples:


Decompress
```
IFSHelper.exe /i PCM3_IFS1_MOPF.ifs /o test.ifs /d

IFSHelper 0.6 (c) 2016 Remko Weijnen
Input file : PCM3_IFS1_MOPF.ifs
Output file: test.ifs
Compress   : False
Decompress : True
Decompressing PCM3_IFS1_MOPF.ifs to test.ifs
SIGNATURE: 0x00FF7EEB VERSION: 1
Compression: STARTUP_HDR_FLAGS1_COMPRESS_LZO
Stored size: 8822868 (Uncompressed: 18256180)
Startup Checksum: 0x00000000 (Calculated: 0x00000000)
Decompressing: 100%
Finished.
```

Compress
```
IFSHelper.exe /i test.ifs /c /o compressed.ifs

IFSHelper 0.6 (c) 2016 Remko Weijnen
Input file : test.ifs
Output file: compressed.ifs
Compress   : True
Decompress : False
Compressing: 100%

Finished.
```
