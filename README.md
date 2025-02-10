# LibTar
[![Available on NuGet https://www.nuget.org/packages?q=libtar](https://img.shields.io/nuget/v/libtar.svg?style=flat-square)](https://www.nuget.org/packages?q=libtar)

[libtar](https://www.nuget.org/packages?q=libtar) is a lightweight C# library for extracting TAR archives. It provides a simple API to archive or extract all files from TAR files.

## API
```cs
Tar.ExtractAll(Stream src, string outputDirectory, bool overrideIfExisting); // Extract Tar or Tar.Gz

Tar.Archive(string inputDirectory, Stream dest); // Create Tar Archive.
```

## Demo
```cs
using CN.Lalaki.Archive;
using System.IO;
using System;

// ...sample code

using (var tar = File.Create("path\\of\\output.example.tar")){ // create tar archive.
    Tar.Archive("D:\\temp", tar);
}

using (var tar = File.OpenRead("path\\of\\example.tar")) // tar file extract.
{
    Tar.ExtractAll(tar, "path\\of\\outputDir\\", true);
}

using (var targz = File.OpenRead("path\\of\\example.tar.gz")) // tar.gz file extract
{
    Tar.ExtractAll(targz, "path\\of\\outputDir\\", true);
}

using (var targz = new GzipStream(..., CompressionMode.Decompress)) // tar.gz stream extract
{
    Tar.ExtractAll(targz, "path\\of\\outputDir\\", true);
}
```
## License
[MIT](https://github.com/lalakii/libtar/blob/master/LICENSE)
