# Select-and-Concat

A fast, lightweight Windows Explorer context-menu extension that concatenates text files and directory structures directly into a fenced Markdown payload on your clipboard — ready for LLM prompts.

## Features

- **Explorer Integration**: Select files or folders in Windows Explorer, right-click, and click **"Copy contents for LLM"**.
- **Multi-Select Coordination**: Seamlessly aggregates paths across Explorer instances using non-blocking inter-process communication.
- **Smart Markdown Fencing**: Dynamically adjusts fence backticks to cleanly enclose code blocks inside Markdown files without syntax breaking.
- **Robust Text Decoding**: Automatically handles UTF-8 / UTF-16 encodings and handles mid-codepoint byte boundary truncations cleanly.
- **Credential Protection**: Automatically excludes secret files (`.env`, `.pem`, `.key`, `id_rsa`, `credentials.json`) to prevent accidental leaks to LLMs.
- **Noise Filtering**: Automatically ignores dependency and build folders (`node_modules`, `.git`, `bin`, `obj`, `dist`, `build`, etc.) during directory expansion.
- **Linear-Time Payload Building**: Fast $O(N)$ execution time with automatic size limits.
- **Visual Feedback**: Displays a system tray notification upon successful copying.

## Building and Installing

### Prerequisites
- .NET 8.0 SDK
- WiX Toolset v5 (for installer build)

### Build Solution
```powershell
dotnet build SelectAndConcat.sln -c Release
```

### Run Tests
```powershell
dotnet test SelectAndConcat.sln -c Release
```

### Build & Install MSI Package
```powershell
dotnet build installer/SelectAndConcat.Installer.wixproj -c Release
msiexec /i installer/bin/x64/Release/SelectAndConcat.Installer.msi /qn
```

## License

MIT
