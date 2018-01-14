namespace FileScan
{
    using Newtonsoft.Json;
    using OpenSSL.Crypto;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;

    class Program
    {
        private const string LongPrefix = @"\\?\";

        private const string FileScanJson = "FileScan.json";

        private static string AddPrefix(string input)
        {
            if (input.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase))
            {
                return input;
            }

            return LongPrefix + input;
        }

        private static string RemovePrefix(string input)
        {
            if (input.StartsWith(@LongPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return input.Substring(LongPrefix.Length);
            }

            return input;
        }

        static async Task Main(string[] args)
        {
            if (args[0] == "-check" || args[0] == "/check")
            {
                Check(args.Skip(1));
                return;
            }

            foreach (var arg in args)
            {
                var fileScanJson = AddPrefix(Path.Combine(arg, FileScanJson));
                var serialised = new List<Entry>();
                var directoryEntries = new Queue<string>();
                var fileEntries = new List<string>();
                directoryEntries.Enqueue(AddPrefix(arg));
                var sw = new Stopwatch();
                sw.Start();
                long last = 0;
                long lastWrite = 0;
                long lastBytes = 0;
                long bytes = 0;
                long processed = 0;

                void WriteJson()
                {
                    File.WriteAllText(fileScanJson, JsonConvert.SerializeObject(serialised, Formatting.Indented));
                }

                void UpdateTitle(string current)
                {
                    var now = sw.ElapsedMilliseconds;
                    var elapsed = now - last;
                    if (elapsed > 1000)
                    {
                        var bps = (double)(bytes - lastBytes) * 1000 / elapsed;
                        Console.WriteLine($@"{GetBytesReadable((long)bps)}/s {GetBytesReadable(bytes)} {processed}/{fileEntries.Count - processed}/{directoryEntries.Count} {RemovePrefix(current)}");
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

                var existingFiles = new Dictionary<string, Entry>(StringComparer.InvariantCultureIgnoreCase);

                if (File.Exists(fileScanJson))
                {
                    existingFiles = JsonConvert.DeserializeObject<Entry[]>(File.ReadAllText(fileScanJson)).ToDictionary(item => item.Name, item => item);
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
                                if (!string.Equals(fileScanJson, file, StringComparison.InvariantCultureIgnoreCase))
                                {
                                    fileEntries.Add(file);
                                }
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

                fileEntries.Sort(StringComparer.InvariantCultureIgnoreCase);

                var buf = new byte[65536];
                var buf2 = new byte[65536];

                var hashTask = Task.FromResult(buf2);

                foreach (var name in fileEntries)
                {
                    UpdateTitle(name);

                    FileInfo fileInfo;
                    try
                    {
                        fileInfo = new FileInfo(name);
                    }
                    catch (IOException)
                    {
                        continue;
                    }
                    string hashStr = null;
                    var storedName = RemovePrefix(name);

                    if (existingFiles.TryGetValue(storedName, out var entry) && entry.CreationTimeUtc == fileInfo.CreationTimeUtc && entry.ModificationTimeUtc == fileInfo.LastWriteTimeUtc && entry.Length == fileInfo.Length)
                    {
                        hashStr = entry.Sha512;
                    }

                    if (hashStr == null)
                    {
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

                                var hash = ctx.DigestFinal();
                                hashStr = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
                            }
                        }
                    }

                    serialised.Add(new Entry
                    {
                        Name = storedName,
                        CreationTimeUtc = fileInfo.CreationTimeUtc,
                        ModificationTimeUtc = fileInfo.LastWriteTimeUtc,
                        Length = fileInfo.Length,
                        Sha512 = hashStr
                    });
                    ++processed;
                }

                WriteJson();
            }
        }

        private static void Check(IEnumerable<string> args)
        {
            var dict = new Dictionary<string, List<Entry>>(StringComparer.OrdinalIgnoreCase);

            foreach (var arg in args)
            {
                var data = JsonConvert.DeserializeObject<Entry[]>(File.ReadAllText(AddPrefix(Path.Combine(arg, FileScanJson))));

                foreach (var entry in data)
                {
                    if (!dict.TryGetValue(entry.Sha512, out var entries))
                    {
                        entries = new List<Entry>();
                        dict.Add(entry.Sha512, entries);
                    }

                    entries.Add(entry);
                }
            }

            var duplicates = dict.Values.Where(v => v.Count > 1).ToList();

            duplicates.Sort((lhs, rhs) => rhs.Sum(i => i.Length).CompareTo(lhs.Sum(i => i.Length)));

            File.WriteAllText("Duplicates.json", JsonConvert.SerializeObject(duplicates, Formatting.Indented));
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
            return readable.ToString("0.### ", CultureInfo.InvariantCulture) + suffix;
        }
    }
}
