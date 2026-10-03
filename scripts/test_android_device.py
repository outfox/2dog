#!/usr/bin/env python3
"""Install an APK and require its C# scene marker in logcat (one explicitly selected device)."""
import argparse
from pathlib import Path
import subprocess
import time


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("apk", type=Path)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--package", default="dev.twodog.smoke")
    parser.add_argument("--marker", default="2DOG_ANDROID_CSHARP_SMOKE_PASSED")
    parser.add_argument("--timeout", type=int, default=90)
    parser.add_argument("--adb", default="adb")
    args = parser.parse_args()
    if args.timeout <= 0:
        parser.error("--timeout must be greater than zero")
    adb = [args.adb, "-s", args.serial]
    subprocess.run([*adb, "install", "-r", str(args.apk.resolve())], check=True)
    subprocess.run([*adb, "shell", "am", "force-stop", args.package], check=True)
    # Restrict reads to this new process; leave the device's existing log buffer intact.
    subprocess.run([*adb, "shell", "monkey", "-p", args.package, "-c", "android.intent.category.LAUNCHER", "1"], check=True)
    deadline = time.monotonic() + args.timeout
    logs = ""
    try:
        while True:
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                break
            try:
                pid = subprocess.run([*adb, "shell", "pidof", args.package], capture_output=True, text=True,
                                     timeout=min(10, remaining)).stdout.strip()
                if pid:
                    remaining = deadline - time.monotonic()
                    if remaining <= 0:
                        break
                    logs = subprocess.run([*adb, "logcat", "-d", "--pid", pid.split()[0]],
                                          capture_output=True, text=True, check=True,
                                          timeout=min(15, remaining)).stdout
                    if args.marker in logs:
                        print(args.marker)
                        return
            except subprocess.TimeoutExpired:
                # An adb read may use up this poll's remaining time budget.
                pass
            remaining = deadline - time.monotonic()
            if remaining > 0:
                time.sleep(min(1, remaining))
        print(logs)
        raise SystemExit("Android smoke marker not observed before timeout")
    finally:
        # Cleanup has a separate short bound and must not hide a smoke timeout.
        try:
            subprocess.run([*adb, "shell", "am", "force-stop", args.package], check=False, timeout=5)
        except subprocess.TimeoutExpired:
            pass


if __name__ == "__main__":
    main()
