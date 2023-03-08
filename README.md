# ifshelper

Tool to unpack VW / VAG MIB QNX files, includes functionality to split joined IFS, remerge them, decompress and compress

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
