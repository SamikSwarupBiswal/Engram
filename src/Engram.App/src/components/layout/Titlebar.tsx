interface TitlebarProps {
  apiOnline?: boolean | null;
}

export function Titlebar({ apiOnline }: TitlebarProps) {
  return (
    <div
      data-tauri-drag-region
      className="flex h-10 items-center justify-between bg-[#212121] px-3"
    >
      <div className="flex items-center gap-2" data-tauri-drag-region>
        <span className="text-[13px] font-medium text-[#b4b4b4]" data-tauri-drag-region>
          Engram
        </span>
      </div>
      <div className="flex items-center gap-2">
        {apiOnline !== null && (
          <div className="flex items-center gap-1.5">
            <div className={`h-1.5 w-1.5 rounded-full ${apiOnline ? "bg-emerald-500" : "bg-red-500"}`} />
            <span className="text-[10px] text-[#666]">{apiOnline ? "Connected" : "Offline"}</span>
          </div>
        )}
      </div>
    </div>
  );
}
