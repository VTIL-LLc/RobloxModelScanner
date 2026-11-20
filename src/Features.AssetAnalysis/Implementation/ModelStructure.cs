using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace RobloxModelScanner.Features.AssetAnalysis;

/// <summary>
/// What a model file says about itself: the names its instances carry, and whether the file is a module.
/// </summary>
/// <remarks>
/// <para>
/// A rule that tests text cannot tell an instance's name from a sentence containing it. Both distinctions
/// matter to a rule that hangs on an asset being a module: the word <c>ModuleScript</c> occurs in a car whose
/// boot script mentions one, <c>enter</c> occurs in every interface that asks a reader to press something, and
/// a name at the end of a chunk runs straight into the letters of the chunk header after it. Reading the file
/// instead of scanning it answers both questions from what the file declares.
/// </para>
/// <para>
/// The shape is not a heuristic. An asset published as a module is delivered by Roblox as exactly one
/// instance -- a <c>ModuleScript</c> named <c>MainModule</c>, holding whatever the module builds -- which is
/// why the entry point of every module asset has that name. A model that merely contains a module somewhere
/// deep inside it is not a module asset.
/// </para>
/// <para>
/// Only the chunks needed to answer are decompressed. A chunk is framed by its own length, so the geometry,
/// the mesh data and every property but <c>Name</c> are stepped over without being read.
/// </para>
/// </remarks>
internal sealed class ModelOutline
{
    /// <summary>
    /// Gets a value indicating whether the model's root is a <c>ModuleScript</c> named <c>MainModule</c>.
    /// </summary>
    public bool IsModuleRoot { get; init; }

