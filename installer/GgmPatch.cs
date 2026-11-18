// Switches Unity's built-in VR on or off in the game data: BuildSettings.enabledVRDevices in
// SHMCD_Data\globalgamemanagers (a Unity serialized file, format 17 for Unity 2018.4). With "OpenVR" in that list and
// vr-enabled=1 in boot.config, Unity starts in SteamVR. Without the list entry Unity never loads its VR support.
//
// Only that one field is rewritten: BuildSettings' data grows or shrinks by a few bytes, the objects stored after it
// move by the same (8-byte aligned) amount, and the object table and file size in the header are updated. Everything
// else stays byte-for-byte, so removing the entry again gives back the original file.
//
// File layout (format 17): header (big-endian) u32 metadata size, u32 file size, u32 format, u32 data offset,
// u8 endianness (0 = little), 3 reserved; metadata: Unity version string, i32 platform, u8 type trees present,
// i32 type count, types, i32 object count, objects (4-byte aligned: i64 path id, u32 data start, u32 data size,
// i32 type index). BuildSettings (class 141) starts with three string arrays: scenes, preloaded plugins, VR devices.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

static class GgmPatch
{
    const int BuildSettingsClass = 141;

    class Obj { public long Entry; public long Start; public long Size; public int ClassId; }

    class Parsed
    {
        public byte[] D;
        public bool Little;
        public long DataOffset;
        public List<Obj> Objects = new List<Obj>();
        public Obj Build;
        public int VrStart, VrEnd;           // the VR device array inside the file (absolute positions)
        public List<string> Devices = new List<string>();
    }

    // The VR devices the game starts with (empty = flat game).
    public static List<string> GetVrDevices(string path)
    {
        return Parse(File.ReadAllBytes(path)).Devices;
    }

    // Sets the list. Returns true if the file was changed. The game must not be running.
    public static bool SetVrDevices(string path, string[] devices)
    {
        var p = Parse(File.ReadAllBytes(path));
        if (string.Join("\n", p.Devices.ToArray()) == string.Join("\n", devices)) return false;
        byte[] result = Rewrite(p, devices);
        Parse(result);   // sanity check: the result must read back
        File.WriteAllBytes(path + ".shmcdvr-tmp", result);
        File.Copy(path + ".shmcdvr-tmp", path, true);
        File.Delete(path + ".shmcdvr-tmp");
        return true;
    }

    static byte[] Rewrite(Parsed p, string[] devices)
    {
        // The new array, aligned like Unity writes it (each string padded to 4 bytes).
        var arr = new MemoryStream();
        WriteI32(arr, devices.Length, p.Little);
        foreach (var dv in devices)
        {
            byte[] b = Encoding.UTF8.GetBytes(dv);
            WriteI32(arr, b.Length, p.Little);
            arr.Write(b, 0, b.Length);
            while ((arr.Length & 3) != 0) arr.WriteByte(0);
        }
        byte[] newArr = arr.ToArray();
        int delta = newArr.Length - (p.VrEnd - p.VrStart);

        long oldEnd = p.DataOffset + p.Build.Start + p.Build.Size;
        long newEnd = oldEnd + delta;
        long shift = Align8(newEnd) - Align8(oldEnd);   // later objects keep their 8-byte alignment

        var o = new MemoryStream();
        o.Write(p.D, 0, p.VrStart);
        o.Write(newArr, 0, newArr.Length);
        o.Write(p.D, p.VrEnd, (int)(oldEnd - p.VrEnd));
        while (o.Length < Align8(newEnd)) o.WriteByte(0);
        long rest = Align8(oldEnd);
        if (rest < p.D.Length) o.Write(p.D, (int)rest, (int)(p.D.Length - rest));
        byte[] r = o.ToArray();

        // Object table: BuildSettings' size, and the start of everything stored after it.
        foreach (var ob in p.Objects)
        {
            if (ob == p.Build) PutU32(r, ob.Entry + 12, (uint)(ob.Size + delta), p.Little);
            else if (ob.Start > p.Build.Start) PutU32(r, ob.Entry + 8, (uint)(ob.Start + shift), p.Little);
        }
        PutU32(r, 4, (uint)r.Length, false);   // header: file size (big-endian)
        return r;
    }

