#!/usr/bin/env python3
"""Install an .app bundle on a booted iOS Simulator and require its C# scene marker on the launched app's console."""
import argparse
from pathlib import Path
from queue import Empty, Queue
import subprocess
import sys
from threading import Thread
import time


def simctl(args, *command):
    return [args.xcrun, "simctl", *command]


def terminate(args, timeout):
    try:
        result = subprocess.run(simctl(args, "terminate", args.device, args.bundle),
                                check=False, capture_output=True, text=True, timeout=timeout)
    except subprocess.TimeoutExpired:
        return
    if result.returncode and "found nothing to terminate" in result.stderr:
        return
    print(result.stdout, end="")
    print(result.stderr, end="", file=sys.stderr)


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
    try:
        subprocess.run(simctl(args, "install", args.device, str(args.app.resolve())), check=True, timeout=args.timeout)
    except subprocess.TimeoutExpired:
        raise SystemExit("iOS Simulator app installation timed out")
    # A stale instance would keep the console attached; simctl itself can hang when the Simulator is overloaded.
    terminate(args, timeout=30)
    # --console attaches the app's stdout and stderr to this process until it exits or is terminated.
    launch = subprocess.Popen(simctl(args, "launch", "--console", args.device, args.bundle),
                              stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    deadline = time.monotonic() + args.timeout
    logs = []
    lines = Queue()

    def read_console():
        for line in iter(launch.stdout.readline, ""):
            lines.put(line)
        lines.put(None)

    Thread(target=read_console, daemon=True).start()
    try:
        while True:
            try:
                line = lines.get(timeout=max(0, deadline - time.monotonic()))
            except Empty:
                break
            if line is None:
                break
            logs.append(line)
            if args.marker in line:
                print(args.marker)
                return
        print("".join(logs))
        raise SystemExit("iOS smoke marker not observed before timeout")
    finally:
        # Cleanup has a separate short bound and must not hide a smoke timeout.
        terminate(args, timeout=10)
        launch.kill()


if __name__ == "__main__":
    main()
