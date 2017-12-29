namespace FileScan
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Security.Cryptography;
    using Newtonsoft.Json;

    class Program
    {
        private const string LongPrefix = @"\\?\";

        static void Main(string[] args)
        {
            foreach (var arg in args)
            {
                var serialised = new List<Entry>();
                var entries = new Queue<string>();
                entries.Enqueue(LongPrefix + arg);
                var sw = new Stopwatch();
                sw.Start();
                long last = 0;
                long lastBytes = 0;
                long bytes = 0;
                long processed = 0;

                void UpdateTitle(string current)
                {
                    var now = sw.ElapsedMilliseconds;
                    var elapsed = now - last;
                    if (elapsed > 1000)
                    {
                        var bps = (double)(bytes - lastBytes) * 1000 / elapsed;
                        Console.WriteLine($@"{GetBytesReadable((long)bps)}/s {GetBytesReadable(bytes)} {processed}/{entries.Count} {current.Substring(LongPrefix.Length)}");
                        last = now;
                        lastBytes = bytes;
                    }
                }

                while (entries.Count > 0)
                {
                    var name = entries.Dequeue();
                    UpdateTitle(name);
                    if (File.Exists(name))
                    {
                        using (var fi = File.OpenRead(name))
                        {
                            using (var progress = new ProgressStream(fi, localBytes =>
                            {
                                bytes += localBytes;
                                UpdateTitle(name);
                            }))
                            {
                                var hash = new SHA256Managed().ComputeHash(progress);
                                var fileInfo = new FileInfo(name);
                                serialised.Add(new Entry
                                {
                                    Name = name.Substring(LongPrefix.Length),
                                    CreationTimeUtc = fileInfo.CreationTimeUtc,
                                    ModificationTimeUtc = fileInfo.LastWriteTimeUtc,
                                    Length = fileInfo.Length,
                                    Sha256 = Convert.ToBase64String(hash)
                                });
                                ++processed;
                            }
                        }
                    }
                    else
                    {
                        try
                        {
                            foreach (var file in Directory.GetFileSystemEntries(name))
                            {
                                entries.Enqueue(file);
                            }
                        }
                        catch (UnauthorizedAccessException)
                        {
                        }
                    }
                }

                File.WriteAllText(Path.Combine(arg, "FileScan.json"), JsonConvert.SerializeObject(serialised, Formatting.Indented));
            }
        }

        public class Entry
        {
            public string Name { get; set; }

            public long Length { get; set; }

            public DateTime CreationTimeUtc { get; set; }

            public DateTime ModificationTimeUtc { get; set; }


            public string Sha256 { get; set; }
        }

        public class ProgressStream : Stream
        {
            private readonly Stream underlyingStream;

            private readonly Action<int> progress;

            public ProgressStream(Stream underlyingStream, Action<int> progress)
            {
                this.underlyingStream = underlyingStream;
                this.progress = progress;
            }

            public override void Flush()
            {
                throw new NotImplementedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotImplementedException();
            }

            public override void SetLength(long value)
            {
                throw new NotImplementedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                var result = this.underlyingStream.Read(buffer, offset, count);
                this.progress(result);
                return result;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            public override bool CanRead { get; }

            public override bool CanSeek { get; }

            public override bool CanWrite { get; }

            public override long Length { get; }

            public override long Position { get; set; }
        }

        // http://www.somacon.com/p576.php
        public static string GetBytesReadable(long i)
        {
            // Get absolute value
            long absolute_i = (i < 0 ? -i : i);
            // Determine the suffix and readable value
            string suffix;
            double readable;
            if (absolute_i >= 0x1000000000000000) // Exabyte
            {
                suffix = "EB";
                readable = (i >> 50);
            }
            else if (absolute_i >= 0x4000000000000) // Petabyte
            {
                suffix = "PB";
                readable = (i >> 40);
            }
            else if (absolute_i >= 0x10000000000) // Terabyte
            {
                suffix = "TB";
                readable = (i >> 30);
            }
            else if (absolute_i >= 0x40000000) // Gigabyte
            {
                suffix = "GB";
                readable = (i >> 20);
            }
            else if (absolute_i >= 0x100000) // Megabyte
            {
                suffix = "MB";
                readable = (i >> 10);
            }
            else if (absolute_i >= 0x400) // Kilobyte
            {
                suffix = "KB";
                readable = i;
            }
            else
            {
                return i.ToString("0 B"); // Byte
            }
            // Divide by 1024 to get fractional value
            readable = (readable / 1024);
            // Return formatted number with suffix
            return readable.ToString("0.### ") + suffix;
        }
    }
}
