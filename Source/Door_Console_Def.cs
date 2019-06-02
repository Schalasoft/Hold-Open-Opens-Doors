using Verse;
using RimWorld;
using Harmony;
using System.Reflection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HOOD
{
    public class Door_Console_Def : ThingDef
    {
        public CompPower powerComp;
        public Map map;

        public Door_Console_Def()
        {
            this.thingClass = typeof(Building);
        }
    }
}