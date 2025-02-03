using System.IO.Compression;
using static System.Text.Encoding;
using Convert = System.Convert;

namespace CN.Lalaki.Archive
{
    public static class Tar
    {
        private const long BlockSize = 512L;
        private const int BufSize = 40960;
        private static readonly byte[] Magic = ASCII.GetBytes("ustar\000");
        private static readonly byte[] Permission = ASCII.GetBytes("0000755\0");

        private enum TypeFlag
        {
            AREGTYPE = '\0',
            REGTYPE = '0',
            LNKTYPE = '1',
            SYMTYPE = '2',
            CHRTYPE = '3',
            BLKTYPE = '4',
            DIRTYPE = '5',
            FIFOTYPE = '6',
            CONTTYPE = '7',
            XGLTYPE = 'g',
            XHDTYPE = 'x',
        }

        public static void Archive(string inputDir, Stream ts)
        {
            if (!Directory.Exists(inputDir))
            {
                throw new IOException("Input directory cannot be null or empty.");
            }

            if (ts?.CanWrite != true)
            {
                throw new IOException("Unable to write stream.");
            }

            string[] dirs = Directory.GetFileSystemEntries(inputDir, "*", SearchOption.AllDirectories);
            string? fullPath = $"{Path.GetDirectoryName(inputDir)}";
            byte[] data = new byte[BlockSize];
            foreach (string child in dirs)
            {
                for (int i = 0; i < BlockSize; i++)
                {
                    data[i] = 0;
                }

                int start = child.IndexOf(fullPath, StringComparison.OrdinalIgnoreCase);
                if (start == -1)
                {
                    continue;
                }

                string path = child.Remove(start, fullPath.Length).Replace('\\', '/').TrimStart('/');
                FileAttributes attrs = File.GetAttributes(child);
                if (attrs.HasFlag(FileAttributes.ReparsePoint))
                {
                    data[156] = (int)TypeFlag.SYMTYPE;
                }
                else if (attrs.HasFlag(FileAttributes.Directory))
                {
                    data[156] = (int)TypeFlag.DIRTYPE;
                    path += "/";
                }
                else
                {
                    data[156] = (int)TypeFlag.REGTYPE;
                }

                byte[] pathBytes = UTF8.GetBytes(path);
                if (pathBytes.Length > 100)
                {
                    int index = Array.FindLastIndex(pathBytes, 100, 100, (s) => s == '/');
                    if (index != -1)
                    {
                        if (index == pathBytes.Length)
                        {
                            data[0] = (int)'/';
                        }
                        else
                        {
                            Buffer.BlockCopy(pathBytes, 0, data, 0, index + 1);
                        }

                        Buffer.BlockCopy(pathBytes, 0, data, 345, index);
                    }
                }
                else
                {
                    pathBytes.CopyTo(data, 0);
                }

                Buffer.BlockCopy(Permission, 0, data, 100, Permission.Length);
                ASCII.GetBytes(Convert.ToString(((DateTimeOffset)File.GetLastWriteTime(child)).ToUnixTimeSeconds(), 8)).CopyTo(data, 136);
                FileStream? fs = null;
                if (data[156] == (int)TypeFlag.REGTYPE)
                {
                    fs = File.OpenRead(child);
                    ASCII.GetBytes($"{Convert.ToString(fs.Length, 8).PadLeft(11, '0')}\0").CopyTo(data, 124);
                }

                Buffer.BlockCopy(Magic, 0, data, 257, Magic.Length);
                uint cksum = 256U;
                for (uint i = 0U; i < 148U; i++)
                {
                    cksum += data[i];
                }

                for (uint i = 156U; i < 500U; i++)
                {
                    cksum += data[i];
                }

                ASCII.GetBytes($"{Convert.ToString(cksum, 8).PadLeft(6, '0')}\0 ").CopyTo(data, 148);
                ts.Write(data, 0, data.Length);

                if (fs != null)
                {
                    fs.CopyTo(ts);
                    fs.Dispose();
                    long end = (BlockSize - ts.Position) & 511L;
                    for (uint i = 0U; i < end; i++)
                    {
                        ts.WriteByte(0);
                    }
                }
            }

            ts.SetLength(ts.Position + BlockSize);
            ts.Dispose();
        }

