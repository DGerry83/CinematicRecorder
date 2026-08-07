using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CinematicRecorder.Capture
{
    /// <summary>
    /// One-shot GPU vendor detection via DXGI adapter enumeration. Used to order the
    /// zero-copy encoder attempts so the vendor matching the render GPU is tried
    /// first instead of always probing NVENC first.
    ///
    /// Detection runs lazily on first access and is cached for the KSP session. Any
    /// failure (no DXGI, no hardware adapters, interop error) yields
    /// <see cref="GpuVendor.Unknown"/>, and callers must treat that as "keep the
    /// previous NVENC-first order" - this class never throws.
    ///
    /// Uses manual vtable P/Invoke rather than [ComImport] so it works reliably on
    /// Unity 2019.4 Mono. No per-frame cost: enumeration happens exactly once.
    /// </summary>
    public static class HardwareDetector
    {
        /// <summary>
        /// GPU vendors identifiable from DXGI adapter vendor IDs.
        /// </summary>
        public enum GpuVendor { Unknown, AMD, Nvidia, Intel }

        private const uint VendorIdAmd = 0x1002;
        private const uint VendorIdNvidia = 0x10DE;
        private const uint VendorIdIntel = 0x8086;
        private const uint DxgiAdapterFlagSoftware = 2;
        private const int DxgiErrorNotFound = unchecked((int)0x887A0002);

        // IID_IDXGIFactory1
        private static readonly Guid IidDxgiFactory1 = new Guid("7b7166ec-21c7-44ae-b21a-c9ae321ae369");

        // IDXGIFactory1 vtable slots: IUnknown (3) + IDXGIObject (4) + IDXGIFactory (5)
        private const int VtableSlotEnumAdapters1 = 12;
        // IDXGIAdapter1 vtable slots: IUnknown (3) + IDXGIObject (4) + IDXGIAdapter (3)
        private const int VtableSlotGetDesc1 = 10;
        // IUnknown::Release
        private const int VtableSlotRelease = 2;

        private static bool _detected;
        private static GpuVendor _primaryVendor = GpuVendor.Unknown;
        private static bool _nvidiaPresent;
        private static bool _amdPresent;

        /// <summary>
        /// Vendor of the hardware adapter with the most dedicated VRAM (best proxy
        /// for the GPU Unity renders on). <see cref="GpuVendor.Unknown"/> when
        /// detection failed or no AMD/NVIDIA/Intel hardware adapter was found.
        /// </summary>
        public static GpuVendor PrimaryGpuVendor
        {
            get { EnsureDetected(); return _primaryVendor; }
        }

        /// <summary>True when any NVIDIA hardware adapter was enumerated.</summary>
        public static bool IsNvidiaGpuPresent
        {
            get { EnsureDetected(); return _nvidiaPresent; }
        }

        /// <summary>True when any AMD hardware adapter was enumerated.</summary>
        public static bool IsAmdGpuPresent
        {
            get { EnsureDetected(); return _amdPresent; }
        }

        private static void EnsureDetected()
        {
            if (_detected) return;
            _detected = true;
            try
            {
                Detect();
            }
            catch (Exception ex)
            {
                // Detection is advisory - fall back to the caller's default order.
                Debug.Log($"[HardwareDetector] DXGI enumeration failed ({ex.GetType().Name}: {ex.Message}) - keeping NVENC-first encoder order");
            }
        }

        private static void Detect()
        {
            IntPtr factory = IntPtr.Zero;
            try
            {
                Guid iid = IidDxgiFactory1;
                int hr = CreateDXGIFactory1(ref iid, out factory);
                if (hr < 0 || factory == IntPtr.Zero)
                {
                    Debug.Log($"[HardwareDetector] CreateDXGIFactory1 failed (hr=0x{hr:X8}) - keeping NVENC-first encoder order");
                    return;
                }

                IntPtr factoryVtable = Marshal.ReadIntPtr(factory);
                var enumAdapters1 = Marshal.GetDelegateForFunctionPointer<EnumAdapters1Delegate>(
                    Marshal.ReadIntPtr(factoryVtable, VtableSlotEnumAdapters1 * IntPtr.Size));

                string primaryDescription = null;
                ulong bestMemory = 0;

                for (uint i = 0; ; i++)
                {
                    IntPtr adapter;
                    hr = enumAdapters1(factory, i, out adapter);
                    if (hr == DxgiErrorNotFound) break; // normal end of enumeration
                    if (hr < 0 || adapter == IntPtr.Zero) break;

                    try
                    {
                        DXGI_ADAPTER_DESC1 desc;
                        GetDesc1(adapter, out desc);

                        if ((desc.Flags & DxgiAdapterFlagSoftware) != 0)
                            continue; // skip WARP / software adapters

                        GpuVendor vendor = VendorFromId(desc.VendorId);
                        if (vendor == GpuVendor.Nvidia) _nvidiaPresent = true;
                        if (vendor == GpuVendor.AMD) _amdPresent = true;

                        ulong memory = desc.DedicatedVideoMemory.ToUInt64();
                        if (vendor != GpuVendor.Unknown && memory >= bestMemory)
                        {
                            bestMemory = memory;
                            _primaryVendor = vendor;
                            primaryDescription = desc.Description;
                        }
                    }
                    finally
                    {
                        ReleaseComObject(adapter);
                    }
                }

                Debug.Log($"[HardwareDetector] Primary GPU: '{primaryDescription ?? "none detected"}' " +
                          $"(vendor: {_primaryVendor}, NVIDIA present: {_nvidiaPresent}, AMD present: {_amdPresent})");
            }
            finally
            {
                if (factory != IntPtr.Zero)
                    ReleaseComObject(factory);
            }
        }

        private static GpuVendor VendorFromId(uint vendorId)
        {
            switch (vendorId)
            {
                case VendorIdAmd: return GpuVendor.AMD;
                case VendorIdNvidia: return GpuVendor.Nvidia;
                case VendorIdIntel: return GpuVendor.Intel;
                default: return GpuVendor.Unknown;
            }
        }

        private static void GetDesc1(IntPtr adapter, out DXGI_ADAPTER_DESC1 desc)
        {
            IntPtr vtable = Marshal.ReadIntPtr(adapter);
            var getDesc1 = Marshal.GetDelegateForFunctionPointer<GetDesc1Delegate>(
                Marshal.ReadIntPtr(vtable, VtableSlotGetDesc1 * IntPtr.Size));
            getDesc1(adapter, out desc);
        }

        private static void ReleaseComObject(IntPtr comObject)
        {
            IntPtr vtable = Marshal.ReadIntPtr(comObject);
            var release = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(
                Marshal.ReadIntPtr(vtable, VtableSlotRelease * IntPtr.Size));
            release(comObject);
        }

        #region Interop

        [DllImport("dxgi.dll", ExactSpelling = true)]
        private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr ppFactory);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int EnumAdapters1Delegate(IntPtr factory, uint adapterIndex, out IntPtr adapter);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetDesc1Delegate(IntPtr adapter, out DXGI_ADAPTER_DESC1 desc);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint ReleaseDelegate(IntPtr comObject);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DXGI_ADAPTER_DESC1
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Description;
            public uint VendorId;
            public uint DeviceId;
            public uint SubSysId;
            public uint Revision;
            public UIntPtr DedicatedVideoMemory;
            public UIntPtr DedicatedSystemMemory;
            public UIntPtr SharedSystemMemory;
            public ulong AdapterLuid;
            public uint Flags;
        }

        #endregion
    }
}
