using System;
using System.Linq;
using System.Net;

namespace GHXX_TcpBridgeMod.Server {
    internal static class Util {
        /// <summary>
        /// Returns whether or not the current address may be connected to. This takes the blacklist in <see cref="Config"/>
        /// </summary>
        /// <param name="addr">The ip address to check</param>
        /// <returns>True if the address is not blacklisted, false if it is blacklisted.</returns>
        public static bool IsAddressAllowed(IPAddress addr) {
            var ipBytes = addr.GetAddressBytes();
            if (ipBytes.Length == 4) {
                foreach (var filter in Config.IpAddressBlacklist) {
                    var splitted = filter.Split("/");
                    if (splitted.Length != 2)
                        throw new FormatException("Invalid ip filter fromat!");


                    var addressStrings = splitted[0].Split(".");
                    if (addressStrings.Length != 4)
                        throw new FormatException("Invalid ip in filter!");
                    var addressBytes = addressStrings.Select(x => byte.Parse(x)).ToArray();
                    var filterBitcount = byte.Parse(splitted[1]);

                    bool isInSubnet = true;
                    for (int bit = 0; bit < filterBitcount; bit++) {
                        var bitIndex = bit % 8;
                        var byteIndex = bit / 8;

                        // if the bits are not equal at the given position, then we are not in this subnet --> therefore this rule passes
                        if (((addressBytes[byteIndex] << bitIndex) & 0x8) != ((ipBytes[byteIndex] << bitIndex) & 0x8)) {
                            isInSubnet = false;
                            break;
                        }
                    }

                    if (isInSubnet) // if we are in the subnet, then block the request by returning false
                        return false;
                }

                return true;
            } else if (ipBytes.Length == 8) // ipv6
              {
                return Config.AllowIPv6Connections;
            } else {
                throw new NotImplementedException($"Invalid ip length: {ipBytes.Length}");
            }
        }
    }
}
