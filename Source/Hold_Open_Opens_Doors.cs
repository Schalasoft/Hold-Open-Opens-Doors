using Verse;
using RimWorld;
using HarmonyLib;
using System.Reflection;
using System;
using System.Linq;

namespace HOOD
{
    // Main
    [StaticConstructorOnStartup]
    class Core
    {
        static Core()
        {
            var harmony = new Harmony("com.github.harmony.rimworld.mod.Hold_Open_Opens_Doors");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }
    }

    // Main entry point for current map
    // Passthrough postfix to change effect of button delegate
    [HarmonyPatch(typeof(Building))]
    [HarmonyPatch("GetGizmos")]
    class Patch_Building : Building
    {
        private static Building previousDoor = null; // For settings per door
        private static string previousLog1 = "";     // For action door
        private static string previousLog2 = "";     // For action door


        // Postfix onto the GetGizmo method
        public static void Postfix(Building __instance)
        {
            string doorName = __instance.def.defName;
            Type type = __instance.GetType();

            // No pawn interacting
            if (!PawnInteracting(__instance, type))
            {
                // If user has not disallowed the door
                if (!HOODSettings.disableOnDoors || (HOODSettings.disableOnDoors && !HOODSettings.disallowedDoors.Contains(doorName)))
                {
                    // Get properties and fields for checks
                    var powerComp_Field = AccessTools.Field(type, "powerComp");
                    var Open_Property = AccessTools.Property(type, "Open");
                    var holdOpenInt_Field = AccessTools.Field(type, "holdOpenInt");
                    var blockedOpenMomentary_Property = AccessTools.Property(type, "BlockedOpenMomentary");

                    // Get the values of the fields and properties
                    CompPowerTrader powerComp = null;
                    if(powerComp_Field != null) powerComp = (CompPowerTrader)powerComp_Field.GetValue(__instance); // We may not have a power comp
                    bool HoldOpen = false; // Assume false
                    if (holdOpenInt_Field != null) HoldOpen = (bool)holdOpenInt_Field.GetValue(__instance);
                    bool Open = false; // Assume false
                    if (Open_Property != null) Open = (bool)Open_Property.GetValue(__instance);
                    bool blockedOpenMomentary = false; // Assume it isn't
                    if(blockedOpenMomentary_Property != null) blockedOpenMomentary = (bool)blockedOpenMomentary_Property.GetValue(__instance);

                    // Check the door for power; true if door is receiving power or door does not need power
                    bool hasPower = powerComp != null ? powerComp.PowerOn : true;

                    // Door is null or different than the last, update previous door
                    if (previousDoor == null || !previousDoor.Equals(__instance))
                    {
                        // Log once on selecting a door
                        L.Log("Door properties: " + "hasPower(" + hasPower + ") - " + "HoldOpen(" + HoldOpen + ") - "
                                + "Open(" + Open + ") - " + "blockedOpenMomentary(" + blockedOpenMomentary + ") - " + "type(" + type + ")"
                                + "\n"
                                + "HOOD settings: " + "checkPower(" + HOODSettings.checkPower + ") - " + "disableOnDoors (" + HOODSettings.disableOnDoors + ") - "
                                + "dissallowedDoors (" + string.Join(",", HOODSettings.disallowedDoors) + ")");

                        previousDoor = __instance;
                    }

                    // If user has disabled checking for power
                    if (!HOODSettings.checkPower || (HOODSettings.checkPower && hasPower))
                    {
                        bool? openState = null;
                        int ticksUntilClose = 30;
                        int lastFriendlyTouch = 110;

                        // Door should be opened/closed
                        if (HoldOpen && !Open)
                            openState = true;
                        else if (!HoldOpen && Open && !blockedOpenMomentary)
                            openState = false;

                        if (openState.HasValue)
                            ActionDoor(__instance, type, openState.Value, lastFriendlyTouch, ticksUntilClose);
                    }
                }
            }
        }

        // Returns true if the pawn is interacting with the door, used to stop race condition where door opens and closes forever
        private static bool PawnInteracting(Building building, Type type)
        {
            bool pawnInteracting = false;

            var holdOpenInt_Field = AccessTools.Field(type, "holdOpenInt");
            if (holdOpenInt_Field != null) // Avoid doing this on any structure other than a one with a hold open field
            {
                // Check adjacent cells for pawns trying to open the door
                foreach (IntVec3 position in building.CellsAdjacent8WayAndInside())
                {
                    // Get each thing in cell
                    foreach (Pawn pawn in Find.CurrentMap.thingGrid.ThingsAt(position).Where(t => t != null && t is Pawn))
                    {
                        if (pawn.pather != null
                            && pawn.pather.Destination != null
                            && pawn.pather.nextCell != null
                            && (pawn.pather.Destination.Equals(building.Position) || pawn.pather.nextCell.Equals(building.Position)))
                            // This may need adjusted to be for exactly what door the pawn is trying to open/close - this may need refactored
                            {
                                return true;
                            }
                    }
                }
            }

            return pawnInteracting;
        }

        // Takes a building to allow for the possibility of overriding of Building_Door and mods not inheriting from Building_Door
        public static void ActionDoor(Building door, Type type, bool open, int lastFriendlyTouch, int ticksUntilCloseDoor)
        {
            // Not using the button press of hold open so need to stop excess logging by checking the previous values, doesn't work for consecutive open/closes on same door which is not ideal (look at this later)
            string log1 = "SetBuildingProperties - " + door.def.defName + " ---- " + open + " ---- " + lastFriendlyTouch;
            if (!previousLog1.Equals(log1))
                L.Log(log1);
            previousLog1 = log1;

            // Set fields
            var openInt = AccessTools.Field(type, "openInt");
            if (openInt != null) openInt.SetValue(door, open);

            var lastFriendlyTouchTick = AccessTools.Field(type, "lastFriendlyTouchTick");
            if (lastFriendlyTouchTick != null) lastFriendlyTouchTick.SetValue(door, lastFriendlyTouch);

            var ticksUntilClose = AccessTools.Field(type, "ticksUntilClose");
            if (ticksUntilClose != null) ticksUntilClose.SetValue(door, ticksUntilCloseDoor);

            string log2 = "ActionDoor - Actioned a door - " + openInt + " ---- " + lastFriendlyTouchTick + " ---- " + ticksUntilClose;
            if (!previousLog2.Equals(log2))
                L.Log(log2);
            previousLog2 = log2;

            // Clear cache
            Find.CurrentMap.reachability.ClearCache();
        }
    }
}