    /// <summary>
    /// Gets the names every instance in the file carries.
    /// </summary>
    public IReadOnlyList<string> Names { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Reads the outline of a model file.
/// </summary>
internal static class ModelStructure
{
    /// <summary>
    /// The fourteen-byte signature of a binary model.
    /// </summary>
    /// <remarks>
    /// The whole of it, not the first ten bytes: <c>&lt;roblox!</c> is followed by six bytes of which the
    /// last two are a carriage return and a line feed, and a reader that stops early starts reading the
    /// signature's tail as a version.
    /// </remarks>
    private static readonly byte[] Signature =
    [
        0x3C, 0x72, 0x6F, 0x62, 0x6C, 0x6F, 0x78, 0x21, 0x89, 0xFF, 0x0D, 0x0A, 0x1A, 0x0A
    ];

    /// <summary>
    /// The class an asset is published as when it is a module.
    /// </summary>
    private const string ModuleClass = "ModuleScript";

    /// <summary>
    /// The name Roblox's module delivery gives that class.
    /// </summary>
    private const string ModuleName = "MainModule";

    /// <summary>
    /// The property an instance is named by.
    /// </summary>
    private const string NameProperty = "Name";

    /// <summary>
    /// Reads a payload's outline.
    /// </summary>
    /// <param name="payload">The asset bytes, unmodified.</param>
    /// <returns>The outline, or <see langword="null"/> when the payload is not a model this reader knows.</returns>
    public static ModelOutline? Read(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < Signature.Length) return null;

        ReadOnlySpan<byte> head = payload[..Signature.Length];

        if (head.SequenceEqual(Signature)) return ReadBinary(payload);

        // Anything else is either the XML form or not a model at all, and the XML reader answers both by
        // failing to parse it.
        return ReadXml(payload);
    }

    /// <summary>
    /// Reads a binary model's class and instance tables, its name chunks and its parent chunk.
    /// </summary>
    /// <param name="payload">The asset bytes.</param>
    /// <returns>The outline, or <see langword="null"/> when the file does not hold together.</returns>
    private static ModelOutline? ReadBinary(ReadOnlySpan<byte> payload)
    {
        int at = Signature.Length;

        if (at + 18 > payload.Length) return null;

        // The version is a sixteen-bit field and this reader knows version zero. The two counts after it are
        // not used: the chunks state their own sizes, and a header that disagreed with them would not change
        // what can be read.
        if (ReadUInt16(payload, at) != 0) return null;

        at += 2 + 4 + 4 + 8;

        Dictionary<uint, string> classes = [];
        Dictionary<uint, List<int>> instancesByClass = [];
        Dictionary<uint, List<string>> namesByClass = [];
        Dictionary<int, int> parentOf = [];

        while (at + 16 <= payload.Length)
        {
            string name = ChunkName(payload, at);
            int stored = (int)ReadUInt32(payload, at + 4);
            int length = (int)ReadUInt32(payload, at + 8);

            at += 16;

            if (name == "END") break;
            if (stored < 0 || length < 0 || at + stored > payload.Length) return null;

            ReadOnlySpan<byte> body;

            if (stored == 0)
            {
                // A zero in the stored-length field is how the writer marks a chunk it did not compress, and
                // the uncompressed-length field is then the whole of it.
                if (at + length > payload.Length) return null;

                body = payload.Slice(at, length);
                at += length;
            }
            else
            {
                body = payload.Slice(at, stored);
                at += stored;

                byte[]? expanded = Decompress(body, length);

                if (expanded is null) return null;

                body = expanded;
            }

            switch (name)
            {
                case "INST":
                    ReadInstances(body, classes, instancesByClass);
                    break;

                case "PROP":
                    ReadNameProperty(body, instancesByClass, namesByClass);
                    break;

                case "PRNT":
                    ReadParents(body, parentOf);
                    break;
            }
        }

        if (instancesByClass.Count == 0) return null;

        List<string> names = [];
        bool isModuleRoot = false;

        foreach (KeyValuePair<uint, string> entry in classes)
        {
            if (!namesByClass.TryGetValue(entry.Key, out List<string>? classNames)) continue;

            names.AddRange(classNames);

            if (!string.Equals(entry.Value, ModuleClass, StringComparison.Ordinal)) continue;
            if (!instancesByClass.TryGetValue(entry.Key, out List<int>? referents)) continue;

            for (int index = 0; index < referents.Count && index < classNames.Count; index++)
            {
                if (!string.Equals(classNames[index], ModuleName, StringComparison.Ordinal)) continue;

                // A root is an instance the parent chunk does not name, or names as -1. A module buried in a
                // model has a parent, and a module asset does not.
                if (parentOf.TryGetValue(referents[index], out int parent) && parent != -1) continue;

                isModuleRoot = true;
            }
        }

        return new ModelOutline { Names = names, IsModuleRoot = isModuleRoot };
    }

    /// <summary>
    /// Reads one class table entry and the referents of that class's instances.
    /// </summary>
    /// <param name="chunk">The decompressed chunk.</param>
    /// <param name="classes">The class table, filled here.</param>
    /// <param name="instancesByClass">The instance referents per class, filled here.</param>
    private static void ReadInstances(
        ReadOnlySpan<byte> chunk,
        Dictionary<uint, string> classes,
        Dictionary<uint, List<int>> instancesByClass)
    {
        int at = 0;

        if (chunk.Length < 9) return;

        uint classId = ReadUInt32(chunk, ref at);
        string? className = ReadString(chunk, ref at);

        if (className is null || at + 5 > chunk.Length) return;

        byte format = chunk[at];
        at += 1;

        int count = (int)ReadUInt32(chunk, ref at);

        if (count < 0 || at + (count * 4) > chunk.Length) return;

        classes[classId] = className;
        instancesByClass[classId] = ReadReferents(chunk, ref at, count);

        // A service's instances each carry a marker byte, and those bytes are stepped over here because the
        // referents come before them and nothing after this chunk is read from it.
        if (format != 0 && at + count <= chunk.Length) at += count;
    }

    /// <summary>
    /// Reads the <c>Name</c> property chunk, when the chunk being read is one.
    /// </summary>
    /// <param name="chunk">The decompressed chunk.</param>
    /// <param name="instancesByClass">The instance referents per class.</param>
    /// <param name="namesByClass">The names per class, filled here.</param>
    private static void ReadNameProperty(
        ReadOnlySpan<byte> chunk,
        Dictionary<uint, List<int>> instancesByClass,
        Dictionary<uint, List<string>> namesByClass)
    {
        int at = 0;

        if (chunk.Length < 9) return;

        uint classId = ReadUInt32(chunk, ref at);
        string? property = ReadString(chunk, ref at);

        if (property is null || at + 1 > chunk.Length) return;

        byte type = chunk[at];
        at += 1;

        // Type one is a string, which is what a name is stored as. Any other type under this name is a file
        // this reader has no business guessing at.
        if (type != 1 || !string.Equals(property, NameProperty, StringComparison.Ordinal)) return;
        if (!instancesByClass.TryGetValue(classId, out List<int>? referents)) return;

        List<string> names = new List<string>(referents.Count);

        for (int index = 0; index < referents.Count; index++)
        {
            string? value = ReadString(chunk, ref at);

            if (value is null) return;

            names.Add(value);
        }

        namesByClass[classId] = names;
    }

    /// <summary>
    /// Reads the parent chunk.
    /// </summary>
    /// <param name="chunk">The decompressed chunk.</param>
    /// <param name="parentOf">The parent of each child, filled here.</param>
    /// <remarks>
    /// A version byte, a count, and then two arrays of that length: the children in one and their parents in
    /// the other. Both are stored as deltas, so each value is added to the one before it.
    /// </remarks>
    private static void ReadParents(ReadOnlySpan<byte> chunk, Dictionary<int, int> parentOf)
    {
        int at = 0;

        if (chunk.Length < 5) return;

        at += 1;

        int count = (int)ReadUInt32(chunk, ref at);

        if (count < 0 || at + (count * 8) > chunk.Length) return;

        List<int> children = ReadDeltaReferents(chunk, ref at, count);
        List<int> parents = ReadDeltaReferents(chunk, ref at, count);

        for (int index = 0; index < count; index++) parentOf[children[index]] = parents[index];
    }

    /// <summary>
    /// Reads an array of referents that are stored as deltas.
    /// </summary>
    /// <param name="chunk">The chunk.</param>
    /// <param name="at">The position, advanced past the array.</param>
    /// <param name="count">How many referents.</param>
    /// <returns>The referents, accumulated.</returns>
    private static List<int> ReadDeltaReferents(ReadOnlySpan<byte> chunk, ref int at, int count)
    {
        List<int> referents = ReadReferents(chunk, ref at, count);
        int total = 0;

        for (int index = 0; index < referents.Count; index++)
        {
            total += referents[index];
            referents[index] = total;
        }

        return referents;
    }

    /// <summary>
    /// Reads an array of referents.
    /// </summary>
    /// <param name="chunk">The chunk.</param>
    /// <param name="at">The position, advanced past the array.</param>
    /// <param name="count">How many referents.</param>
    /// <returns>The referents, unaccumulated.</returns>
    /// <remarks>
    /// The array is stored by column rather than by row: the first byte of every value, then the second of
    /// every value, and so on. Each value is four big-endian bytes carrying a zigzag-transformed integer,
    /// which is not the byte order the structural fields of a chunk use -- those are little-endian.
    /// </remarks>
    private static List<int> ReadReferents(ReadOnlySpan<byte> chunk, ref int at, int count)
    {
        List<int> referents = new List<int>(count);

        if (count <= 0 || at + (count * 4) > chunk.Length) return referents;

        for (int index = 0; index < count; index++)
        {
            uint raw = ((uint)chunk[at + index] << 24)
                       | ((uint)chunk[at + count + index] << 16)
                       | ((uint)chunk[at + (count * 2) + index] << 8)
                       | chunk[at + (count * 3) + index];

            referents.Add((int)(raw >> 1) ^ -(int)(raw & 1));
        }

        at += count * 4;

        return referents;
    }

    /// <summary>
    /// Reads a length-prefixed UTF-8 string.
    /// </summary>
    /// <param name="chunk">The chunk.</param>
    /// <param name="at">The position, advanced past the string.</param>
    /// <returns>The string, or <see langword="null"/> when the chunk ends inside it.</returns>
    private static string? ReadString(ReadOnlySpan<byte> chunk, ref int at)
    {
        if (at + 4 > chunk.Length) return null;

        int length = (int)ReadUInt32(chunk, ref at);

        if (length < 0 || at + length > chunk.Length) return null;

        string value = System.Text.Encoding.UTF8.GetString(chunk.Slice(at, length));
        at += length;

        return value;
    }

    /// <summary>
    /// Reads four bytes as a chunk name.
    /// </summary>
    /// <param name="payload">The payload.</param>
    /// <param name="at">Where the name starts.</param>
    /// <returns>The name, without a terminator.</returns>
    private static string ChunkName(ReadOnlySpan<byte> payload, int at)
    {
        Span<char> letters = stackalloc char[4];

        for (int index = 0; index < 4; index++)
        {
            byte value = payload[at + index];
            letters[index] = value == 0 ? ' ' : (char)value;
        }

        return new string(letters).TrimEnd();
    }

    /// <summary>
    /// Reads two bytes as a little-endian unsigned integer.
    /// </summary>
    /// <param name="bytes">The bytes.</param>
    /// <param name="at">Where the value starts.</param>
    /// <returns>The value.</returns>
    private static int ReadUInt16(ReadOnlySpan<byte> bytes, int at) => bytes[at] | (bytes[at + 1] << 8);

    /// <summary>
    /// Reads four bytes as a little-endian unsigned integer.
    /// </summary>
    /// <param name="bytes">The bytes.</param>
    /// <param name="at">Where the value starts.</param>
    /// <returns>The value.</returns>
    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int at) =>
        (uint)(bytes[at] | (bytes[at + 1] << 8) | (bytes[at + 2] << 16) | (bytes[at + 3] << 24));

