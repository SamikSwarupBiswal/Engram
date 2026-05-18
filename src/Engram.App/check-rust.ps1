$env:PATH = "C:\Users\Samik\.cargo\bin;$env:PATH"
cd C:\projects\Engram\Engram\src\Engram.App
cargo check --manifest-path src-tauri\Cargo.toml 2>&1 | Select-Object -Last 15