        public static void ExtractAll(Stream ts, string outDir, bool mOverride)
        {
            if (ts?.CanRead != true)
            {
                throw new IOException("Unable to read stream.");
            }

            if (string.IsNullOrWhiteSpace(outDir))
            {
                throw new IOException("Output directory cannot be null or empty.");
            }

            Stream? fs = null;
            GZipStream? gz = null;
            if (ts is GZipStream gz0)
            {
                gz = gz0;
            }
            else
            {
                long pos = ts.Position;
                int b0 = ts.ReadByte();
                int b1 = ts.ReadByte();
                if (b0 == 0x1F && b1 == 0x8B)
                {
                    gz = new(ts, CompressionMode.Decompress);
                }

                ts.Position = pos;
            }

            if (gz != null)
            {
                using (gz)
                {
                    ts = fs = File.Create(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
                    gz.CopyTo(fs);
                    ts.Position = 0L;
                }
            }

            if (!Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir).Refresh();
            }

            byte[] buf = new byte[BufSize];
            while (true)
            {
                int bytesRead = ts.Read(buf, 0, 100);
                if (bytesRead != 100)
                {
                    break;
                }

                string fileName = UTF8.GetString(buf, 0, 100).TrimEnd('\0');
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    break;
                }

                bytesRead = ts.Read(buf, 100, 400);
                if (bytesRead != 400)
                {
                    break;
                }

                uint cksum = Convert.ToUInt32(ASCII.GetString(buf, 148, 8).Trim().TrimEnd('\0'), 8);
                uint vcksum = 256U;
                for (uint i = 0U; i < 148U; i++)
                {
                    vcksum += buf[i];
                }

                for (uint i = 156U; i < 500U; i++)
                {
                    vcksum += buf[i];
                }

                if (cksum != vcksum)
                {
                    ReleaseStreams(ts, fs);
                    throw new IOException($"Checksum mismatch.(c0:{cksum} c1:{vcksum})");
                }

                ts.Position += 12L;
                string size = ASCII.GetString(buf, 124, 12).TrimEnd('\0');
                long fileSize = 0L;
                if (size.Length > 0)
                {
                    fileSize = Convert.ToInt64(size, 8);
                }

                TypeFlag type = (TypeFlag)buf[156];
                string prefix = UTF8.GetString(buf, 345, 155).TrimEnd('\0');
                if (!string.IsNullOrWhiteSpace(prefix))
                {
                    fileName = Path.Combine(prefix, fileName);
                }

                string output = Path.Combine(outDir, fileName);
                switch (type)
                {
                    case TypeFlag.REGTYPE:
                    case TypeFlag.AREGTYPE:
                        if (!File.Exists(output) || mOverride)
                        {
                            string? mDir = Path.GetDirectoryName(output);
                            if (mDir != null && !Directory.Exists(mDir))
                            {
                                Directory.CreateDirectory(mDir).Refresh();
                            }

                            using FileStream os = new(output, FileMode.Create, FileAccess.Write);
                            long total = 0;
                            int endOp = (int)(fileSize % BufSize);
                            int pos = BufSize;
                            while (total < fileSize)
                            {
                                if (total + pos > fileSize)
                                {
                                    pos = endOp;
                                }

                                bytesRead = ts.Read(buf, 0, pos);
                                os.Write(buf, 0, bytesRead);
                                total += bytesRead;
                            }

                            if (total != fileSize)
                            {
                                ReleaseStreams(ts, fs);
                                throw new IOException("File length mismatch.");
                            }
                        }
                        else
                        {
                            ts.Position += fileSize;
                        }

                        break;

                    case TypeFlag.SYMTYPE:
                        break;

                    case TypeFlag.DIRTYPE:
                        string? dir = Path.GetDirectoryName(output);
                        if (dir != null && !Directory.Exists(dir))
                        {
                            Directory.CreateDirectory(dir).Refresh();
                        }

                        break;

                    case TypeFlag.XGLTYPE:
                        ts.Position += fileSize;
                        break;

                    case TypeFlag.LNKTYPE:
                        break;

                    case TypeFlag.CHRTYPE:
                        break;

                    case TypeFlag.BLKTYPE:
                        break;

                    case TypeFlag.FIFOTYPE:
                        break;

                    case TypeFlag.CONTTYPE:
                        break;

                    case TypeFlag.XHDTYPE:
                        break;

                    default:
                        ts.Position += fileSize;
                        break;
                }

                ts.Position += (BlockSize - ts.Position) & 511L;
            }

            ReleaseStreams(ts, fs);
        }

        private static void ReleaseStreams(Stream ts, Stream? fs)
        {
            if (ts == fs)
            {
                fs.SetLength(0L);
            }

            ts.Dispose();
        }
    }
}