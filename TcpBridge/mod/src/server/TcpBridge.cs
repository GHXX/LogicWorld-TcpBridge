#define ENABLE_DEBUG_CHECKS

using LogicAPI.Server.Components;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace GHXX_TcpBridgeMod.Server
{
    public class TcpBridge : LogicComponent
    {
        const bool enableDebugMessages = true;

        // proxies:
        byte Data_in
        {
            get
            {
                byte sum = 0;
                for (int i = 0; i < 8; i++)
                {
                    sum <<= 1;
                    if (base.Inputs[i].On)
                        sum |= 1;
                }
                return sum;
            }
        }
        bool Clk_in_in { get => base.Inputs[8].On; }
        bool Clk_out_in { get => base.Inputs[9].On; }
        bool Enable_in { get => base.Inputs[10].On; }

        byte Data_out
        {
            get
            {
                byte sum = 0;
                for (int i = 0; i < 8; i++)
                {
                    if (base.Outputs[i].On)
                        sum |= 1;
                    sum <<= 1;
                }
                return sum;
            }

            set
            {
                for (int i = 7; i >= 0; i--)
                {
                    base.Outputs[i].On = (value & 1) > 0;
                    value >>= 1;
                }
            }
        }

        bool Data_ready_out { get => base.Outputs[8].On; set => base.Outputs[8].On = value; }
        bool Tcp_error_out { get => base.Outputs[9].On; set => base.Outputs[9].On = value; }
        bool Is_connected_out { get => base.Outputs[10].On; set => base.Outputs[10].On = value; }

        bool IsTcpClientIsConnected => this.tcpClient != null && this.tcpClient.Connected;

        void DebugMsg(string s)
        {
            if (enableDebugMessages)
#pragma warning disable CS0162 // Unreachable code detected
                Logger.Info($"TcpBridge {s}");
#pragma warning restore CS0162 // Unreachable code detected
        }

        TcpClient tcpClient = null;
        NetworkStream tcpStream = null;

        short connectFlags = -1; // actually byte
        ConcurrentQueue<byte> tcpSendBuffer = null;
        List<int> tcpConnectDataBuffer = null;
        ConcurrentQueue<int> tcpRecBuffer = null;

        Thread workerThread = null;
        // serial: read and write least significant bit first, and msb last

        bool IsReceiveDataAvailable() => !this.tcpRecBuffer.IsEmpty; // whether or not we can synchronously read a byte

        byte GetNextReceivedByte()
        {
            //#if ENABLE_DEBUG_CHECKS
            if (!IsReceiveDataAvailable())
            {
                throw new Exception("Cannot get next receive bit as no data is available!");
            }
            //#endif
            if (this.tcpRecBuffer.TryDequeue(out int res))
            {
                return (byte)res;
            }
            else
            {
                throw new Exception("Receive data dequeueing failed!");
            }
        }

        void AddSendByte(byte input)
        {
            if (this.connectFlags == -1)
            {
                DebugMsg($"Read connect flag input byte: 0x{input:X2}");
                this.connectFlags = input;
            }
            else
            {
                DebugMsg($"Read serial input byte: 0x{input:X2}");
                if (this.enableConnection) // connection should be active at this point -> throw it into the tcp send buffer
                {
                    this.tcpSendBuffer.Enqueue(input);
                }
                else // connection should not be active --> we are still in setup phase --> throw it into the setup buffer
                {
                    this.tcpConnectDataBuffer.Add(input);
                }
            }
        }

        protected override void Initialize()
        {
            base.Initialize();
            this.tcpSendBuffer = new ConcurrentQueue<byte>();
            this.tcpConnectDataBuffer = new List<int>();
            this.tcpRecBuffer = new ConcurrentQueue<int>();

            this.workerThread = new Thread(WorkerThreadRun);
            this.workerThread.Start();
        }

        bool enableConnection = false;
        bool tcpErrorWasEncountered = false;
        private void WorkerThreadRun()
        {
            try
            {
                while (true)
                {
                    if (!this.tcpErrorWasEncountered)
                    {
                        while (this.tcpSendBuffer.TryDequeue(out byte res))
                        {
                            DebugMsg("A");
                            this.tcpStream.WriteByte(res);
                        }

                        while (this.tcpRecBuffer.Count < 10 && this.tcpClient != null && this.tcpClient.Available > 0)
                        {
                            bool queueUpdate = this.tcpRecBuffer.Count == 0;
                            DebugMsg("B");
                            this.tcpRecBuffer.Enqueue(this.tcpStream.ReadByte());
                            if (queueUpdate)
                                QueueLogicUpdate();

                        }

                        if (this.enableConnection && this.tcpClient == null)
                        {
                            DebugMsg("C");
                            if (this.connectFlags == -1) // cant connect if the connect flags havent been set
                            {
                                this.tcpErrorWasEncountered = true;
                                Logger.Info("Connecting failed: Connect flags werent set!");
                            }
                            else
                            {
                                Logger.Info($"Connectflags: 0x{this.connectFlags:X2}");
                                bool useIpAddress = (0b1000_0000 & this.connectFlags) != 0;
                                bool errored = false;
                                if (useIpAddress)
                                {
                                    if (this.tcpConnectDataBuffer.Count == 6) // we need 4 + 2 bytes for address + port
                                    {
                                        var ipBytes = this.tcpConnectDataBuffer.Take(4).Select(x => (byte)x).ToArray();
                                        var port = (this.tcpConnectDataBuffer[4] << 8) | this.tcpConnectDataBuffer[5];
                                        this.tcpClient = new TcpClient();
                                        try
                                        {
                                            var address = new IPAddress(ipBytes);
                                            if (Util.IsAddressAllowed(address))
                                            {
                                                Logger.Info($"Attempting to connect to ip: {string.Join(".", ipBytes.Select(x => $"{x}"))}:{port}");
                                                this.tcpClient.Connect(address, port);
                                                Logger.Info($"Connected to connect to ip: {string.Join(".", ipBytes.Select(x => $"{x}"))}:{port}");
                                                QueueLogicUpdate();
                                            }
                                            else
                                            {
                                                Logger.Info($"Attempted to connect to blacklisted ip: {string.Join(".", ipBytes.Select(x => $"{x}"))}:{port}");
                                                errored = true;
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            Logger.Info($"Connecting to ip failed with ex: {ex}");
                                            errored = true;
                                        }
                                    }
                                    else
                                    {
                                        Logger.Info($"Connecting failed: Not enough or too many bytes given. Exactly 6 needed, {this.tcpConnectDataBuffer.Count} given.");
                                        errored = true;
                                    }
                                }
                                else
                                {
                                    string hostname = "";
                                    var portBytes = new List<ushort>(2);
                                    bool colonFound = false;
                                    for (int i = 0; i < this.tcpConnectDataBuffer.Count; i++)
                                    {
                                        char chr = (char)this.tcpConnectDataBuffer[i];
                                        if (this.tcpConnectDataBuffer[i] == ':')
                                        {
                                            if (!colonFound)
                                            {
                                                colonFound = true;
                                            }
                                            else
                                            {
                                                Logger.Info("Connecting failed: hostname:port compound did not contain a ':' character");
                                                errored = true;
                                                break;
                                            }
                                        }
                                        else
                                        {
                                            if (colonFound)
                                            {
                                                if (portBytes.Count > 1) // if 2 are in there already
                                                {
                                                    Logger.Info("More than 2 bytes were supplied to represent the port.");
                                                    errored = true;
                                                    break;
                                                }
                                                portBytes.Add((ushort)this.tcpConnectDataBuffer[i]);
                                            }
                                            else
                                                hostname += chr;
                                        }
                                    }

                                    ushort port = (ushort)((portBytes[0] << 8) | portBytes[1]);
                                    if (!errored)
                                    {
                                        this.tcpClient = new TcpClient();
                                        try
                                        {
                                            var addresses = Dns.GetHostAddresses(hostname);

                                            if (addresses.Length == 0)
                                            {
                                                Logger.Info($"Dns lookup for host '{hostname}' failed: No addreses exist for this host.");
                                                errored = true;
                                            }
                                            else
                                            {
                                                var blacklistedAddresses = addresses.Where(x => !Util.IsAddressAllowed(x)).ToList();
                                                if (blacklistedAddresses.Count == 0) // no addresses were blacklisted --> connect
                                                {
                                                    var address = addresses.First();
                                                    Logger.Info($"Attempting to connect to host: {hostname}:{(int)port} ({address}:{(int)port})");
                                                    this.tcpClient.Connect(address, (int)port);
                                                    Logger.Info($"Connected to connect to host: {hostname}:{(int)port} ({address}:{(int)port})");
                                                    QueueLogicUpdate();
                                                }
                                                else // otherwise reject
                                                {
                                                    Logger.Info($"Attempted to connect to host '{hostname}', but one of the ip addresses ({string.Join(", ", blacklistedAddresses.Select(x => x.ToString()))}) is blacklisted, thus no connection was made.");
                                                    errored = true;
                                                }
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            Logger.Info($"Connecting failed with ex: {ex}");
                                            errored = true;
                                        }
                                    }
                                }

                                if (!errored)
                                {
                                    this.tcpStream = this.tcpClient.GetStream();
                                }
                                else
                                {
                                    this.tcpErrorWasEncountered = true;
                                    DebugMsg("C2e");
                                }
                            }

                            if (this.tcpErrorWasEncountered)
                            {
                                QueueLogicUpdate();
                            }
                        }
                        else if (!this.enableConnection && this.tcpClient != null)
                        {
                            try
                            {
                                this.tcpClient.Close();
                                QueueLogicUpdate();
                            }
                            catch (Exception) { }

                            this.tcpClient = null;
                        }
                    }
                    //var sw = new Stopwatch();
                    //sw.Start();
                    //this.tcpClientIsConnected =  &&
                    //    this.tcpClient.Client.Connected;
                    //sw.Stop();
                    //if (sw.ElapsedMilliseconds > 1)
                    //{
                    //    Logger.Warn("Determining connection state took more than 1ms!");
                    //}

                    Thread.Sleep(50);
                }
            }
            catch (Exception ex)
            {
                Logger.Fatal($"Caught workerthread exception: {ex}");
            }
        }

        // TODO enable, even tho its an edgecase
        // public override bool HasPersistentValues => true; 


        bool firstUpdateSkipped = false;
        bool lastClkIn = false;
        bool lastClkOut = false;
        bool lastEnabled = false;

        public override bool InputAtIndexShouldTriggerComponentLogicUpdates(int inputIndex)
        {
            return inputIndex != 0;
        }
        protected override void DoLogicUpdate()
        {
            try
            {

                if (!this.firstUpdateSkipped) // when init isnt completed yet, so we dont falsely trigger anything on the first iteration
                {
                    DebugMsg("D");
                    this.firstUpdateSkipped = true;
                    this.lastClkIn = this.Clk_in_in;
                    this.lastClkOut = this.Clk_out_in;
                    this.lastEnabled = this.Enable_in;
                }
                else
                {
                    if (this.lastClkIn != this.Clk_in_in) // first check clock-edges
                    {
                        DebugMsg("E");
                        if (this.Clk_in_in) // rising edge -> accept new data
                        {
                            AddSendByte(this.Data_in);
                        }
                        this.lastClkIn = this.Clk_in_in;
                    }
                    if (this.lastClkOut != this.Clk_out_in) // second check clock-edges
                    {
                        DebugMsg("E");
                        if (this.Clk_out_in) // rising edge -> accept new data
                        {
                            if (this.Data_ready_out)
                            {
                                DebugMsg("E2");
                                //#if ENABLE_DEBUG_CHECKS
                                if (!IsReceiveDataAvailable())
                                {
                                    DebugMsg("E2ERR");
                                    throw new Exception("No receivedata is available, even though the data_ready_out bit is set!");
                                }

                                DebugMsg("E3");
                                //#endif
                                this.Data_out = GetNextReceivedByte();
                                DebugMsg("E3-done");
                            }
                        }
                        this.lastClkOut = this.Clk_out_in;
                    }

                    if (this.lastEnabled != this.Enable_in) // then check enable-edges
                    {
                        DebugMsg("F");
                        if (this.Enable_in) // rising edge
                        {
                            this.enableConnection = true;
                        }
                        else // falling edge
                        {
                            this.enableConnection = false;
                            this.tcpErrorWasEncountered = false;
                            // TODO clear buffers

                            this.connectFlags = -1;
                            this.Data_out = 0;

                            this.tcpSendBuffer.Clear();
                            this.tcpConnectDataBuffer.Clear();
                            this.tcpRecBuffer.Clear();
                            QueueLogicUpdate();
                        }
                        this.lastEnabled = this.Enable_in;
                    }

                    DebugMsg("F2");
                    this.Tcp_error_out = this.tcpErrorWasEncountered;
                    DebugMsg("F3");
                    this.Data_ready_out = IsReceiveDataAvailable();
                    DebugMsg("F4");
                    this.Is_connected_out = this.IsTcpClientIsConnected;
                    DebugMsg("F5-done");
                }
            }
            catch (Exception ex)
            {
                Logger.Fatal($"Caught tickthread exception: {ex}");
            }
        }

        public override void Dispose()
        {
            base.Dispose();
        }
    }
}