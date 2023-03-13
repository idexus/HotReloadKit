using System;

#if NET5_0_OR_GREATER
#nullable disable
#endif

namespace HotReloadKit.Shared
{
    public class HotReloadMessage
    {
        public string Type { get; set; }
        public HotReloadMessage() => Type = GetType().Name;
    }

    public class HotReloadServerConnectionData : HotReloadMessage
    {
        public static string DefaultToken => $@"<<|HotReloadKit|>>";
        public static string CurrentVersion => $@"0.5.0-beta";

        public string Token { get; set; }
        public string Version { get; set; }
        public Guid Guid { get; set; }
    }

    public class HotReloadClientConnectionData : HotReloadMessage
    {
        public string AssemblyName { get; set; }
        public string PlatformName { get; set; }
    }

    public class HotReloadRequest : HotReloadMessage { }

    public class HotReloadRequestAdditionalTypesMessage : HotReloadMessage
    {
        public string[] TypeNames { get; set; }
    }

    public class HotReloadData : HotReloadMessage
    {
        public string[] TypeNames { get; set; }
        public string[] ChangedTypeNames { get; set; }
        public byte[] DllData { get; set; }
        public byte[] PdbData { get; set; }
    }
}

#if NET6_0_OR_GREATER
#nullable restore
#endif