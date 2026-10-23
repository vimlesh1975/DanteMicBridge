using System;
using System.IO;
using System.Runtime.InteropServices;

namespace DanteMicBridge.Core
{
    public class DanteDalTransmitter : IDanteAudioTransmitter
    {
        private bool _isConnected;
        private string _channelName = "Mic-Ch1";
        private int _sampleRate = 48000;
        private int _bitDepth = 24;
        private readonly bool _dalLibraryFound;

        public bool IsConnected => _isConnected;
        public string ChannelName => _channelName;
        public int SampleRate => _sampleRate;
        public int BitDepth => _bitDepth;
        public bool IsSdkAvailable => _dalLibraryFound;

        public event Action<string>? OnLog;

        public DanteDalTransmitter()
        {
            _dalLibraryFound = File.Exists("dal.dll");
        }

        public bool Initialize(string channelName, int sampleRate = 48000, int bitDepth = 24)
        {
            _channelName = channelName;
            _sampleRate = sampleRate;
            _bitDepth = bitDepth;

            if (!_dalLibraryFound)
            {
                OnLog?.Invoke("[DAL Wrapper] dal.dll was not found in application directory. Falling back to DAL readiness / mock transmitter mode.");
                _isConnected = true;
                return true;
            }

            try
            {
                var config = new DanteNative.DalConfig
                {
                    device_name = "MicBridge-" + Environment.MachineName,
                    sample_rate = (uint)sampleRate,
                    tx_channels = 2,
                    rx_channels = 0,
                    latency_us = 1000,
                    license_token = ""
                };

                var res = DanteNative.dal_init(ref config, (level, msg) => OnLog?.Invoke($"[DAL Core {level}] {msg}"));
                if (res == DanteNative.DalResult.DAL_SUCCESS)
                {
                    _isConnected = true;
                    OnLog?.Invoke($"[DAL Core] Dante Application Library initialized successfully. Tx Channel: {channelName}");
                    return true;
                }
                else
                {
                    OnLog?.Invoke($"[DAL Core] dal_init returned status {res}.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[DAL Error] Exception initializing DAL: {ex.Message}");
                return false;
            }
        }

        public void TransmitAudioSamples(float[] leftChannel, float[]? rightChannel, int count)
        {
            if (!_isConnected || count <= 0) return;

            if (_dalLibraryFound)
            {
                GCHandle handle = GCHandle.Alloc(leftChannel, GCHandleType.Pinned);
                try
                {
                    DanteNative.dal_tx_send_float(0, handle.AddrOfPinnedObject(), (uint)count);
                }
                finally
                {
                    handle.Free();
                }

                if (rightChannel != null && rightChannel.Length >= count)
                {
                    GCHandle handleR = GCHandle.Alloc(rightChannel, GCHandleType.Pinned);
                    try
                    {
                        DanteNative.dal_tx_send_float(1, handleR.AddrOfPinnedObject(), (uint)count);
                    }
                    finally
                    {
                        handleR.Free();
                    }
                }
            }
        }

        public void Stop()
        {
            if (_isConnected && _dalLibraryFound)
            {
                try
                {
                    DanteNative.dal_shutdown();
                }
                catch { }
            }
            _isConnected = false;
            OnLog?.Invoke("[DAL Core] Dante transmitter stopped.");
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
