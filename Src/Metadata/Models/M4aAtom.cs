using System.Text;
using SoundFlow.Metadata.Utilities;

namespace SoundFlow.Metadata.Models;

/// <summary>
/// Represents a generic M4A/MP4 atom (or "box") for parsing and rebuilding.
/// This version is designed to work with an in-memory stream of a single parent atom (e.g., 'moov').
/// </summary>
internal class M4aAtom(string type)
{
    public string Type { get; } = type;
    public List<M4aAtom> Children { get; } = [];
    public byte[]? Data { get; set; }

    /// <summary>
    /// Recursively parses a sequence of sibling atoms from a stream until the end position is reached.
    /// </summary>
    public static List<M4aAtom> ParseAtoms(BigEndianBinaryReader reader, long endPosition, M4aAtom? parent = null)
    {
        var atoms = new List<M4aAtom>();
        var stream = reader.BaseStream;

        var knownContainers = new HashSet<string> { "moov", "udta", "meta", "ilst", "trak", "mdia", "minf", "stbl" };

        while (stream.Position < endPosition)
        {
            var atomStart = stream.Position;
            if (endPosition - atomStart < 8) break; // Not enough space for a header

            var size = reader.ReadUInt32();
            var type = reader.ReadFixedString(4);
            long headerSize = 8;
            
            if (size == 1) // 64-bit size
            {
                if (endPosition - atomStart < 16) break;
                size = (uint)reader.ReadInt64();
                headerSize = 16;
            }
            
            if (size == 0) size = (uint)(endPosition - atomStart);

            if (size < headerSize) break; // Invalid atom
            
            var dataSize = size - headerSize;
            var atom = new M4aAtom(type);
            
            // An atom is a parent if it's a known container OR if its direct parent is 'ilst'.
            var isParentAtom = knownContainers.Contains(type) || parent?.Type == "ilst";
            
            if (isParentAtom)
            {
                atom.ParseChildren(reader, dataSize);
            }
            else // Leaf atom
            {
                if (dataSize > 0)
                {
                    // Ensure we don't read past the atom's boundary
                    var bytesToRead = (int)Math.Min(dataSize, endPosition - stream.Position);
                    if (bytesToRead > 0) atom.Data = reader.ReadBytes(bytesToRead);
                }
            }
            atoms.Add(atom);
            
            // Explicitly set position to the start of the next atom to handle any parsing variations
            stream.Position = atomStart + size;
        }
        return atoms;
    }

    /// <summary>
    /// Parses the children for this parent atom.
    /// </summary>
    private void ParseChildren(BigEndianBinaryReader reader, long dataSize)
    {
        var stream = reader.BaseStream;
        var childrenEndPosition = stream.Position + dataSize;
        
        // Special handling for 'meta' atom, which has a 4-byte version/flags header before its children.
        if (Type == "meta")
        {
            if (dataSize < 4) return;
            Data = reader.ReadBytes(4); // Store the version/flags
        }
        
        Children.AddRange(ParseAtoms(reader, childrenEndPosition, this));
    }
    

    /// <summary>
    /// Recursively searches through the children of this atom to find a descendant with the specified type.
    /// </summary>
    public M4aAtom? FindDescendant(string type)
    {
        // Breadth-first search is often faster for common structures like this
        var queue = new Queue<M4aAtom>(Children);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current.Type == type)
            {
                return current;
            }

            foreach (var child in current.Children)
            {
                queue.Enqueue(child);
            }
        }
        return null;
    }


    /// <summary>
    /// Recursively serializes this atom and its children into a byte array.
    /// </summary>
    public byte[] ToByteArray()
    {
        using var ms = new MemoryStream();
        using (var writer = new BigEndianBinaryWriter(ms))
        {
            var childrenData = Children.Select(c => c.ToByteArray()).ToList();
            var dataSize = (Data?.Length ?? 0) + childrenData.Sum(cd => (long)cd.Length);
            
            var typeBytes = Encoding.GetEncoding("ISO-8859-1").GetBytes(Type);
            
            // Determine if we need a 32-bit or 64-bit size header
            if (dataSize + 8 > uint.MaxValue)
            {
                // 64-bit atom
                var totalSize = 16 + dataSize;
                writer.Write((uint)1); // Special indicator for 64-bit size
                writer.Write(typeBytes);
                writer.Write(totalSize);
            }
            else
            {
                // 32-bit atom
                var totalSize = (uint)(8 + dataSize);
                writer.Write(totalSize);
                writer.Write(typeBytes);
            }

            if (Data != null) writer.Write(Data);

            foreach (var childData in childrenData)
            {
                writer.Write(childData);
            }
        }
        return ms.ToArray();
    }
}