#!/usr/bin/env python3
"""Install the smoke APK and require its C# scene marker in logcat (one explicitly selected device)."""
import argparse
from pathlib import Path
import subprocess
import time


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("apk", type=Path)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--package", default="dev.twodog.smoke")
    parser.add_argument("--timeout", type=int, default=90)
    parser.add_argument("--adb", default="adb")
    args = parser.parse_args()
    adb = [args.adb, "-s", args.serial]
    subprocess.run([*adb, "install", "-r", str(args.apk.resolve())], check=True)
    subprocess.run([*adb, "shell", "am", "force-stop", args.package], check=True)
    # Restrict reads to this new process; leave the device's existing log buffer intact.
    subprocess.run([*adb, "shell", "monkey", "-p", args.package, "-c", "android.intent.category.LAUNCHER", "1"], check=True)
    deadline = time.monotonic() + args.timeout
    logs = ""
    try:
        while time.monotonic() < deadline:
            pid = subprocess.run([*adb, "shell", "pidof", args.package], capture_output=True, text=True, timeout=10).stdout.strip()
            if pid:
                logs = subprocess.run([*adb, "logcat", "-d", "--pid", pid.split()[0]],
                                      capture_output=True, text=True, check=True, timeout=15).stdout
                if "2DOG_ANDROID_CSHARP_SMOKE_PASSED" in logs:
                    print("2DOG_ANDROID_CSHARP_SMOKE_PASSED")
                    return
            time.sleep(1)
        print(logs)
        raise SystemExit("Android smoke marker not observed before timeout")
    finally:
        subprocess.run([*adb, "shell", "am", "force-stop", args.package], check=False)


if __name__ == "__main__":
    main()
