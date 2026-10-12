using System;
using UnityEngine;

public interface IStatBlock 
{
    int GetTotal();
}

[Serializable]
public class DragonStats : IStatBlock
{
    public int str;
    public int mana;

    public int GetTotal() => str + mana;
}
