using Verse;
using RimWorld;
using HarmonyLib;
using System.Reflection;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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

    [HarmonyPatch(typeof(GizmoGridDrawer))]
    [HarmonyPatch("DrawGizmoGrid")]
    class Patch_GizmoGridDrawer
    {
        // DrawGizmoGrid is called twice so we only want to add each delegate once
        private static bool doOncePerFunctionCall = true;
       
        // We only want to grab the selected doors once per selection
        private static bool doOnce = true;

        // At least 1 door has been selected flag
        private static bool doorSelected = false;

        // The selected doors enumerator for passing into HOOD
        private static IEnumerator<Building> selectedDoorsEnumerator = null;

        private static void Prefix(IEnumerable<Gizmo> gizmos)
        {
            // Grab the selected doors once
            if (doOnce)
            {
                // If any doors have been selected, set the flag
                doorSelected = Find.Selector.SelectedObjects.Any(obj => AccessTools.Field(obj.GetType(), "holdOpenInt") != null);

                // Grab the selected doors if there is any
                if(doorSelected)
                    selectedDoorsEnumerator = GetSelectedDoors();

                doOnce = false;
            }

            // Only progress if at least 1 door has been selected
            if (doorSelected)
            {
                if (doOncePerFunctionCall)
                {
                    // Find and replace command toggle for Hold Open gizmo
                    foreach (Gizmo gizmo in gizmos)
                    {
                        // Replace delegate for Hold Open
                        if (gizmo is Command_Toggle && ((Command_Toggle)gizmo).Label.Equals("CommandToggleDoorHoldOpen".Translate()))
                        {
                            // Append to the toggle action
                            Action customToggleAction = delegate ()
                            {
                                // Call HOOD code
                                HoldOpenOpensDoors(selectedDoorsEnumerator.Current);

                                // Check if we are done this selection, if so, reset the do once flag for the next selection
                                if (selectedDoorsEnumerator.MoveNext() != true)
                                    doOnce = true;
                            };
                            ((Command_Toggle)gizmo).toggleAction += customToggleAction;
                        }
                    }

                    doOncePerFunctionCall = false;
                }
                else
                {
                    doOncePerFunctionCall = true;
                }
            }
        }

        // Get the selected doors enumerator, already set to the first position in the enumerator
        public static IEnumerator<Building> GetSelectedDoors()
        {
            IEnumerator<Building> enumerator = Find.Selector.SelectedObjects.Where(obj => AccessTools.Field(obj.GetType(), "holdOpenInt") != null).OfType<Building>().GetEnumerator();
            enumerator.MoveNext();

            return enumerator;
        }

        public static void HoldOpenOpensDoors(Building door)
        {
            // Get door name
            string doorName = door.def.defName;

            // Get type
            Type type = door.GetType();

            // No pawn interacting
            if (!PawnInteracting(door, type))
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
                    if (powerComp_Field != null) powerComp = (CompPowerTrader)powerComp_Field.GetValue(door); // We may not have a power comp
                    bool HoldOpen = false;
                    if (holdOpenInt_Field != null) HoldOpen = (bool)holdOpenInt_Field.GetValue(door);
                    bool Open = false;
                    if (Open_Property != null) Open = (bool)Open_Property.GetValue(door);
                    bool blockedOpenMomentary = false;
                    if (blockedOpenMomentary_Property != null) blockedOpenMomentary = (bool)blockedOpenMomentary_Property.GetValue(door);

                    // Check the door for power; true if door is receiving power or door does not need power
                    bool hasPower = powerComp != null ? powerComp.PowerOn : true;

                    string dissallowedDoors = "None";
                    if (HOODSettings.disallowedDoors.Count > 0) string.Join(",", HOODSettings.disallowedDoors);
                    L.Log(
                        "Door properties: "
                        + "Door name (" + doorName + ") - "
                        + "hasPower (" + hasPower + ") - "
                        + "HoldOpen (" + HoldOpen + ") - "
                        + "Open (" + Open + ") - "
                        + "blockedOpenMomentary (" + blockedOpenMomentary + ")"
                        );
                    L.Log(
                        "HOOD settings: "
                        + "checkPower (" + HOODSettings.checkPower + ") - "
                        + "disableOnDoors (" + HOODSettings.disableOnDoors + ") - "
                        + "dissallowedDoors (" + dissallowedDoors + ")"
                        );

                    // If user has disabled checking for power
                    if (!HOODSettings.checkPower || (HOODSettings.checkPower && hasPower))
                    {
                        bool openState = true; // Default to true to get around issue with multi-selected doors returning false for the very first door
                        int ticksUntilClose = 30;
                        int lastFriendlyTouch = 110;

                        if (!HoldOpen && Open && !blockedOpenMomentary)
                            openState = false;

                        L.Log("Open State: " + openState.ToString());

                        ActionDoor(door, type, openState, lastFriendlyTouch, ticksUntilClose);
                    }
                }
            }
        }

        // Returns true if the pawn is interacting with the door, used to stop race condition where door opens and closes forever
        private static bool PawnInteracting(Building building, Type type)
        {
            bool pawnInteracting = false;

            var holdOpenInt_Field = AccessTools.Field(type, "holdOpenInt");
            if (holdOpenInt_Field != null) // Avoid doing this on any structure other than one with a hold open field
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
            L.Log(
                "Building Properties To Set: "
                + "Open (" + open + ") - "
                + "Last Friendly Touch Tick (" + lastFriendlyTouch + ") - "
                + "Ticks Until Close (" + ticksUntilCloseDoor + ")");

            // Set fields
            var openInt_Field = AccessTools.Field(type, "openInt");
            if (openInt_Field != null) openInt_Field.SetValue(door, open);

            var lastFriendlyTouchTick_Field = AccessTools.Field(type, "lastFriendlyTouchTick");
            if (lastFriendlyTouchTick_Field != null) lastFriendlyTouchTick_Field.SetValue(door, lastFriendlyTouch);

            var ticksUntilClose_Field = AccessTools.Field(type, "ticksUntilClose");
            if (ticksUntilClose_Field != null) ticksUntilClose_Field.SetValue(door, ticksUntilCloseDoor);

            // Call OpenDoor so invisible doors do not get out of sync
            object[] parameters = new object[1];
            parameters[0] = ticksUntilCloseDoor;
            MethodInfo openDoorMethodInfo = AccessTools.Method(type, "DoorOpen");
            if (openDoorMethodInfo != null)
                openDoorMethodInfo.Invoke(door, parameters);

            if (openInt_Field != null && lastFriendlyTouchTick_Field != null && ticksUntilClose_Field != null)
                L.Log(
                    "Building Properties Set: "
                    + "Door name (" + door.def.defName + ") - "
                    + "Open (" + openInt_Field.GetValue(door) + ") - "
                    + "Last Friendly Touch Tick (" + lastFriendlyTouchTick_Field.GetValue(door) + ") - "
                    + "Ticks Until Close (" + ticksUntilClose_Field.GetValue(door) + ")"
                    );

            // Clear cache
            Find.CurrentMap.reachability.ClearCache();
        }
    }
}
