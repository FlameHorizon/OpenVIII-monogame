﻿using K4os.Compression.LZ4;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenVIII
{
    public class ArchiveMap : IReadOnlyDictionary<string, FI>
    {
        #region Fields

        private readonly Dictionary<string, FI> _entries;

        #endregion Fields

        #region Constructors

        public ArchiveMap(StreamWithRangeValues fI, StreamWithRangeValues fL, int max)
        {
            if (fI == null || fL == null)
                throw new ArgumentNullException($"{nameof(ArchiveMap)}::{nameof(ArchiveMap)} {nameof(fI)} or {nameof(fL)} is null");
            Max = max;
            var s1 = Uncompress(fL, out var flOffset);
            var s2 = Uncompress(fI, out var fiOffset);
            var fiSize = fI.UncompressedSize == 0 ? fI.Size : fI.UncompressedSize;

            using (var sr = new StreamReader(s1, System.Text.Encoding.UTF8))
            using (var br = new BinaryReader(s2))
            {
                s1.Seek(flOffset, SeekOrigin.Begin);
                s2.Seek(fiOffset, SeekOrigin.Begin);
                _entries = new Dictionary<string, FI>();
                var count = fiSize / 12;
                while (count-- > 0)
                    _entries.Add(sr.ReadLine()?.TrimEnd() ?? throw new InvalidOperationException(), Extended.ByteArrayToClass<FI>(br.ReadBytes(12)));
            }
            CorrectCompressionForLzsFiles();
        }

        public ArchiveMap(int count) => _entries = new Dictionary<string, FI>(count);

        #endregion Constructors

        #region Properties

        public int Count => ((IReadOnlyDictionary<string, FI>)_entries).Count;

        public IEnumerable<string> Keys => ((IReadOnlyDictionary<string, FI>)FilteredEntries).Keys;

        public int Max { get; }

        public string ArchiveLangFolder
        {
            get;
        } = $"lang-{Memory.Languages}";

        public IReadOnlyList<KeyValuePair<string, FI>> OrderedByName => FilteredEntries.OrderBy(x => x.Key).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToList();

        public IReadOnlyList<KeyValuePair<string, FI>> OrderedByOffset => FilteredEntries.OrderBy(x => x.Value.Offset).ThenBy(x => x.Key).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToList();

        public IEnumerable<FI> Values => ((IReadOnlyDictionary<string, FI>)FilteredEntries).Values;

        private IEnumerable<KeyValuePair<string, FI>> FilteredEntries => _entries.Where(x => !string.IsNullOrWhiteSpace(x.Key) && x.Value.UncompressedSize > 0);

        #endregion Properties

        #region Indexers

        public FI this[string key] => ((IReadOnlyDictionary<string, FI>)_entries)[key];

        #endregion Indexers

        #region Methods

        public static byte[] Lz4Uncompress(byte[] input, int fsUncompressedSize, int offset = 12)
        {
            Memory.Log.WriteLine($"{nameof(ArchiveMap)}::{nameof(Lz4Uncompress)} :: decompressing data");
            //ReadOnlySpan<byte> input = new ReadOnlySpan<byte>(file);
            var output = new byte[fsUncompressedSize];
            //Span<byte> output = new Span<byte>(r);
            while (input.Length - offset > 0 && LZ4Codec.Decode(input, offset, input.Length - offset, output, 0, output.Length) <= -1)
            {
                offset++;
            }
            if (offset > -1)
            {
                Memory.Log.WriteLine($"{nameof(ArchiveWorker)}::{nameof(Lz4Uncompress)}::{nameof(offset)}: {offset}");
                return output;
            }
            else
                throw new Exception($"{nameof(ArchiveWorker)}::{nameof(Lz4Uncompress)} Failed to uncompress...");
        }

        public void Add(KeyValuePair<string, FI> keyValuePair) => _entries.Add(keyValuePair.Key, keyValuePair.Value);

        public bool ContainsKey(string key) => ((IReadOnlyDictionary<string, FI>)_entries).ContainsKey(key);

        public FI FindString(ref string input, out int size)
        {
            var localInput = input;

            var result = GetSpecificLangFileData(input);

            if (string.IsNullOrWhiteSpace(result.Key))
            {
                result = OrderedByName.FirstOrDefault(x => x.Key.IndexOf(localInput, StringComparison.OrdinalIgnoreCase) > -1);
            }

            if (string.IsNullOrWhiteSpace(result.Key) || result.Value == default)
            {
                size = 0;
                return null;
            }
            var result2 = OrderedByOffset.FirstOrDefault(x => x.Value.Offset > result.Value.Offset);
            if (result2.Value == default)
            {
                switch (result.Value.CompressionType)
                {
                    case CompressionType.None:
                    case CompressionType.LZSS_UnknownSize:
                        size = result.Value.UncompressedSize;
                        break;

                    default:
                        size = Max - result.Value.Offset;
                        if (size < 0) size = 0; // could be problems.
                        break;
                }
            }
            else
                size = result2.Value.Offset - result.Value.Offset;
            input = result.Key;
            return result.Value;
        }

        public byte[] GetBinaryFile(FI fi, Stream data, string input, int size, long offset = 0)
        {
            var max = data.Length;
            if (data is StreamWithRangeValues s)
            {
                max = s.Max;
            }
            if (fi == null)
            {
                Memory.Log.WriteLine($"{nameof(ArchiveMap)}::{nameof(GetBinaryFile)} failed to extract {input}");
                return null;
            }
            else
                Memory.Log.WriteLine($"{nameof(ArchiveMap)}::{nameof(GetBinaryFile)} extracting {input}");
            if (size == 0)
                size = checked((int)(max - (fi.Offset + offset)));
            byte[] buffer;
            using (var br = new BinaryReader(data))
            {
                br.BaseStream.Seek(fi.Offset + offset, SeekOrigin.Begin);
                switch (fi.CompressionType)
                {
                    case CompressionType.LZSS:
                    case CompressionType.LZSS_UnknownSize:
                    case CompressionType.LZSS_LZSS:
                        var readSize = br.ReadInt32();
                        if (size != readSize + sizeof(int))
                            throw new InvalidDataException($"{nameof(ArchiveMap)}::{nameof(GetBinaryFile)} Size inside of lzs {{{readSize}}} doesn't match size calculated by region {{{size}}}.");
                        size = readSize;
                        break;
                }
                buffer = br.ReadBytes(size);
            }
            switch (fi.CompressionType)
            {
                case 0:
                    return buffer;

                case CompressionType.LZSS:
                    return LZSS.DecompressAllNew(buffer, fi.UncompressedSize);

                case CompressionType.LZSS_LZSS:
                    return LZSS.DecompressAllNew(LZSS.DecompressAllNew(buffer, fi.UncompressedSize), 0, true);

                case CompressionType.LZSS_UnknownSize:
                    return LZSS.DecompressAllNew(buffer, 0);

                case CompressionType.LZ4:
                    return Lz4Uncompress(buffer, fi.UncompressedSize);

                default:
                    throw new InvalidDataException($"{nameof(fi.CompressionType)}: {fi.CompressionType} is invalid...");
            }
        }

        public byte[] GetBinaryFile(string input, Stream data)
        {
            if (data == null)
                throw new ArgumentNullException($"{nameof(ArchiveMap)}::{nameof(GetBinaryFile)} {nameof(data)} cannot be null.");
            if (string.IsNullOrWhiteSpace(input))
                throw new ArgumentNullException($"{nameof(ArchiveMap)}::{nameof(GetBinaryFile)} {nameof(input)} cannot be empty or null");
            long offset = 0;
            if (data.GetType() == typeof(StreamWithRangeValues))
            {
                var s = (StreamWithRangeValues)data;

                data = Uncompress(s, out offset);
                //do I need to do something here? :P
            }
            var fi = FindString(ref input, out var size);
            return GetBinaryFile(fi, data, input, size, offset);
        }

        public IEnumerator<KeyValuePair<string, FI>> GetEnumerator() => ((IReadOnlyDictionary<string, FI>)_entries).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => ((IReadOnlyDictionary<string, FI>)_entries).GetEnumerator();

        public KeyValuePair<string, FI> GetFileData(string fileName)
        {
            if (!TryGetValue(fileName, out var value))
                return OrderedByName.FirstOrDefault(x => x.Key.IndexOf(fileName, StringComparison.OrdinalIgnoreCase) >= 0);
            return new KeyValuePair<string, FI>(fileName, value);
        }

        /// <summary>
        /// Merge two file lists together.
        /// </summary>
        /// <param name="child">the map being merged.</param>
        /// <param name="offsetForFs">offset where the file data is.</param>
        public void MergeMaps(ArchiveMap child, int offsetForFs) => _entries.AddRange(child._entries.ToDictionary(x => x.Key, x => x.Value.Adjust(offsetForFs)));

        public bool TryGetValue(string key, out FI value) => ((IReadOnlyDictionary<string, FI>)_entries).TryGetValue(key, out value);

        /// <summary>
        /// This forces the compression to be set to lzss so the file comes out uncompressed.
        /// </summary>
        private void CorrectCompressionForLzsFiles()
        {
            var lszFiles = _entries.Where(x => x.Key.EndsWith("lzs", StringComparison.OrdinalIgnoreCase));
            // ReSharper disable once PossibleMultipleEnumeration
            lszFiles.Where(x => x.Value.CompressionType == CompressionType.None).ForEach(x => _entries[x.Key].CompressionType = CompressionType.LZSS_UnknownSize);
            // ReSharper disable once PossibleMultipleEnumeration
            lszFiles.Where(x => x.Value.CompressionType == CompressionType.LZSS).ForEach(x => _entries[x.Key].CompressionType = CompressionType.LZSS_LZSS);
        }

        private KeyValuePair<string, FI> GetSpecificLangFileData(string input)
        {
            return OrderedByName.FirstOrDefault(x =>
            {
                return x.Key.IndexOf(ArchiveLangFolder, StringComparison.OrdinalIgnoreCase) > 1 && x.Key.IndexOf(input, StringComparison.OrdinalIgnoreCase) > -1;
            });
        }

        private Stream Uncompress(StreamWithRangeValues @in, out long offset)
        {
            offset = 0;
            if (@in == null) return null;
            byte[] buffer;
            byte[] open(int skip = 0)
            {
                @in.Seek(@in.Offset + skip, SeekOrigin.Begin);
                using (var br = new BinaryReader(@in))
                    return br.ReadBytes(checked((int)(@in.Size - skip)));
            }
            if (@in.Compression > 0)
                switch (@in.Compression)
                {
                    case CompressionType.LZSS:
                        buffer = open();
                        var compressedSize = BitConverter.ToInt32(buffer, 0);
                        if (compressedSize != buffer.Length - sizeof(int))
                            throw new InvalidDataException($"{nameof(ArchiveMap)}::{nameof(Uncompress)} buffer size incorrect ({compressedSize}) != ({buffer.Length - sizeof(int)})");
                        return new MemoryStream(LZSS.DecompressAllNew(buffer, @in.UncompressedSize, true));

                    case CompressionType.LZ4:
                        buffer = open();
                        return new MemoryStream(Lz4Uncompress(buffer, @in.UncompressedSize));
                }
            offset = @in.Offset;
            return @in;
        }

        #endregion Methods
    }
}