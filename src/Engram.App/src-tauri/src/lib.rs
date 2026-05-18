use std::sync::Mutex;
use tauri::{
    menu::{Menu, MenuItem},
    tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent},
    Emitter, Manager,
};
use tauri_plugin_shell::{
    process::{CommandChild, CommandEvent},
    ShellExt,
};

struct SidecarState {
    pid: Mutex<Option<u32>>,
    child: Mutex<Option<CommandChild>>,
}

#[tauri::command]
fn get_app_version() -> String {
    "1.0.0".to_string()
}

#[tauri::command]
fn get_capture_status() -> serde_json::Value {
    serde_json::json!({
        "isCapturing": true,
        "eventsCaptured": 0,
        "eventsDropped": 0,
        "tier": "free",
        "powerMode": "eco"
    })
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_shell::init())
        .manage(SidecarState {
            pid: Mutex::new(None),
            child: Mutex::new(None),
        })
        .invoke_handler(tauri::generate_handler![
            get_app_version,
            get_capture_status
        ])
        .setup(|app| {
            // ── Spawn .NET API sidecar ──
            // The sidecar binary is bundled at src-tauri/sidecar/engram-api-{target_triple}
            // Tauri resolves it via externalBin in tauri.conf.json
            let sidecar = app.shell().sidecar("engram-api").expect("sidecar config missing");
            let (mut rx, child) = sidecar
                .args(["--urls", "http://127.0.0.1:5000"])
                .spawn()
                .expect("failed to spawn .NET sidecar");
            let child_pid = child.pid();

            // Store PID for cleanup
            {
                let state = app.state::<SidecarState>();
                let mut pid = state.pid.lock().unwrap();
                *pid = Some(child_pid);

                let mut child_handle = state.child.lock().unwrap();
                *child_handle = Some(child);
            }

            // Keep child handle alive — Tauri manages lifecycle
            // The sidecar is killed when the app exits
            tauri::async_runtime::spawn(async move {
                while let Some(event) = rx.recv().await {
                    match event {
                        CommandEvent::Stdout(line) => {
                            println!("engram-api: {}", String::from_utf8_lossy(&line));
                        }
                        CommandEvent::Stderr(line) => {
                            eprintln!("engram-api: {}", String::from_utf8_lossy(&line));
                        }
                        CommandEvent::Terminated(payload) => {
                            eprintln!("engram-api exited: {:?}", payload.code);
                            break;
                        }
                        CommandEvent::Error(error) => {
                            eprintln!("engram-api error: {error}");
                        }
                        _ => {}
                    }
                }
            });

            // ── Build system tray menu ──
            let pause_item = MenuItem::with_id(app, "pause", "Pause Capture", true, None::<&str>)?;
            let resume_item = MenuItem::with_id(app, "resume", "Resume Capture", true, None::<&str>)?;
            let show_item = MenuItem::with_id(app, "show", "Show Window", true, None::<&str>)?;
            let quit_item = MenuItem::with_id(app, "quit", "Quit Engram", true, None::<&str>)?;

            let menu = Menu::with_items(
                app,
                &[&pause_item, &resume_item, &show_item, &quit_item],
            )?;

            let _tray = TrayIconBuilder::new()
                .icon(app.default_window_icon().unwrap().clone())
                .menu(&menu)
                .tooltip("Engram — semantic memory")
                .on_menu_event(move |app, event| {
                    match event.id.as_ref() {
                        "pause" => {
                            let _ = app.emit("capture-paused", ());
                        }
                        "resume" => {
                            let _ = app.emit("capture-resumed", ());
                        }
                        "show" => {
                            if let Some(window) = app.get_webview_window("main") {
                                let _ = window.show();
                                let _ = window.set_focus();
                            }
                        }
                        "quit" => {
                            app.exit(0);
                        }
                        _ => {}
                    }
                })
                .on_tray_icon_event(|tray, event| {
                    if let TrayIconEvent::Click {
                        button: MouseButton::Left,
                        button_state: MouseButtonState::Up,
                        ..
                    } = event
                    {
                        let app = tray.app_handle();
                        if let Some(window) = app.get_webview_window("main") {
                            let _ = window.show();
                            let _ = window.set_focus();
                        }
                    }
                })
                .build(app)?;

            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
