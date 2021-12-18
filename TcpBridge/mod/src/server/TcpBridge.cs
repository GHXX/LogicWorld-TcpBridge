#define ENABLE_DEBUG_CHECKS

using LogicAPI.Server.Components;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace GHXX_TcpBridgeMod.Server
{
    public class TcpBridge : LogicComponent
    {
        // proxies:
        bool Data_in { get => base.Inputs[0].On; }
        bool Clk_in { get => base.Inputs[1].On; }
        bool Enable_in { get => base.Inputs[2].On; }

        bool Data_out { get => base.Outputs[0].On; set => base.Outputs[0].On = value; }
        bool Data_ready_out { get => base.Outputs[1].On; set => base.Outputs[1].On = value; }
        bool Tcp_error_out { get => base.Outputs[2].On; set => base.Outputs[2].On = value; }
        bool Is_connected_out { get => base.Outputs[3].On; set => base.Outputs[3].On = value; }

        bool tcpClientIsConnected = false; // effectively the same as this.tcpClient != null && this.tcpClient.Connected

        void debugMsg(string s) => Logger.Info($"TcpBridge {s}");

        TcpClient tcpClient = null;
        NetworkStream tcpStream = null;

        int receiveBitIndex = 0;
        int receiveBufByte = -1;

        int connectFlags = -1; // actually byte
        ConcurrentQueue<int> tcpSendBuffer = null;
        List<int> tcpConnectDataBuffer = null;
        ConcurrentQueue<int> tcpRecBuffer = null;

        Thread workerThread = null;
        // serial: read and write least significant bit first, and msb last

        bool IsReceiveDataAvailable() => this.receiveBufByte != -1 || !this.tcpRecBuffer.IsEmpty; // whether or not we can synchronously read a byte
        bool GetNextReceiveBit()
        {
            //#if ENABLE_DEBUG_CHECKS
            if (!IsReceiveDataAvailable())
            {
                throw new Exception("Cannot get next receive bit as no data is available!");
            }
            //#endif
            if (this.receiveBitIndex == 0) // need to read a new byte from the buffer
            {
                this.receiveBufByte = this.tcpStream.ReadByte();
            }

            bool bit = (this.receiveBufByte & (1 << this.receiveBitIndex)) != 0;

            if (this.receiveBitIndex == 7)
            {
                this.receiveBitIndex = 0;
            }
            else
            {
                this.receiveBitIndex++;
            }

            return bit;
        }

        int sendBitIndex = 0;
        int sendBufByte = -1;
        void AddSendBit(bool input)
        {
            if (this.sendBitIndex == 0) // need to start a new byte
            {
                this.sendBufByte = 0;
            }

            if (input)
                this.sendBufByte |= (1 << this.sendBitIndex);

            debugMsg($"Received {(input ? "HI" : "LOW")} input bit");

            if (this.sendBitIndex == 7)
            {
                this.sendBitIndex = 0;
                // send byte is ready at this point
                if (this.connectFlags == -1)
                {
                    debugMsg($"Read finished connect flag input byte: 0x{this.sendBufByte:X2}");
                    this.connectFlags = this.sendBufByte;
                }
                else
                {
                    debugMsg($"Read finished serial input byte: 0x{this.sendBufByte:X2}");
                    if (this.enableConnection) // connection should be active at this point -> throw it into the tcp send buffer
                    {
                        this.tcpSendBuffer.Enqueue(this.sendBufByte);
                    }
                    else // connection should not be active --> we are still in setup phase --> throw it into the setup buffer
                    {
                        this.tcpConnectDataBuffer.Add(this.sendBufByte);
                    }
                }
            }
            else
            {
                this.sendBitIndex++;
            }
        }

        protected override void Initialize()
        {
            base.Initialize();
            this.tcpSendBuffer = new ConcurrentQueue<int>();
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
                        while (this.tcpSendBuffer.TryDequeue(out int res))
                        {
                            debugMsg("A");
                            this.tcpStream.WriteByte((byte)res);
                        }

                        while (this.tcpRecBuffer.Count < 10 && this.tcpClient != null && this.tcpClient.Available > 0)
                        {
                            bool queueUpdate = this.tcpRecBuffer.Count == 0 && this.receiveBitIndex == 0;
                            debugMsg("B");
                            this.tcpRecBuffer.Enqueue(this.tcpStream.ReadByte());
                            if (queueUpdate)
                                QueueLogicUpdate();

                        }

                        if (this.enableConnection && this.tcpClient == null)
                        {
                            debugMsg("C");
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
                                        var port = this.tcpConnectDataBuffer[4] | (this.tcpConnectDataBuffer[5] << 8);
                                        this.tcpClient = new TcpClient();
                                        try
                                        {
                                            Logger.Info($"Attempting to connect to ip: {string.Join(".", ipBytes.Select(x => $"{x}"))}:{port}");
                                            this.tcpClient.Connect(new IPAddress(ipBytes), port);
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
                                    string portString = "";
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
                                                portString += chr;
                                            else
                                                hostname += chr;
                                        }
                                    }

                                    if (!errored)
                                    {
                                        if (ushort.TryParse(portString, out ushort port))
                                        {
                                            this.tcpClient = new TcpClient();
                                            try
                                            {
                                                Logger.Info($"Attempting to connect to host: {hostname}:{(int)port}");
                                                this.tcpClient.Connect(hostname, (int)port);
                                            }
                                            catch (Exception ex)
                                            {
                                                Logger.Info($"Connecting failed with ex: {ex}");
                                                errored = true;
                                            }
                                        }
                                        else
                                        {
                                            errored = true;
                                            Logger.Info("Connecting failed: Invalid port");
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
                                    debugMsg("C2e");
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
                            }
                            catch (Exception) { }

                            this.tcpClient = null;
                        }
                    }
                    var sw = new Stopwatch();
                    sw.Start();
                    tcpClientIsConnected = this.tcpClient != null && this.tcpClient.Client != null && this.tcpClient.Client.Connected;
                    sw.Stop();
                    if (sw.ElapsedMilliseconds > 1)
                    {
                        Logger.Warn("Determining connection state took more than 1ms!");
                    }

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
        bool lastClk = false;
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
                    debugMsg("D");
                    this.firstUpdateSkipped = true;
                    this.lastClk = this.Clk_in;
                    this.lastEnabled = this.Enable_in;
                }
                else
                {
                    if (this.lastClk != this.Clk_in) // first check clock-edges
                    {
                        debugMsg("E");
                        if (this.Clk_in) // rising edge -> accept new data
                        {
                            AddSendBit(this.Data_in);
                            if (this.Data_ready_out)
                            {
                                //#if ENABLE_DEBUG_CHECKS
                                if (!IsReceiveDataAvailable())
                                {
                                    throw new Exception("No receivedata is available, even though the data_ready_out bit is set!");
                                }
                                //#endif
                                this.Data_out = GetNextReceiveBit();
                            }

                        }
                        this.lastClk = this.Clk_in;
                    }

                    if (this.lastEnabled != this.Enable_in) // then check enable-edges
                    {
                        debugMsg("F");
                        if (this.Enable_in) // rising edge
                        {
                            this.enableConnection = true;
                        }
                        else // falling edge
                        {
                            this.enableConnection = false;
                            this.tcpErrorWasEncountered = false;
                            // TODO clear buffers

                            this.sendBitIndex = 0;
                            this.sendBufByte = -1;
                            this.connectFlags = -1;
                            this.receiveBitIndex = 0;
                            this.receiveBufByte = -1;

                            this.tcpSendBuffer.Clear();
                            this.tcpConnectDataBuffer.Clear();
                            this.tcpRecBuffer.Clear();
                            QueueLogicUpdate();
                        }
                        this.lastEnabled = this.Enable_in;
                    }

                    debugMsg("F2");
                    this.Tcp_error_out = this.tcpErrorWasEncountered;
                    this.Data_ready_out = false; // IsReceiveDataAvailable();
                    this.Is_connected_out = tcpClientIsConnected;
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