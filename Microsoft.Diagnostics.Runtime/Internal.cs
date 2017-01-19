﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime
{
	internal class ContextHelper
    {
        [ThreadStatic]
        private static volatile byte[] _context;
        private static int _ipOffset;
        private static int _spOffset;

        public static uint ContextFlags { get { return 0x1003f; } }
        public static byte[] Context { get { Init(); return _context; } }
        public static int InstructionPointerOffset { get { Init(); return _ipOffset; } }
        public static int StackPointerOffset { get { Init(); return _spOffset; } }
        public static uint Length { get { Init(); return (uint)_context.Length; } }

        static void Init()
        {
            if (_context != null)
                return;
            
            if (IntPtr.Size == 4)
            {
                _ipOffset = 184;
                _spOffset = 196;
                _context = new byte[716];
            }
            else
            {
                _ipOffset = 248;
                _spOffset = 152;
                _context = new byte[1232];
            }
        }
    }

    internal unsafe class MemoryReader
    {
        #region Variables
        protected ulong _currPageStart;
        protected int _currPageSize;
        protected byte[] _data;
        private byte[] _ptr;
        private byte[] _dword;
        protected IDataReader _dataReader;
        protected int _cacheSize;
        #endregion

        public MemoryReader(IDataReader dataReader, int cacheSize)
        {
            _data = new byte[cacheSize];
            _dataReader = dataReader;
            uint sz = _dataReader.GetPointerSize();
            if (sz != 4 && sz != 8)
                throw new InvalidOperationException("DataReader reported an invalid pointer size.");

            _ptr = new byte[sz];
            _dword = new byte[4];
            _cacheSize = cacheSize;
        }

        public bool ReadDword(ulong addr, out uint value)
        {
            uint size = 4;
            // Is addr on the current page?  If not read the page of memory addr is on.
            // If this fails, we will fall back to a raw read out of the process (which
            // is what MisalignedRead does).
            if ((addr < _currPageStart) || (addr >= _currPageStart + (uint)_currPageSize))
                if (!MoveToPage(addr))
                    return MisalignedRead(addr, out value);

            // If MoveToPage succeeds, we MUST be on the right page.
            Debug.Assert(addr >= _currPageStart);

            // However, the amount of data requested may fall off of the page.  In that case,
            // fall back to MisalignedRead.
            ulong offset = addr - _currPageStart;
            if (offset + size > (uint)_currPageSize)
                return MisalignedRead(addr, out value);

            // If we reach here we know we are on the right page of memory in the cache, and
            // that the read won't fall off of the end of the page.
            value = BitConverter.ToUInt32(_data, (int)offset);
            return true;
        }

        public bool ReadDword(ulong addr, out int value)
        {
            uint tmp = 0;
            bool res = ReadDword(addr, out tmp);

            value = (int)tmp;
            return res;
        }

        internal bool TryReadPtr(ulong addr, out ulong value)
        {
            if ((_currPageStart <= addr) && (addr - _currPageStart < (uint)_currPageSize))
            {
                ulong offset = addr - _currPageStart;
                fixed (byte* b = &_data[offset])
                    if (_ptr.Length == 4)
                    value = *((uint*)b);
                else
                    value = *((ulong*)b);

                return true;
            }

            return MisalignedRead(addr, out value);
        }

        internal bool TryReadDword(ulong addr, out uint value)
        {
            if ((_currPageStart <= addr) && (addr - _currPageStart < (uint)_currPageSize))
            {
                ulong offset = addr - _currPageStart;
                value = BitConverter.ToUInt32(_data, (int)offset);
                fixed (byte* b = &_data[offset])
                    value = *((uint*)b);
                return true;
            }

            return MisalignedRead(addr, out value);
        }

        internal bool TryReadDword(ulong addr, out int value)
        {
            if ((_currPageStart <= addr) && (addr - _currPageStart < (uint)_currPageSize))
            {
                ulong offset = addr - _currPageStart;
                fixed (byte* b = &_data[offset])
                    value = *((int*)b);

                return true;
            }

            return MisalignedRead(addr, out value);
        }

        public bool ReadPtr(ulong addr, out ulong value)
        {
            // Is addr on the current page?  If not read the page of memory addr is on.
            // If this fails, we will fall back to a raw read out of the process (which
            // is what MisalignedRead does).
            if ((addr < _currPageStart) || (addr - _currPageStart > (uint)_currPageSize))
                if (!MoveToPage(addr))
                    return MisalignedRead(addr, out value);

            // If MoveToPage succeeds, we MUST be on the right page.
            Debug.Assert(addr >= _currPageStart);

            // However, the amount of data requested may fall off of the page.  In that case,
            // fall back to MisalignedRead.
            ulong offset = addr - _currPageStart;
            if (offset + (uint)_ptr.Length > (uint)_currPageSize)
            {
                if (!MoveToPage(addr))
                    return MisalignedRead(addr, out value);

                offset = 0;
            }

            // If we reach here we know we are on the right page of memory in the cache, and
            // that the read won't fall off of the end of the page.
            fixed (byte* b = &_data[offset])
                if (_ptr.Length == 4)
                value = *((uint*)b);
            else
                value = *((ulong*)b);

            return true;
        }

        public virtual void EnsureRangeInCache(ulong addr)
        {
            if (!Contains(addr))
                MoveToPage(addr);
        }


        public bool Contains(ulong addr)
        {
            return ((_currPageStart <= addr) && (addr - _currPageStart < (uint)_currPageSize));
        }

        #region Private Functions
        private bool MisalignedRead(ulong addr, out ulong value)
        {
            int size = 0;
            bool res = _dataReader.ReadMemory(addr, _ptr, _ptr.Length, out size);
            fixed (byte* b = _ptr)
                if (_ptr.Length == 4)
                value = *((uint*)b);
            else
                value = *((ulong*)b);
            return res;
        }

        private bool MisalignedRead(ulong addr, out uint value)
        {
            int size = 0;
            bool res = _dataReader.ReadMemory(addr, _dword, _dword.Length, out size);
            value = BitConverter.ToUInt32(_dword, 0);
            return res;
        }

        private bool MisalignedRead(ulong addr, out int value)
        {
            int size = 0;
            bool res = _dataReader.ReadMemory(addr, _dword, _dword.Length, out size);
            value = BitConverter.ToInt32(_dword, 0);
            return res;
        }

        virtual protected bool MoveToPage(ulong addr)
        {
            return ReadMemory(addr);
        }

        protected virtual bool ReadMemory(ulong addr)
        {
            _currPageStart = addr;
            bool res = _dataReader.ReadMemory(_currPageStart, _data, _cacheSize, out _currPageSize);

            if (!res)
            {
                _currPageStart = 0;
                _currPageSize = 0;
            }

            return res;
        }
        #endregion
    }
    
    internal class GCDesc
    {
        private static readonly int s_GCDescSize = IntPtr.Size * 2;

        #region Variables
        private byte[] _data;
        #endregion

        #region Functions
        public GCDesc(byte[] data)
        {
            _data = data;
        }

        public void WalkObject(ulong addr, ulong size, MemoryReader cache, Action<ulong, int> refCallback)
        {
            Debug.Assert(size >= (ulong)IntPtr.Size);

            int series = GetNumSeries();
            int highest = GetHighestSeries();
            int curr = highest;

            if (series > 0)
            {
                int lowest = GetLowestSeries();
                do
                {
                    ulong ptr = addr + GetSeriesOffset(curr);
                    ulong stop = (ulong)((long)ptr + (long)GetSeriesSize(curr) + (long)size);

                    while (ptr < stop)
                    {
                        ulong ret;
                        if (cache.ReadPtr(ptr, out ret) && ret != 0)
                            refCallback(ret, (int)(ptr - addr));

                        ptr += (ulong)IntPtr.Size;
                    }

                    curr -= s_GCDescSize;
                } while (curr >= lowest);
            }
            else
            {
                ulong ptr = addr + GetSeriesOffset(curr);

                while (ptr < (addr + size - (ulong)IntPtr.Size))
                {
                    for (int i = 0; i > series; i--)
                    {
                        uint nptrs = GetPointers(curr, i);
                        uint skip = GetSkip(curr, i);

                        ulong stop = ptr + (ulong)(nptrs * IntPtr.Size);
                        do
                        {
                            ulong ret;
                            if (cache.ReadPtr(ptr, out ret) && ret != 0)
                                refCallback(ret, (int)(ptr - addr));

                            ptr += (ulong)IntPtr.Size;
                        } while (ptr < stop);

                        ptr += skip;
                    }
                }
            }
        }
        #endregion

        #region Private Functions
        private uint GetPointers(int curr, int i)
        {
            int offset = i * IntPtr.Size;
            if (IntPtr.Size == 4)
                return BitConverter.ToUInt16(_data, curr + offset);
            else
                return BitConverter.ToUInt32(_data, curr + offset);
        }

        private uint GetSkip(int curr, int i)
        {
            int offset = i * IntPtr.Size + IntPtr.Size / 2;
            if (IntPtr.Size == 4)
                return BitConverter.ToUInt16(_data, curr + offset);
            else
                return BitConverter.ToUInt32(_data, curr + offset);
        }

        private int GetSeriesSize(int curr)
        {
            if (IntPtr.Size == 4)
                return (int)BitConverter.ToInt32(_data, curr);
            else
                return (int)BitConverter.ToInt64(_data, curr);
        }

        private ulong GetSeriesOffset(int curr)
        {
            ulong offset;
            if (IntPtr.Size == 4)
                offset = BitConverter.ToUInt32(_data, curr + IntPtr.Size);
            else
                offset = BitConverter.ToUInt64(_data, curr + IntPtr.Size);

            return offset;
        }

        private int GetHighestSeries()
        {
            return _data.Length - IntPtr.Size * 3;
        }

        private int GetLowestSeries()
        {
            return _data.Length - ComputeSize(GetNumSeries());
        }

        static private int ComputeSize(int series)
        {
            return IntPtr.Size + series * IntPtr.Size * 2;
        }

        private int GetNumSeries()
        {
            if (IntPtr.Size == 4)
                return (int)BitConverter.ToInt32(_data, _data.Length - IntPtr.Size);
            else
                return (int)BitConverter.ToInt64(_data, _data.Length - IntPtr.Size);
        }
        #endregion
    }


    internal class NativeMethods
    {
        private const string Kernel32LibraryName = "kernel32.dll";

        public const uint FILE_MAP_READ = 4;

        [DllImportAttribute(Kernel32LibraryName)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool FreeLibrary(IntPtr hModule);

        public static IntPtr LoadLibrary(string lpFileName)
        {
            return LoadLibraryEx(lpFileName, 0, LoadLibraryFlags.NoFlags);
        }

        [DllImportAttribute(Kernel32LibraryName, SetLastError = true)]
        public static extern IntPtr LoadLibraryEx(String fileName, int hFile, LoadLibraryFlags dwFlags);

        [Flags]
        public enum LoadLibraryFlags : uint
        {
            NoFlags = 0x00000000,
            DontResolveDllReferences = 0x00000001,
            LoadIgnoreCodeAuthzLevel = 0x00000010,
            LoadLibraryAsDatafile = 0x00000002,
            LoadLibraryAsDatafileExclusive = 0x00000040,
            LoadLibraryAsImageResource = 0x00000020,
            LoadWithAlteredSearchPath = 0x00000008
        }

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWow64Process([In] IntPtr hProcess, [Out] out bool isWow64);

        [DllImport("version.dll")]
        internal static extern bool GetFileVersionInfo(string sFileName, int handle, int size, byte[] infoBuffer);

        [DllImport("version.dll")]
        internal static extern int GetFileVersionInfoSize(string sFileName, out int handle);

        [DllImport("version.dll")]
        internal static extern bool VerQueryValue(byte[] pBlock, string pSubBlock, out IntPtr val, out int len);

        private const int VS_FIXEDFILEINFO_size = 0x34;
        public static short IMAGE_DIRECTORY_ENTRY_COM_DESCRIPTOR = 14;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        internal delegate int CreateDacInstance([In, ComAliasName("REFIID")] ref Guid riid,
                                       [In, MarshalAs(UnmanagedType.Interface)] IDacDataTarget data,
                                       [Out, MarshalAs(UnmanagedType.IUnknown)] out object ppObj);


        [DllImport("kernel32.dll")]
        internal static extern IntPtr GetProcAddress(IntPtr hModule, string procedureName);

        internal static bool IsEqualFileVersion(string file, VersionInfo version)
        {
            int major, minor, revision, patch;
            if (!GetFileVersion(file, out major, out minor, out revision, out patch))
                return false;

            return major == version.Major && minor == version.Minor && revision == version.Revision && patch == version.Patch;
        }


        internal static bool GetFileVersion(string dll, out int major, out int minor, out int revision, out int patch)
        {
            major = minor = revision = patch = 0;

            int handle;
            int len = GetFileVersionInfoSize(dll, out handle);

            if (len <= 0)
                return false;

            byte[] data = new byte[len];
            if (!GetFileVersionInfo(dll, handle, len, data))
                return false;

            IntPtr ptr;
            if (!VerQueryValue(data, "\\", out ptr, out len))
            {
                return false;
            }


            byte[] vsFixedInfo = new byte[len];
            Marshal.Copy(ptr, vsFixedInfo, 0, len);

            minor = (ushort)Marshal.ReadInt16(vsFixedInfo, 8);
            major = (ushort)Marshal.ReadInt16(vsFixedInfo, 10);
            patch = (ushort)Marshal.ReadInt16(vsFixedInfo, 12);
            revision = (ushort)Marshal.ReadInt16(vsFixedInfo, 14);

            return true;
        }

        internal static bool TryGetWow64(IntPtr proc, out bool result)
        {
            if (Environment.OSVersion.Version.Major > 5 ||
                (Environment.OSVersion.Version.Major == 5 && Environment.OSVersion.Version.Minor >= 1))
            {
                return IsWow64Process(proc, out result);
            }
            else
            {
                result = false;
                return false;
            }
        }
    }
}