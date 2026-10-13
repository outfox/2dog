"""Forward dotnet build commands, applying the machine's optional worker cap."""
import os
import re
import subprocess
import sys


def get_job_limit():
    value = os.environ.get("TWODOG_MAX_BUILD_JOBS")
    if value is None and sys.platform == "win32":
        # Existing terminals may predate a user-environment update.
        import winreg
        try:
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, "Environment") as key:
                value, _ = winreg.QueryValueEx(key, "TWODOG_MAX_BUILD_JOBS")
        except FileNotFoundError:
            pass
    if value is None:
        return None
    limit = int(value)
    if limit < 1:
        raise ValueError("TWODOG_MAX_BUILD_JOBS must be a positive integer")
    return min(limit, os.cpu_count() or 1)


def main():
    command = ["dotnet", *sys.argv[1:]]
    try:
        limit = get_job_limit()
    except ValueError as error:
        print(error, file=sys.stderr)
        return 2
    environment = os.environ.copy()
    if limit is not None:
        for argument in sys.argv[1:]:
            explicit = re.fullmatch(r"[-/](?:m|maxcpucount):(\d+)", argument, re.IGNORECASE)
            if explicit:
                limit = min(limit, int(explicit.group(1)))
        command.append(f"-maxcpucount:{limit}")
        environment["DOTNET_PROCESSOR_COUNT"] = str(limit)
    return subprocess.call(command, env=environment)


if __name__ == "__main__":
    sys.exit(main())
