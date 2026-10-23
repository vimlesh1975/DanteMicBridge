using System;

namespace DanteMicBridge.Core
{
    public interface IDanteAudioTransmitter : IDisposable
    {
        bool IsConnected { get; }
        string ChannelName { get; }
        int SampleRate { get; }
        int BitDepth { get; }
        
        bool Initialize(string channelName, int sampleRate = 48000, int bitDepth = 24);
        void TransmitAudioSamples(float[] leftChannel, float[]? rightChannel, int count);
        void Stop();
    }
}
