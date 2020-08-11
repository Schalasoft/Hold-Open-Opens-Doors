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

    // If the user left clicks, check for selected door(s) and get them
    [HarmonyPatch(typeof(Selector))]
    [HarmonyPatch("Select")]
    class Patch_Selector
    {
        private static void Postfix()
        {
            Patch_GizmoGridDrawer.doOncePerSelect = true;
        }
    }

    [HarmonyPatch(typeof(GizmoGridDrawer))]
    [HarmonyPatch("DrawGizmoGrid")]
    class Patch_GizmoGridDrawer
    {
        // DrawGizmoGrid is called twice so we only want to add each delegate once
        private static bool doOncePerFunctionCall = true;

        // We only want to grab the selected doors once per selection
        public static bool doOncePerSelect = true;

        // At least 1 door has been selected flag
        private static bool doorSelected = false;

        // The selected doors enumerator for passing into HOOD
        private static IEnumerator<Building> selectedDoorsEnumerator = null;

        //
        private static string holdOpenLabel = "CommandToggleDoorHoldOpen".Translate();

        //
        private static void Prefix(IEnumerable<Gizmo> gizmos)
        {
            if (doOncePerSelect)
            {
                // If any doors have been selected, set the flag
                doorSelected = Find.Selector.SelectedObjects.Any(obj => AccessTools.Field(obj.GetType(), "holdOpenInt") != null);

                // Grab the selected doors if there is any
                if (doorSelected)
                    selectedDoorsEnumerator = GetSelectedDoors();

                L.Log($"Any door selected: {doorSelected}");

                doOncePerSelect = false;
                doOncePerFunctionCall = true;
            }

            // Only progress if at least 1 door has been selected
            if (doorSelected)
            {
                if (doOncePerFunctionCall)
                    UpdateGizmos(gizmos);

                doOncePerFunctionCall = !doOncePerFunctionCall;
            }
        }

        //
        private static void UpdateGizmos(IEnumerable<Gizmo> gizmos)
        {
            // Find and replace command toggle for Hold Open gizmo
            foreach (Gizmo gizmo in gizmos)
            {
                // Replace delegate for Hold Open
                if (gizmo is Command_Toggle && ((Command_Toggle)gizmo).Label.Equals(holdOpenLabel))
                {
                    // Append to the toggle action
                    Action customToggleAction = delegate ()
                    {
                        // Call HOOD code
                        HoldOpenOpensDoors(selectedDoorsEnumerator.Current);

                        // Move to the next door
                        if (selectedDoorsEnumerator.MoveNext() == false)
                        {
                            // If we reach the end, go back to the start of the enumeration by reseting it
                            selectedDoorsEnumerator = GetSelectedDoors();
                        }
                    };
                    ((Command_Toggle)gizmo).toggleAction += customToggleAction;
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

        //
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
                        if (!HoldOpen && Open && !blockedOpenMomentary)
                            openState = false;
                        ActionDoor(door, type, openState);
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
        public static void ActionDoor(Building door, Type type, bool targetOpenState)
        {
            var openInt_Field = AccessTools.Field(type, "openInt");
            var ticksUntilClose_Field = AccessTools.Field(type, "ticksUntilClose");

            L.Log("Building Properties To Set: Open (" + (openInt_Field?.GetValue(door) ?? "n/a") + " => " + targetOpenState + ")");

            // Call DoorOpen/DoorTryClose rather than set openInt field (so invisible doors in Doors Expanded do not get out of sync)
            MethodInfo method;
            if (targetOpenState)
            {
                method = AccessTools.Method(type, "DoorOpen");
                method?.Invoke(door, new object[] { Type.Missing });
            }
            else
            {
                method = AccessTools.Method(type, "DoorTryClose");
                var closeSuccess = (bool)(method?.Invoke(door, Array.Empty<object>()) ?? false);
                // If couldn't close door, try setting door ticksUntilClose field to 1 to ensure door closes ASAP
                // (note: if pawn is currently in the door, ticksUntilClose will reset to 110 on the next tick)
                if (!closeSuccess)
                    ticksUntilClose_Field?.SetValue(door, 1);
            }

            L.Log(
                "Building Properties Set: "
                + "Door name (" + door.def.defName + ") - "
                + "Open (" + (openInt_Field?.GetValue(door) ?? "n/a") + ") - "
                + "Ticks Until Close (" + (ticksUntilClose_Field?.GetValue(door) ?? "n/a") + ") - "
                + "Method (" + method + ")"
                );

            // Clear cache
            Find.CurrentMap.reachability.ClearCache();
        }
    }
}
