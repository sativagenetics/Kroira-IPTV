using System;
using System.Runtime.InteropServices;

namespace Kroira.App.Services.Playback
{
    // Thin P/Invoke surface for libmpv (mpv-1.dll). Only the calls actually used by
    // MpvPlayer are declared here. Keep this file free of policy — it just mirrors the
    // mpv client.h ABI.
    internal static class NativeMpv
    {
        private const string Dll = "mpv-1.dll";

        public enum MpvFormat : int
        {
            None = 0,
            String = 1,
            OsdString = 2,
            Flag = 3,
            Int64 = 4,
            Double = 5,
        }

        public enum MpvEventId : int
        {
            None = 0,
            Shutdown = 1,
            LogMessage = 2,
            GetPropertyReply = 3,
            SetPropertyReply = 4,
            CommandReply = 5,
            StartFile = 6,
            EndFile = 7,
            FileLoaded = 8,
            Idle = 11,
            Tick = 14,
            ClientMessage = 16,
            VideoReconfig = 17,
            AudioReconfig = 18,
            Seek = 20,
            PlaybackRestart = 21,
            PropertyChange = 22,
            QueueOverflow = 24,
        }

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        public struct MpvEvent
        {
            public MpvEventId EventId;
            public int Error;
            public ulong ReplyUserdata;
            public IntPtr Data;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        public struct MpvEventProperty
        {
            public IntPtr Name;
            public MpvFormat Format;
            public IntPtr Data;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        public struct MpvEventLogMessage
        {
            public IntPtr Prefix;
            public IntPtr Level;
            public IntPtr Text;
            public int LogLevel;
        }

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr mpv_create();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_initialize(IntPtr ctx);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpv_terminate_destroy(IntPtr ctx);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpv_wakeup(IntPtr ctx);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_set_option_string(IntPtr ctx,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string data);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_set_property_string(IntPtr ctx,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string data);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr mpv_get_property_string(IntPtr ctx,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpv_free(IntPtr data);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr mpv_wait_event(IntPtr ctx, double timeout);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_observe_property(IntPtr ctx, ulong replyUserdata,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name, MpvFormat format);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_command(IntPtr ctx, IntPtr args);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_request_log_messages(IntPtr ctx,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string minLevel);

        public static MpvEvent ReadEvent(IntPtr ptr)
        {
            return new MpvEvent
            {
                EventId = (MpvEventId)Marshal.ReadInt32(ptr, 0),
                Error = Marshal.ReadInt32(ptr, 4),
                ReplyUserdata = unchecked((ulong)Marshal.ReadInt64(ptr, 8)),
                Data = Marshal.ReadIntPtr(ptr, 16),
            };
        }

        public static MpvEventProperty ReadEventProperty(IntPtr ptr)
        {
            return new MpvEventProperty
            {
                Name = Marshal.ReadIntPtr(ptr, 0),
                Format = (MpvFormat)Marshal.ReadInt32(ptr, IntPtr.Size),
                Data = Marshal.ReadIntPtr(ptr, IntPtr.Size == 8 ? 16 : 8),
            };
        }

        public static MpvEventLogMessage ReadEventLogMessage(IntPtr ptr)
        {
            return new MpvEventLogMessage
            {
                Prefix = Marshal.ReadIntPtr(ptr, 0),
                Level = Marshal.ReadIntPtr(ptr, IntPtr.Size),
                Text = Marshal.ReadIntPtr(ptr, IntPtr.Size * 2),
                LogLevel = Marshal.ReadInt32(ptr, IntPtr.Size * 3),
            };
        }

        // Convenience wrapper: pack a variadic command into the char** form mpv expects.
        public static int Command(IntPtr ctx, params string[] args)
        {
            var utf8 = new IntPtr[args.Length + 1];
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    utf8[i] = Marshal.StringToCoTaskMemUTF8(args[i] ?? string.Empty);
                }
                utf8[args.Length] = IntPtr.Zero;

                var handle = GCHandle.Alloc(utf8, GCHandleType.Pinned);
                try
                {
                    return mpv_command(ctx, handle.AddrOfPinnedObject());
                }
                finally
                {
                    handle.Free();
                }
            }
            finally
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (utf8[i] != IntPtr.Zero) Marshal.FreeCoTaskMem(utf8[i]);
                }
            }
        }

        public static string GetPropertyString(IntPtr ctx, string name)
        {
            var ptr = mpv_get_property_string(ctx, name);
            if (ptr == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUTF8(ptr); }
            finally { mpv_free(ptr); }
        }
    }
}
