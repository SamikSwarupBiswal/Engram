use std::fs;
use std::net::TcpListener;
use std::path::{Path, PathBuf};
use std::process::Command;
use serde::Serialize;

#[derive(Serialize, Debug, Clone)]
pub struct PreflightReport {
    pub sidecar_exists: bool,
    pub write_access: bool,
    pub port_available: bool,
    pub vc_runtime_installed: bool,
    pub webview2_installed: bool,
    pub wal_consistent: bool,
    pub severity: String, // "Info", "Warning", "IntegrityUncertain", "Critical"
    pub errors: Vec<String>,
}

pub fn run_checks(install_dir: &Path) -> PreflightReport {
    let mut errors = Vec::new();
    let mut severity = "Info".to_string();

    // 1. Check sidecar executable existence
    let sidecar_exe = if cfg!(target_os = "windows") {
        install_dir.join("sidecar").join("publish").join("Engram.Api.exe")
    } else {
        install_dir.join("sidecar").join("publish").join("Engram.Api")
    };
    let sidecar_exists = sidecar_exe.exists();
    if !sidecar_exists {
        errors.push(format!("Sidecar executable not found at: {:?}", sidecar_exe));
        severity = "Critical".to_string();
    }

    // 2. Check writable local directories
    let local_app_data = std::env::var("LOCALAPPDATA").unwrap_or_else(|_| "C:\\temp".to_string());
    let engram_dir = PathBuf::from(local_app_data).join("Engram");
    let test_file = engram_dir.join(".write_test");
    
    let write_access = match fs::create_dir_all(&engram_dir) {
        Ok(_) => {
            match fs::write(&test_file, "test") {
                Ok(_) => {
                    let _ = fs::remove_file(&test_file);
                    true
                }
                Err(_) => false,
            }
        }
        Err(_) => false,
    };

    if !write_access {
        errors.push(format!("Directory %LOCALAPPDATA%/Engram/ is not writable. Check folder permissions."));
        severity = "Critical".to_string();
    }

    // 3. Check port 5000 availability
    let port_available = TcpListener::bind("127.0.0.1:5000").is_ok();
    if !port_available {
        // If port 5000 is occupied, it's a Warning, not a Critical block, because the sidecar
        // can try to bind to a fallback port or we can log it. But for local deployment,
        // it means another instance or process is running.
        errors.push("Port 5000 is currently occupied. Check if another instance of Engram is running.".to_string());
        if severity != "Critical" {
            severity = "Warning".to_string();
        }
    }

    // 4. VC++ Redistributable status (Windows only)
    let mut vc_runtime_installed = true;
    if cfg!(target_os = "windows") {
        vc_runtime_installed = check_registry_key(
            "HKLM\\SOFTWARE\\Microsoft\VisualStudio\\14.0\\VC\\Runtimes\\x64",
            "Installed",
        );
        if !vc_runtime_installed {
            errors.push("Visual C++ 2015-2022 Redistributable (x64) is not installed. GPU inference may fail.".to_string());
            if severity != "Critical" {
                severity = "Warning".to_string();
            }
        }
    }

    // 5. WebView2 presence (Windows only)
    let mut webview2_installed = true;
    if cfg!(target_os = "windows") {
        // Query WOW6432Node and regular registry clients for WebView2 Runtime
        let webview_hklm_wow = check_registry_key(
            "HKLM\\SOFTWARE\\WOW6432Node\\Microsoft\\EdgeUpdate\\Clients\\{F3017226-FE2A-4295-8ABB-3D584A7316B7}",
            "pv",
        );
        let webview_hklm = check_registry_key(
            "HKLM\\SOFTWARE\\Microsoft\\EdgeUpdate\\Clients\\{F3017226-FE2A-4295-8ABB-3D584A7316B7}",
            "pv",
        );
        let webview_hkcu = check_registry_key(
            "HKCU\\SOFTWARE\\Microsoft\\EdgeUpdate\\Clients\\{F3017226-FE2A-4295-8ABB-3D584A7316B7}",
            "pv",
        );
        webview2_installed = webview_hklm_wow || webview_hklm || webview_hkcu;

        if !webview2_installed {
            errors.push("Microsoft Edge WebView2 Runtime is not installed. Browser automation fallback will be degraded.".to_string());
            if severity != "Critical" {
                severity = "Warning".to_string();
            }
        }
    }

    // 6. Check WAL consistency / local store ontology integrity
    // We check if the WAL has unrecoverable corruption or syntax issues
    let raw_wal_path = engram_dir.join("raw").join(".wal");
    let mut wal_consistent = true;
    if raw_wal_path.exists() {
        if let Ok(content) = fs::read_to_string(&raw_wal_path) {
            // Check if WAL has corrupted contents (e.g. non-JSON lines or truncated text)
            for line in content.lines() {
                if !line.trim().is_empty() {
                    if serde_json::from_str::<serde_json::Value>(line).is_err() {
                        wal_consistent = false;
                        errors.push("Write-Ahead Log (WAL) contains unrecoverable JSON formatting corruption. Safe-Mode recovery is required.".to_string());
                        if severity != "Critical" {
                            severity = "IntegrityUncertain".to_string();
                        }
                        break;
                    }
                }
            }
        }
    }

    PreflightReport {
        sidecar_exists,
        write_access,
        port_available,
        vc_runtime_installed,
        webview2_installed,
        wal_consistent,
        severity,
        errors,
    }
}

fn check_registry_key(key: &str, value: &str) -> bool {
    #[cfg(target_os = "windows")]
    {
        let output = Command::new("reg")
            .args(["query", key, "/v", value])
            .output();

        match output {
            Ok(out) => out.status.success(),
            Err(_) => false,
        }
    }
    #[cfg(not(target_os = "windows"))]
    {
        let _ = (key, value);
        true
    }
}
