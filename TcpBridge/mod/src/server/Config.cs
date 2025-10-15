namespace GHXX_TcpBridgeMod.Server {
    internal static class Config {
        public const bool ShowDebugMessages = false;

        public const bool AllowIPv6Connections = false; // if this is set to true then ALL ipv6 addresses will be allowed. If set to false, ALL ipv6 addresses will be blocked

        public static readonly string[] IpAddressBlacklist = new string[] // in the format A.B.C.D/E , A-D being bytes, and E being a mask length
        {
            "127.0.0.0/8",
            "10.0.0.0/8",
            "192.168.0.0/16",
            "172.16.0.0/12"
        };
    }
}
