using LogicAPI.Server;

namespace GHXX_TcpBridgeMod.Server {
    public class Loader : ServerMod {
        protected override void Initialize() {
            Logger.Info("TcpBridge mod initialized!");
        }
    }
}