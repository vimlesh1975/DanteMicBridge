using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace DanteMicBridge.Core
{
    /// <summary>
    /// AES67 / RTP Audio-over-IP Transmitter.
    /// Implements standard RFC 3550 RTP multicast audio streaming compatible with Dante (AES67 mode),
    /// Ravenna, Livewire, and standard AoIP hardware with SAP (Session Announcement Protocol) advertising.
    /// </summary>
    public class Aes67AudioTransmitter : IDanteAudioTransmitter
    {
        private Socket? _rtpSocket;
        private Timer? _sapTimer;
        private IPEndPoint? _multicastEndPoint;
        private bool _isConnected;
        private string _channelName = "Mic-Ch1";
        private int _sampleRate = 48000;
        private int _bitDepth = 24;
        private string _multicastIp = "239.69.1.1";
        private int _rtpPort = 5004;

        // RTP Header state
        private ushort _sequenceNumber;
        private uint _timestamp;
        private readonly uint _ssrc;

        // AES67 standard payload: 1ms packets at 48kHz = 48 samples per packet (L24 audio)
        private const int PacketSampleCount = 48;
        private readonly byte[] _rtpPacketBuffer;
        private int _bufferSampleCount;
        private readonly float[] _pendingSamplesL = new float[PacketSampleCount * 4];
        private readonly float[] _pendingSamplesR = new float[PacketSampleCount * 4];

        public bool IsConnected => _isConnected;
        public string ChannelName => _channelName;
        public int SampleRate => _sampleRate;
        public int BitDepth => _bitDepth;
        public string MulticastIp => _multicastIp;
        public int RtpPort => _rtpPort;

        public event Action<string>? OnLog;

        public Aes67AudioTransmitter()
        {
            var rand = new Random();
            _ssrc = (uint)rand.Next(1, int.MaxValue);
            _sequenceNumber = (ushort)rand.Next(0, 65535);
            _timestamp = (uint)rand.Next(0, int.MaxValue);

            // RTP Header = 12 bytes. For stereo 24-bit (3 bytes * 2 channels = 6 bytes/sample) * 48 samples = 288 bytes
            // Max buffer size: 12 + 48 * 2 * 4 = 396 bytes
            _rtpPacketBuffer = new byte[1024];
        }

        public bool Initialize(string channelName, int sampleRate = 48000, int bitDepth = 24)
        {
            return Initialize(channelName, sampleRate, bitDepth, "239.69.1.1", 5004);
        }

        public bool Initialize(string channelName, int sampleRate, int bitDepth, string multicastIp, int port)
        {
            Stop();

            _channelName = channelName;
            _sampleRate = sampleRate;
            _bitDepth = bitDepth;
            _multicastIp = multicastIp;
            _rtpPort = port;

            try
            {
                _rtpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                _rtpSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _rtpSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 32);

                _multicastEndPoint = new IPEndPoint(IPAddress.Parse(_multicastIp), _rtpPort);

                _isConnected = true;
                _bufferSampleCount = 0;

                // Start Session Announcement Protocol (SAP) to advertise stream to Dante Controller / Ravenna receivers
                StartSapAnnouncements();

                OnLog?.Invoke($"[AES67 AoIP] Transmitter online. Multicast={_multicastIp}:{_rtpPort}, Ch={_channelName}, Rate={_sampleRate}Hz, Bits={_bitDepth}");
                OnLog?.Invoke($"[AES67 AoIP] SDP Session announcement broadcasting to 224.2.127.254:9875 for Dante discovery.");
                return true;
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[AES67 Error] Failed to initialize socket: {ex.Message}");
                _isConnected = false;
                return false;
            }
        }

        public void TransmitAudioSamples(float[] leftChannel, float[]? rightChannel, int count)
        {
            if (!_isConnected || _rtpSocket == null || _multicastEndPoint == null || count <= 0) return;

            int srcOffset = 0;
            while (srcOffset < count)
            {
                int needed = PacketSampleCount - _bufferSampleCount;
                int toCopy = Math.Min(needed, count - srcOffset);

                Array.Copy(leftChannel, srcOffset, _pendingSamplesL, _bufferSampleCount, toCopy);
                if (rightChannel != null)
                {
                    Array.Copy(rightChannel, srcOffset, _pendingSamplesR, _bufferSampleCount, toCopy);
                }
                else
                {
                    Array.Copy(leftChannel, srcOffset, _pendingSamplesR, _bufferSampleCount, toCopy);
                }

                _bufferSampleCount += toCopy;
                srcOffset += toCopy;

                if (_bufferSampleCount >= PacketSampleCount)
                {
                    SendRtpPacket();
                    _bufferSampleCount = 0;
                }
            }
        }

        private void SendRtpPacket()
        {
            if (_rtpSocket == null || _multicastEndPoint == null) return;

            // 1. RTP Header (RFC 3550) - 12 bytes
            // V=2, P=0, X=0, CC=0 -> 0x80
            _rtpPacketBuffer[0] = 0x80;
            // M=0, Payload Type (96 - 127 are dynamic, 97 is common for AES67 L24)
            _rtpPacketBuffer[1] = 97;

            // Sequence Number (Big Endian)
            _rtpPacketBuffer[2] = (byte)(_sequenceNumber >> 8);
            _rtpPacketBuffer[3] = (byte)(_sequenceNumber & 0xFF);
            _sequenceNumber++;

            // Timestamp (Big Endian)
            _rtpPacketBuffer[4] = (byte)(_timestamp >> 24);
            _rtpPacketBuffer[5] = (byte)((_timestamp >> 16) & 0xFF);
            _rtpPacketBuffer[6] = (byte)((_timestamp >> 8) & 0xFF);
            _rtpPacketBuffer[7] = (byte)(_timestamp & 0xFF);
            _timestamp += (uint)PacketSampleCount;

            // SSRC Identifier (Big Endian)
            _rtpPacketBuffer[8] = (byte)(_ssrc >> 24);
            _rtpPacketBuffer[9] = (byte)((_ssrc >> 16) & 0xFF);
            _rtpPacketBuffer[10] = (byte)((_ssrc >> 8) & 0xFF);
            _rtpPacketBuffer[11] = (byte)(_ssrc & 0xFF);

            int offset = 12;

            // 2. Audio Payload: Standard AES67 L24 (24-bit Big-Endian PCM, Stereo Interleaved)
            for (int i = 0; i < PacketSampleCount; i++)
            {
                // Left Sample (24-bit signed, clamped to [-8388607, 8388607])
                int sampleL = (int)(Math.Clamp(_pendingSamplesL[i], -1.0f, 1.0f) * 8388607.0f);
                _rtpPacketBuffer[offset++] = (byte)((sampleL >> 16) & 0xFF);
                _rtpPacketBuffer[offset++] = (byte)((sampleL >> 8) & 0xFF);
                _rtpPacketBuffer[offset++] = (byte)(sampleL & 0xFF);

                // Right Sample
                int sampleR = (int)(Math.Clamp(_pendingSamplesR[i], -1.0f, 1.0f) * 8388607.0f);
                _rtpPacketBuffer[offset++] = (byte)((sampleR >> 16) & 0xFF);
                _rtpPacketBuffer[offset++] = (byte)((sampleR >> 8) & 0xFF);
                _rtpPacketBuffer[offset++] = (byte)(sampleR & 0xFF);
            }

            try
            {
                _rtpSocket.SendTo(_rtpPacketBuffer, 0, offset, SocketFlags.None, _multicastEndPoint);
            }
            catch
            {
                // Ignore transient network socket drops during transmission
            }
        }

        private void StartSapAnnouncements()
        {
            // Session Announcement Protocol: Standard SAP packet sent every 3 seconds to 224.2.127.254:9875
            _sapTimer = new Timer(_ => SendSapPacket(), null, 100, 3000);
        }

        private void SendSapPacket()
        {
            if (!_isConnected) return;

            try
            {
                using var sapSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                sapSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 32);
                var sapEp = new IPEndPoint(IPAddress.Parse("224.2.127.254"), 9875);

                string originIp = GetLocalIpAddress();
                string sdp = 
                    "v=0\r\n" +
                    $"o=- {_ssrc} 1 IN IP4 {originIp}\r\n" +
                    $"s={_channelName}\r\n" +
                    $"c=IN IP4 {_multicastIp}/32\r\n" +
                    "t=0 0\r\n" +
                    $"m=audio {_rtpPort} RTP/AVP 97\r\n" +
                    $"a=rtpmap:97 L24/{_sampleRate}/2\r\n" +
                    "a=ptime:1\r\n" +
                    "a=mediaclk:direct=0\r\n" +
                    "a=ts-refclk:localmac\r\n";

                byte[] sdpBytes = Encoding.UTF8.GetBytes(sdp);
                string mimeType = "application/sdp\0";
                byte[] mimeBytes = Encoding.ASCII.GetBytes(mimeType);

                // SAP Header: V=1, A=0, R=0, T=0, E=0, C=0 -> 0x20
                // Auth length = 0
                // Message ID hash = 0x1234
                byte[] sapHeader = new byte[] { 0x20, 0x00, 0x12, 0x34 };
                byte[] originBytes = IPAddress.Parse(originIp).GetAddressBytes();

                using var ms = new MemoryStream();
                ms.Write(sapHeader, 0, sapHeader.Length);
                ms.Write(originBytes, 0, originBytes.Length);
                ms.Write(mimeBytes, 0, mimeBytes.Length);
                ms.Write(sdpBytes, 0, sdpBytes.Length);

                byte[] packet = ms.ToArray();
                sapSocket.SendTo(packet, sapEp);
            }
            catch
            {
                // Non-critical background SAP announcement
            }
        }

        private string GetLocalIpAddress()
        {
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
                socket.Connect("8.8.8.8", 65530);
                if (socket.LocalEndPoint is IPEndPoint endPoint)
                {
                    return endPoint.Address.ToString();
                }
            }
            catch { }
            return "127.0.0.1";
        }

        public void Stop()
        {
            _isConnected = false;
            if (_sapTimer != null)
            {
                _sapTimer.Dispose();
                _sapTimer = null;
            }

            if (_rtpSocket != null)
            {
                try
                {
                    _rtpSocket.Close();
                    _rtpSocket.Dispose();
                }
                catch { }
                _rtpSocket = null;
            }

            OnLog?.Invoke("[AES67 AoIP] Audio-over-IP transmitter stopped.");
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
