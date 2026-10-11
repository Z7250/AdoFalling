// A separate assembly with the game's assembly/type names exercises reflection hooks.
using System;

public class scrFloor
{
    public int seqID;
    public double entryangle;
    public string grade = "Perfect";
}

public class scrPlayer
{
    public Action<scrFloor> onHit;
}

public class scrController
{
    public static scrController instance = new scrController();
    public scrPlayer playerOne = new scrPlayer();
}

public static class RDC
{
    public static bool auto;
}
