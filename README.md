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

## 2. Audinate DAL SDK Integration Details

### How Audinate DAL Works:
The Audinate Dante Application Library (DAL) is proprietary software distributed by Audinate to licensed partners. It provides:
1. `dal.dll` / `dal.lib`
2. `dal.h` (C headers)
3. An OEM license token required during initialization.

### Where to Place the SDK Files:
When you obtain access to the Dante Application Library from the Audinate Developer Portal:
1. Copy `dal.dll` into the output directory:
   `DanteMicBridge\bin\Debug\net8.0-windows\` (or publish root).
2. Open [DanteDalTransmitter.cs](file:///d:/_projects/danteaudio/DanteMicBridge/Core/DanteDalTransmitter.cs) and provide your license token in `config.license_token`.
3. Launch the app. The SDK status badge will turn green (`DAL SDK: Native dal.dll Active`), and your microphone stream will appear directly in **Audinate Dante Controller**.

---

## 3. Alternative without Commercial DAL (Immediate Options):
If you do not have an active Audinate OEM agreement:
1. **Dante Virtual Soundcard (DVS)**: Audinate sells a standalone virtual driver (~$30–$50). When installed, any WASAPI/ASIO app can route microphone audio straight into Dante networks without writing custom C/C++ native network code.
2. **Dante Via**: Automatically detects desktop audio inputs and advertises them as Dante channels on your local network.

---

## 4. Running the Application
From PowerShell:
```powershell
cd d:\_projects\danteaudio\DanteMicBridge
dotnet run
```
Or open [DanteMicBridge.csproj](file:///d:/_projects/danteaudio/DanteMicBridge/DanteMicBridge.csproj) directly in Visual Studio.
