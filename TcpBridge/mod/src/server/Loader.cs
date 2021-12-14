using LogicAPI.Server.Components;
using LogicAPI.Server;
using LogicLog;
using System;
using LogicWorld.Server.Circuitry;

namespace GHXX_TcpBridgeMod.Server
{
    public class Loader : ServerMod
    {
        protected override void Initialize()
        {
            Logger.Info("TcpBridge mod initialized!");
        }
    }
}