    /// <summary>
    /// Reads four bytes as a little-endian unsigned integer, advancing the position.
    /// </summary>
    /// <param name="bytes">The bytes.</param>
    /// <param name="at">The position, advanced by four.</param>
    /// <returns>The value.</returns>
    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, ref int at)
    {
        if (at + 4 > bytes.Length)
        {
            at = bytes.Length;
            return 0;
        }

        uint value = ReadUInt32(bytes, at);
        at += 4;

        return value;
    }

    /// <summary>
    /// Expands one LZ4 block.
    /// </summary>
    /// <param name="source">The compressed bytes.</param>
    /// <param name="size">The expanded length, which the chunk header states.</param>
    /// <returns>The expanded bytes, or <see langword="null"/> when the block does not describe exactly that.</returns>
    /// <remarks>
    /// LZ4's block format, without a frame around it: a token whose high nibble counts literals and whose low
    /// nibble counts the copy that follows, a two-byte distance back into what has already been written, and a
    /// fifteen in either nibble meaning the count continues in further bytes. Copies may overlap the bytes
    /// being written, which is how the format gets a run from a short distance.
    /// </remarks>
    private static byte[]? Decompress(ReadOnlySpan<byte> source, int size)
    {
        if (size < 0 || size > 256 * 1024 * 1024) return null;

        byte[] target = new byte[size];

        int from = 0;
        int to = 0;

        while (from < source.Length)
        {
            int token = source[from++];

            int literals = token >> 4;

            if (literals == 15)
            {
                int extra;

                do
                {
                    if (from >= source.Length) return null;

                    extra = source[from++];
                    literals += extra;
                }
                while (extra == 255);
            }

            if (from + literals > source.Length || to + literals > size) return null;

            source.Slice(from, literals).CopyTo(target.AsSpan(to));

            from += literals;
            to += literals;

            if (from >= source.Length) break;
            if (from + 2 > source.Length) return null;

            int distance = source[from] | (source[from + 1] << 8);
            from += 2;

            if (distance == 0 || distance > to) return null;

            int match = token & 0x0F;

            if (match == 15)
            {
                int extra;

                do
                {
                    if (from >= source.Length) return null;

                    extra = source[from++];
                    match += extra;
                }
                while (extra == 255);
            }

            match += 4;

            if (to + match > size) return null;

            int read = to - distance;

            for (int index = 0; index < match; index++) target[to++] = target[read++];
        }

        return to == size ? target : null;
    }

    /// <summary>
    /// Reads the XML form of a model.
    /// </summary>
    /// <param name="payload">The asset bytes.</param>
    /// <returns>The outline, or <see langword="null"/> when the payload is not an XML model.</returns>
    private static ModelOutline? ReadXml(ReadOnlySpan<byte> payload)
    {
        try
        {
            using MemoryStream stream = new MemoryStream(payload.ToArray(), false);

            XDocument document = XDocument.Load(stream);
            XElement? root = document.Root;

            if (root is null) return null;

            List<string> names = [];
            bool isModuleRoot = false;

            foreach (XElement item in root.Descendants("Item"))
            {
                string? name = ReadXmlName(item);

                if (name is not null) names.Add(name);
            }

            foreach (XElement item in root.Elements("Item"))
            {
                if ((string?)item.Attribute("class") != ModuleClass) continue;

                // Only the root's own items: a module inside a model is not a module asset.
                if (!string.Equals(ReadXmlName(item), ModuleName, StringComparison.Ordinal)) continue;

                isModuleRoot = true;
            }

            return new ModelOutline { Names = names, IsModuleRoot = isModuleRoot };
        }
        catch (Exception error) when (error is System.Xml.XmlException or ArgumentException or NotSupportedException)
        {
            // Not markup this reader can use, which is the same answer as not being a model.
            return null;
        }
    }

    /// <summary>
    /// Reads the name of one XML instance.
    /// </summary>
    /// <param name="item">The element.</param>
    /// <returns>The name, or <see langword="null"/> when it carries none.</returns>
    private static string? ReadXmlName(XElement item)
    {
        XElement? properties = item.Element("Properties");

        if (properties is null) return null;

        foreach (XElement property in properties.Elements("string"))
        {
            if ((string?)property.Attribute("name") == NameProperty) return property.Value.Trim();
        }

        return null;
    }
}
