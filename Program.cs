using System.Linq;
using System.Threading.Tasks;
using OpenSSL.Crypto;

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

        static async Task Main(string[] args)
        {
            foreach (var arg in args)
            {
                var serialised = new List<Entry>();
                var directoryEntries = new Queue<string>();
                var fileEntries = new List<string>();
                directoryEntries.Enqueue(LongPrefix + arg);
                var sw = new Stopwatch();
                sw.Start();
                long last = 0;
                long lastWrite = 0;
                long lastBytes = 0;
                long bytes = 0;
                long processed = 0;

                void WriteJson()
                {
                    File.WriteAllText(Path.Combine(arg, "FileScan.json"), JsonConvert.SerializeObject(serialised, Formatting.Indented));
                }

                void UpdateTitle(string current)
                {
                    var now = sw.ElapsedMilliseconds;
                    var elapsed = now - last;
                    if (elapsed > 1000)
                    {
                        var bps = (double)(bytes - lastBytes) * 1000 / elapsed;
                        Console.WriteLine($@"{GetBytesReadable((long)bps)}/s {GetBytesReadable(bytes)} {processed}/{fileEntries.Count - processed}/{directoryEntries.Count} {current.Substring(LongPrefix.Length)}");
                        last = now;
                        lastBytes = bytes;
                    }

                    var elapsedWrite = now - lastWrite;
                    if (elapsedWrite > 3600000)
                    {
                        WriteJson();
                        lastWrite = now;
                    }
                }

                while (directoryEntries.Count > 0)
                {
                    var name = directoryEntries.Dequeue();
                    UpdateTitle(name);
                    try
                    {
                        foreach (var file in Directory.GetFileSystemEntries(name))
                        {
                            if (File.Exists(file))
                            {
                                fileEntries.Add(file);
                            }
                            else
                            {
                                directoryEntries.Enqueue(file);
                            }
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }

                fileEntries.Sort();

                var buf = new byte[65536];
                var buf2 = new byte[65536];

                Task<byte[]> hashTask = Task.FromResult(buf2);
                var lockObject = new object();

                foreach (var name in fileEntries)
                {
                    UpdateTitle(name);

                    Stream fi;

                    try
                    {
                        fi = File.OpenRead(name);
                    }
                    catch (IOException)
                    {
                        continue;
                    }
                    using (fi)
                    {
                        byte[] hash = null;
                        using (var ctx = new MessageDigestContext(MessageDigest.SHA512))
                        {
                            ctx.Init();
                            var currentBuf = buf;

                            while (true)
                            {
                                var localRead = fi.Read(currentBuf, 0, currentBuf.Length);
                                var nextBuf = await hashTask;
                                if (localRead == 0)
                                {
                                    break;
                                }

                                var localBuf = currentBuf;
                                currentBuf = nextBuf;
                                hashTask = Task.Factory.StartNew(() =>
                                {
                                    ctx.Update(localRead == localBuf.Length ? localBuf : localBuf.Take(localRead).ToArray());
                                    bytes += localRead;
                                    UpdateTitle(name);
                                    return localBuf;
                                });
                            }

                            hash = ctx.DigestFinal();
                        }

                        var fileInfo = new FileInfo(name);
                        serialised.Add(new Entry
                        {
                            Name = name.Substring(LongPrefix.Length),
                            CreationTimeUtc = fileInfo.CreationTimeUtc,
                            ModificationTimeUtc = fileInfo.LastWriteTimeUtc,
                            Length = fileInfo.Length,
                            Sha512 = BitConverter.ToString(hash).Replace("-", string.Empty)
                        });
                        ++processed;
                    }
                }

                WriteJson();
            }
        }

        public class Entry
        {
            public string Name { get; set; }

            public long Length { get; set; }

            public DateTime CreationTimeUtc { get; set; }

            public DateTime ModificationTimeUtc { get; set; }


            public string Sha512 { get; set; }
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
