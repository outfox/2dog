#!/usr/bin/env python3
"""Install an .app bundle on a booted iOS Simulator and require its C# scene marker on the launched app's console."""
import argparse
from pathlib import Path
import subprocess
import time


def simctl(args, *command):
    return [args.xcrun, "simctl", *command]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("app", type=Path)
    parser.add_argument("--device", default="booted", help="Simulator UDID or name; 'booted' uses the running one")
    parser.add_argument("--bundle", default="dev.twodog.smoke")
    parser.add_argument("--marker", default="2DOG_IOS_CSHARP_SMOKE_PASSED")
    parser.add_argument("--timeout", type=int, default=120)
    parser.add_argument("--xcrun", default="xcrun")
    args = parser.parse_args()
    if args.timeout <= 0:
        parser.error("--timeout must be greater than zero")
    if not args.marker:
        parser.error("--marker must not be empty: any output would contain it")
    subprocess.run(simctl(args, "install", args.device, str(args.app.resolve())), check=True)
    # A stale instance would keep the console attached; simctl itself can hang when the Simulator is overloaded.
    try:
        subprocess.run(simctl(args, "terminate", args.device, args.bundle), check=False, timeout=30)
    except subprocess.TimeoutExpired:
        pass
    # --console attaches the app's stdout and stderr to this process until it exits or is terminated.
    launch = subprocess.Popen(simctl(args, "launch", "--console", args.device, args.bundle),
                              stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    deadline = time.monotonic() + args.timeout
    logs = []
    try:
        for line in iter(launch.stdout.readline, ""):
            logs.append(line)
            if args.marker in line:
                print(args.marker)
                return
            if time.monotonic() >= deadline:
                break
        print("".join(logs))
        raise SystemExit("iOS smoke marker not observed before timeout")
    finally:
        # Cleanup has a separate short bound and must not hide a smoke timeout.
        try:
            subprocess.run(simctl(args, "terminate", args.device, args.bundle), check=False, timeout=10)
        except subprocess.TimeoutExpired:
            pass
        launch.kill()


if __name__ == "__main__":
    main()
