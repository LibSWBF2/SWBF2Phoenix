using System;
using System.Collections.Generic;
using UnityEngine;
using LibSWBF2.Wrappers;


public class PhxGrenade : PhxGenericWeapon<PhxGrenade.ClassProperties>
{
    public new class ClassProperties : PhxGenericWeapon<ClassProperties>.ClassProperties
    {
    	// Dont know what goes here for sure yet, grenade anim bank is not used
    }

    public override void Init()
    {
        base.Init();
    }
}