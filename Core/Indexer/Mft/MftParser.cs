using System.Text;

namespace SwiftList.Core.Indexer.Mft;

/// <summary>Low-level NTFS $MFT structure parsing: fixup, $DATA run lists, and attribute walking.</summary>
internal static class MftParser
{
    /// <summary>Applies the update-sequence-array fixup to a raw FILE record in place.</summary>
    internal static void ApplyFixup(byte[] buf, uint bytesPerSector, int recOff, int recLen)
    {
        int usaOff = BitConverter.ToUInt16(buf, recOff + 4);
        int usaCount = BitConverter.ToUInt16(buf, recOff + 6);
        for (var i = 1; i < usaCount; i++)
        {
            var secEnd = recOff + i * (int)bytesPerSector - 2;
            var usaEntry = recOff + usaOff + i * 2;
            if (secEnd + 2 > recOff + recLen || usaEntry + 2 > buf.Length)
                break;
            buf[secEnd] = buf[usaEntry];
            buf[secEnd + 1] = buf[usaEntry + 1];
        }
    }

    /// <summary>Parses the $MFT's own non-resident $DATA run list into (lcn, clusterCount) extents.</summary>
    internal static List<(long lcn, long clusters)> ParseDataRuns(byte[] rec)
    {
        var extents = new List<(long, long)>();
        int a = BitConverter.ToUInt16(rec, 0x14);
        while (a + 8 <= rec.Length)
        {
            var type = BitConverter.ToUInt32(rec, a);
            if (type == 0xFFFFFFFF)
                break;
            var len = BitConverter.ToUInt32(rec, a + 4);
            if (len < 16 || a + len > rec.Length)
                break;
            if (type == 0x80 && rec[a + 8] == 1) // $DATA, non-resident
            {
                int mpOff = BitConverter.ToUInt16(rec, a + 0x20);
                var p = a + mpOff;
                long lcn = 0;
                while (p < a + len && rec[p] != 0)
                {
                    var hdr = rec[p++];
                    var lenBytes = hdr & 0x0F;
                    var offBytes = (hdr >> 4) & 0x0F;
                    if (lenBytes == 0)
                        break;
                    var runLen = ReadLE(rec, p, lenBytes);
                    p += lenBytes;
                    if (offBytes == 0)
                        continue; // sparse hole (unexpected for $MFT)
                    var runOff = ReadSignedLE(rec, p, offBytes);
                    p += offBytes;
                    lcn += runOff;
                    extents.Add((lcn, runLen));
                }
                break;
            }
            a += (int)len;
        }
        return extents;
    }

