# Technology Stack

**Analysis Date:** 2026-05-23

## Languages

**Primary:**
- **C# 12 (.NET 8.0)** - Core backend library (`Engram.Store`), dev CLI (`Engram.Cli`), and sidecar API (`Engram.Api`)
- **TypeScript 5.8** - React frontend application (`Engram.App`)
- **Rust** - Tauri v2 application shell (`Engram.App/src-tauri`)

**Secondary:**
- **PowerShell** - Build, setup, and validation scripts (`build-*.ps1`, `run-dev.ps1`, `validate-install.ps1`)
- **NSIS** - Installer script (`installer.nsi`)
- **JavaScript** - Build configuration (Vite, PostCSS, Tailwind)

## Runtime

**Environment:**
- **Tauri v2 Shell** - Desktop environment running React 19 UI in WebView2 (Windows edge runtime)
- **.NET 8.0 Runtime** - Sidecar API child process spawned by Tauri at `127.0.0.1:5000`
- **Node.js 20.x+** - Frontend build time only

**Package Manager:**
- **NuGet** - .NET dependency manager
- **npm 10.x** - Frontend package manager
- **Cargo** - Rust dependency manager

## Frameworks

**Core:**
- **React 19.1** - Frontend UI library
- **Tauri v2** - Native OS shell wrapper (Rust)
- **ASP.NET Core Minimal API** - Backend server API

**Testing:**
- **xUnit 2.5.3** - Unit and integration testing runner
- **Coverlet.Collector 6.0.0** - Code coverage reporting

**Build/Dev:**
- **Vite 6.3** - Bundler and dev server for React frontend
- **MSBuild / dotnet CLI** - C# compiler and runtime toolchain
- **cargo** - Rust build toolchain

## Key Dependencies

**Critical:**
- **LLamaSharp 0.24.0** - In-process LLM inference engine using llama.cpp bindings
- **LLamaSharp.Backend.Cpu 0.24.0** - CPU inference backend (supports discrete GPU via Vulkan)
- **Microsoft.Playwright 1.60.0** - Web automation driver for the browser agent runtime
- **CopilotKit (^1.8.0)** - UI integration for AI interaction (react-core, react-ui, react-textarea)

**Infrastructure:**
- **System.Drawing.Common (10.0.8)** - Core graphics processing (for screen/perception layers)
- **Microsoft.Extensions.Hosting.Abstractions (10.0.8)** - Generic Host utilities
- **Swashbuckle.AspNetCore (6.5.0)** - OpenAPI/Swagger documentation generator
- **System.Threading.RateLimiting (8.0.0)** - Token bucket and concurrency limiting

## Configuration

**Environment:**
- **.engram/config.json** - Local configuration state
- **Tauri Config (`src-tauri/tauri.conf.json`)** - Shell configuration, capabilities, sidecar definitions

**Build:**
- **Directory.Build.props** - Global MSBuild configuration
- **vite.config.ts / tsconfig.json** - Frontend compiler and build configurations
- **tailwind.config.js / postcss.config.js** - Utility styling configurations

## Platform Requirements

**Development:**
- **Windows 10/11 (x64)** - Required due to COM automation, Win32 SendInput APIs, and ScreenCaptureService
- **.NET 8.0 SDK** - C# compiler and runtime
- **Rust Toolchain (MSVC target)** - Cargo and rustc
- **Node.js (LTS)** - npm and package build

**Production:**
- **Windows 10/11 (x64)** - Tauri WebView2 runtime + .NET 8 sidecar
- **DirectX/Vulkan Compatible GPU** - (Optional but recommended for hardware-accelerated local inference)
- **~4GB RAM** (Minimum requirement for Phi-4-mini local inference)

---

*Stack analysis: 2026-05-23*
*Update after major dependency changes*