    static Parsed Parse(byte[] d)
    {
        var p = new Parsed { D = d };
        uint metaSize = U32(d, 0, false), fileSize = U32(d, 4, false), format = U32(d, 8, false);
        p.DataOffset = U32(d, 12, false);
        if (format != 17) throw new InvalidDataException("unsupported globalgamemanagers format " + format + " (expected 17, Unity 2018)");
        if (fileSize != d.Length) throw new InvalidDataException("globalgamemanagers size mismatch");
        p.Little = d[16] == 0;
        int pos = 20;

        int z = Array.IndexOf(d, (byte)0, pos); pos = z + 1;    // Unity version
        pos += 4;                                                // platform
        bool trees = d[pos++] != 0;
        int typeCount = I32(d, ref pos, p.Little);
        var classOfType = new int[typeCount];
        for (int i = 0; i < typeCount; i++)
        {
            int classId = I32(d, ref pos, p.Little);
            classOfType[i] = classId;
            pos += 1 + 2;                                        // stripped flag, script type index
            if (classId == 114) pos += 16;                       // MonoBehaviour: script id
            pos += 16;                                           // type hash
            if (trees)
            {
                int nodes = I32(d, ref pos, p.Little), strBytes = I32(d, ref pos, p.Little);
                pos += nodes * 24 + strBytes;
            }
        }
        int objCount = I32(d, ref pos, p.Little);
        for (int i = 0; i < objCount; i++)
        {
            pos = (pos + 3) & ~3;
            var ob = new Obj { Entry = pos };
            pos += 8;                                            // path id
            ob.Start = U32(d, pos, p.Little); ob.Size = U32(d, pos + 4, p.Little);
            int t = (int)U32(d, pos + 8, p.Little);
            pos += 12;
            if (t < 0 || t >= typeCount) throw new InvalidDataException("bad type index");
            ob.ClassId = classOfType[t];
            if (p.DataOffset + ob.Start + ob.Size > d.Length) throw new InvalidDataException("object outside the file");
            p.Objects.Add(ob);
            if (ob.ClassId == BuildSettingsClass) p.Build = ob;
        }
        if (pos > 20 + metaSize) throw new InvalidDataException("metadata overrun");
        if (p.Build == null) throw new InvalidDataException("no BuildSettings in globalgamemanagers");

        // BuildSettings: scenes, preloaded plugins, VR devices (string arrays).
        int s = (int)(p.DataOffset + p.Build.Start), end = (int)(s + p.Build.Size);
        int q = s;
        SkipStrings(d, ref q, end, p.Little, null);
        SkipStrings(d, ref q, end, p.Little, null);
        p.VrStart = q;
        SkipStrings(d, ref q, end, p.Little, p.Devices);
        p.VrEnd = q;
        return p;
    }

    static void SkipStrings(byte[] d, ref int q, int end, bool little, List<string> into)
    {
        int n = I32(d, ref q, little);
        if (n < 0 || n > 100000) throw new InvalidDataException("unexpected BuildSettings layout");
        for (int i = 0; i < n; i++)
        {
            int len = I32(d, ref q, little);
            if (len < 0 || q + len > end) throw new InvalidDataException("unexpected BuildSettings layout");
            if (into != null) into.Add(Encoding.UTF8.GetString(d, q, len));
            q += len;
            q = (q + 3) & ~3;   // the file's objects are 8-aligned, so absolute alignment = alignment within the object
        }
        if (q > end) throw new InvalidDataException("unexpected BuildSettings layout");
    }

    static long Align8(long v) { return (v + 7) & ~7L; }

    static uint U32(byte[] d, long p, bool little)
    {
        return little ? (uint)(d[p] | d[p + 1] << 8 | d[p + 2] << 16 | d[p + 3] << 24)
                      : (uint)(d[p] << 24 | d[p + 1] << 16 | d[p + 2] << 8 | d[p + 3]);
    }

    static int I32(byte[] d, ref int p, bool little) { int v = (int)U32(d, p, little); p += 4; return v; }

    static void PutU32(byte[] d, long p, uint v, bool little)
    {
        for (int i = 0; i < 4; i++) d[p + (little ? i : 3 - i)] = (byte)(v >> (8 * i));
    }

    static void WriteI32(Stream s, int v, bool little)
    {
        var b = new byte[4];
        PutU32(b, 0, (uint)v, little);
        s.Write(b, 0, 4);
    }
}