    /// <summary>
    /// Walks a FILE record's attributes: collects every resident $FILE_NAME (excluding DOS-only 8.3
    /// short names) as (parentReference, name, realSize) into <paramref name="names"/>, and returns the
    /// $STANDARD_INFORMATION file attributes (hidden/system/etc) plus its Creation/LastWrite/LastAccess
    /// FILETIMEs (shared by every hard-linked name, unlike size which $FILE_NAME tracks per link).
    /// </summary>
    internal static uint CollectNames(byte[] buf, int recOff, int recLen, List<(UInt128 parent, string name, long size)> names,
        out long creationTimeUtc, out long lastWriteTimeUtc, out long lastAccessTimeUtc)
    {
        uint stdAttrs = 0;
        creationTimeUtc = 0;
        lastWriteTimeUtc = 0;
        lastAccessTimeUtc = 0;
        // $FILE_NAME's own "real size" (read below, per name) is $DATA's real size DUPLICATED into the
        // directory-entry-like $FILE_NAME record for fast listing -- NTFS only refreshes that copy on
        // rename/move/link, so it can sit stale (often 0) for a file that was written once and never
        // touched again. $DATA's own real-size field (read here from the same already-loaded record,
        // no extra I/O) is the one NTFS keeps authoritative, so it wins whenever present.
        long? dataSize = null;
        (UInt128 parent, string name, long size)? dosFallbackName = null;
        int a = BitConverter.ToUInt16(buf, recOff + 0x14);
        while (a + 8 <= recLen)
        {
            var type = BitConverter.ToUInt32(buf, recOff + a);
            if (type == 0xFFFFFFFF)
                break;
            var len = BitConverter.ToUInt32(buf, recOff + a + 4);
            if (len < 16 || a + len > recLen)
                break;
            var resident = buf[recOff + a + 8] == 0;
            if (type == 0x10 && resident) // $STANDARD_INFORMATION
            {
                var vo = BitConverter.ToUInt16(buf, recOff + a + 0x14);
                if (a + vo + 0x24 <= recLen)
                {
                    creationTimeUtc = BitConverter.ToInt64(buf, recOff + a + vo + 0x00);
                    lastWriteTimeUtc = BitConverter.ToInt64(buf, recOff + a + vo + 0x08);
                    lastAccessTimeUtc = BitConverter.ToInt64(buf, recOff + a + vo + 0x18);
                    stdAttrs = BitConverter.ToUInt32(buf, recOff + a + vo + 0x20);
                }
            }
            else if (type == 0x30 && resident) // $FILE_NAME
            {
                var vo = BitConverter.ToUInt16(buf, recOff + a + 0x14);
                var vp = recOff + a + vo;
                if (vp + 0x42 <= recOff + recLen)
                {
                    var fnAttrs = BitConverter.ToUInt32(buf, vp + 0x38);
                    if ((fnAttrs & (uint)FileAttributes.Directory) != 0)
                        stdAttrs |= (uint)FileAttributes.Directory;

                    var ns = buf[vp + 0x41]; // 0=POSIX 1=Win32 2=DOS 3=Win32&DOS
                    UInt128 parent = (ulong)BitConverter.ToInt64(buf, vp);
                    var size = BitConverter.ToInt64(buf, vp + 0x30); // real (logical) size -- may be stale, see dataSize above
                    int nameLen = buf[vp + 0x40];
                    if (vp + 0x42 + nameLen * 2 <= recOff + recLen)
                    {
                        var parsedName = Encoding.Unicode.GetString(buf, vp + 0x42, nameLen * 2);
                        if (ns != 2)
                        {
                            names.Add((parent, parsedName, size));
                        }
                        else
                        {
                            dosFallbackName ??= (parent, parsedName, size);
                        }
                    }
                }
            }
            else if (type == 0x80 && buf[recOff + a + 0x09] == 0) // $DATA, unnamed stream only (skip alternate data streams)
            {
                if (resident)
                {
                    // Resident value length IS the file's content length -- no separate size field.
                    if (a + 0x14 <= recLen)
                        dataSize = BitConverter.ToUInt32(buf, recOff + a + 0x10);
                }
                else if (a + 0x38 <= recLen)
                {
                    dataSize = BitConverter.ToInt64(buf, recOff + a + 0x30); // real (logical) size, always current
                }
            }
            a += (int)len;
        }

        if (names.Count == 0 && dosFallbackName.HasValue)
        {
            names.Add(dosFallbackName.Value);
        }

        if (dataSize.HasValue)
        {
            for (var i = 0; i < names.Count; i++)
            {
                var (parent, name, _) = names[i];
                names[i] = (parent, name, dataSize.Value);
            }
        }

        return stdAttrs;
    }

    private static long ReadLE(byte[] b, int off, int n)
    {
        long v = 0;
        for (var i = 0; i < n; i++)
            v |= (long)b[off + i] << (8 * i);
        return v;
    }

    private static long ReadSignedLE(byte[] b, int off, int n)
    {
        var v = ReadLE(b, off, n);
        if (n < 8 && (b[off + n - 1] & 0x80) != 0)
            v |= -1L << (8 * n);
        return v;
    }
}
