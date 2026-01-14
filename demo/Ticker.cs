using Godot;
using System;

public partial class Ticker : Node
{
	public int localAccumulator = 0;
	public static int staticAccumulator = 0;
	
	public override void _Process(double delta)
	{
		++localAccumulator;
		++staticAccumulator;
	}
}
