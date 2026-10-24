# Dante Mic Bridge — Analog Microphone to Dante Audio Desktop App

A modern Windows desktop application built with **C# / .NET 8 WPF**, **WASAPI low-latency audio capture**, and an integration architecture for the **Audinate Dante Application Library (DAL)**.

---

## 1. Features
- **Ultra-low Latency Analog Mic Capture**: Uses Windows Audio Session API (WASAPI) with 10ms hardware buffers.
- **Audinate DAL Ready**: Native P/Invoke binding interface in `Core/DanteNative.cs` and `Core/DanteDalTransmitter.cs`.
- **Dual-Mode Execution**:
  - **With DAL SDK**: Automatically loads `dal.dll` and begins broadcasting real Dante audio packets to Dante Controller and Dante-enabled endpoints.
  - **Without DAL SDK**: Operates in diagnostic / simulation / readiness mode with full live input meter visualization and audio buffer processing so you can test microphones and preamps immediately.
- **Audio Control**:
  - Sample rate selection (44.1 kHz, 48 kHz standard, 96 kHz).
  - Bit depth selection (24-bit PCM / 32-bit Float).
  - Digital preamp gain slider with realtime dB calculation.
  - Stereo peak/VU level meters.
  - Live activity logging console.

---

## 2. Audio-over-IP Protocols & Licensing

### Route 1: AES67 Mode (100% Free & Open Standard — Enabled by Default)
- **Zero License Fees**: Operates using standard RTP (RFC 3550) multicast packets with L24 uncompressed audio.
- **Direct Dante Compatibility**: Standard Dante hardware endpoints (mixers, stage boxes, DSPs, amps) support AES67 mode. In **Audinate Dante Controller**, simply double-click the receiver device, navigate to the **AES67 Config** tab, and enable AES67 mode.
- **Automatic Discovery via SAP**: Broadcasts Session Announcement Protocol (SAP) and SDP packets to `224.2.127.254:9875` every 3 seconds, making the stream visible to AES67-aware Dante devices and software.
- **Multicast Default**: IP `239.69.1.1`, Port `5004`, 48kHz, 24-bit stereo.

### Route 2: Audinate DAL SDK (Commercial OEM License)
- For native proprietary Dante unicast/multicast without AES67 mode, Audinate requires the commercial Dante Application Library (`dal.dll`) and an OEM license token.
- You can switch to this mode at any time using the "Transmission Mode" dropdown in the app.

---

## 4. Running the Application
From PowerShell:
```powershell
cd d:\_projects\danteaudio\DanteMicBridge
dotnet run
```
Or open [DanteMicBridge.csproj](file:///d:/_projects/danteaudio/DanteMicBridge/DanteMicBridge.csproj) directly in Visual Studio.
