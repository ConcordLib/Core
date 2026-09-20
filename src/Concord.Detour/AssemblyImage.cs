namespace Concord.Detour;

/// <summary>
///     Prepares an assembly image for a second in-memory load of the same file.
/// </summary>
/// <remarks>
///     Harmony stores a patch as (module id, token) and resolves it against the first loaded module with
///     that id. Every copy loaded from one file shares an id, so after a host reload Harmony keeps
///     handing back members of the oldest copy, which is bound to assemblies that are now reflection-only.
/// </remarks>
public static class AssemblyImage {
    /// <summary>
    ///     Returns a copy of <paramref name="image" /> with a new module version id.
    /// </summary>
    /// <param name="image">The raw bytes of a managed assembly.</param>
    /// <returns>The rewritten image. Every other byte is left alone, the embedded debug data included.</returns>
    /// <exception cref="BadImageFormatException">The bytes are not a managed assembly this can rewrite.</exception>
    public static byte[] WithFreshModuleId(byte[] image) {
        byte[] copy = (byte[])image.Clone();
        Guid.NewGuid().ToByteArray().CopyTo(copy, FindModuleIdOffset(copy));
        return copy;
    }

    private static int FindModuleIdOffset(byte[] image) {
        int pe = Read32(image, 0x3c);
        Expect(image[pe] == 'P' && image[pe + 1] == 'E' && image[pe + 2] == 0 && image[pe + 3] == 0, "no PE signature");

        int count = Read16(image, pe + 6);
        int optional = pe + 24;
        int sections = optional + Read16(image, pe + 20);
        int directories = optional + (Read16(image, optional) == 0x20b ? 112 : 96);

        // Data directory 14 is the CLI header, whose Metadata field points at the metadata root.
        int cli = ToOffset(image, sections, count, Read32(image, directories + (14 * 8)));
        int root = ToOffset(image, sections, count, Read32(image, cli + 8));
        Expect(image[root] == 'B' && image[root + 1] == 'S' && image[root + 2] == 'J' && image[root + 3] == 'B', "no metadata root");

        int cursor = root + 16 + Read32(image, root + 12) + 4;
        int streams = Read16(image, cursor - 2);
        int guids = -1;
        int tables = -1;
        for (int i = 0; i < streams; i++) {
            int offset = Read32(image, cursor);
            cursor += 8;
            string name = ReadName(image, ref cursor);
            if (name == "#GUID") {
                guids = root + offset;
            }
            else if (name == "#~" || name == "#-") {
                tables = root + offset;
            }
        }

        Expect(guids >= 0 && tables >= 0, "no #GUID or table stream");

        byte heaps = image[tables + 6];
        ulong present = (ulong)BitConverter.ToInt64(image, tables + 8);
        Expect((present & 1) != 0, "no module row");

        int rows = tables + 24;
        for (int table = 0; table < 64; table++) {
            if ((present & (1UL << table)) != 0) {
                rows += 4;
            }
        }

        // Module is table 0, so its one row leads the table data: Generation, Name, Mvid, EncId, EncBaseId.
        int index = ReadIndex(image, rows + 2 + ((heaps & 1) != 0 ? 4 : 2), (heaps & 2) != 0);
        Expect(index > 0, "empty module id");

        int position = guids + ((index - 1) * 16);
        Expect(position + 16 <= image.Length, "module id past the end of the image");
        return position;
    }

    private static string ReadName(byte[] image, ref int cursor) {
        int start = cursor;
        while (image[cursor] != 0) {
            cursor++;
        }

        string name = System.Text.Encoding.ASCII.GetString(image, start, cursor - start);
        cursor = (cursor + 4) & ~3;
        return name;
    }

    private static int ReadIndex(byte[] image, int offset, bool wide) {
        return wide ? Read32(image, offset) : Read16(image, offset);
    }

    private static int Read16(byte[] image, int offset) {
        return BitConverter.ToUInt16(image, offset);
    }

    private static int Read32(byte[] image, int offset) {
        return (int)BitConverter.ToUInt32(image, offset);
    }

    private static int ToOffset(byte[] image, int sections, int count, int rva) {
        for (int i = 0; i < count; i++) {
            int header = sections + (i * 40);
            int address = Read32(image, header + 12);
            int size = Math.Max(Read32(image, header + 8), Read32(image, header + 16));
            if (rva >= address && rva < address + size) {
                return Read32(image, header + 20) + (rva - address);
            }
        }

        throw new BadImageFormatException("No section holds RVA 0x" + rva.ToString("x") + ".");
    }

    private static void Expect(bool condition, string what) {
        if (!condition) {
            throw new BadImageFormatException("Cannot rewrite the module id: " + what + ".");
        }
    }
}
