using System;

namespace HotReloadKit.Shared
{
    public class HotReloadMessage
    {
        public string Type { get; set; }
        public HotReloadMessage() => Type = GetType().Name;
    }

    public class HotReloadErrorMessage : HotReloadMessage
    {
        public string Description { get; set; }
    }

    public class HotReloadServerConnectionData : HotReloadMessage
    {
        public static string DefaultToken => $@"<<|HotReloadKit|>>";
        public static string CurrentVersion => $@"0.4.0-beta.1";

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
        public byte[] DllData { get; set; }
        public byte[] PdbData { get; set; }
    }
}