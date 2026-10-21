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
    public string currentState = "PlayerControl";
    public bool paused;
    public int currentSeqID, currentFloorID;
}

public class scrConductor
{
    public static scrConductor instance = new scrConductor();
    public double songposition_minusi;
    public double bpm = 120, crotchetAtStart = .5;
}

public static class RDC
{
    public static bool auto;
}
