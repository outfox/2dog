# MTP host contracts

Run against locally packed packages:

```text
uv run python tests/mtp/test_hosts.py --configuration Debug --precision single
```

The suite exercises xUnit's MTP v2 runner, NUnit's official MTP v2 bridge,
and the embedded GdUnit MTP framework. It checks discovery, stable IDs,
parameter-row selection, empty selections, results, native warnings, native
.NET 10 `dotnet test`, cancellation and Godot owner-thread teardown.

The minimal game has a main scene because Godot validates its presence
before the fixture can bypass loading it. Normal resource import generates
the script cache; tests never instantiate that scene.

NUnit adapter 6.3.1 does not relay MTP session cancellation to NUnit's test
token, and its forced stop skips fixture teardown. The NUnit probe uses
NUnit's `CancelAfter` without canceling the MTP session. The marker asserts
the token was observed and native shutdown completed on the owner thread.
This verifies cooperative test cancellation; safe MTP session cancellation
remains an upstream NUnit adapter limitation. xUnit and GdUnit probes observe runner cancellation directly.

Artifacts, including console and native logs and fixture thread markers,
are under `tests/mtp/bin` and `tests/gdunit-mtp/bin/contracts`.
