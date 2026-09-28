extends RefCounted
## Statically typed TwoDogProbe calls, kept out of gdextension_probe.gd: parsing needs the class, which only exists
## once the extension loaded. Returns the description, or "" when the typed add() is wrong.


static func describe() -> String:
	var probe := TwoDogProbe.new()
	if probe.add(20, 22) != 42:
		return ""
	return probe.describe()
