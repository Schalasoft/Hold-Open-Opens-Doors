using Verse;
using RimWorld;
using HarmonyLib;
using System.Reflection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HOOD
{
    // Main
    [StaticConstructorOnStartup]
    class Core
    {
        static Core()
        {
            // Force enable logging here (settings are not loaded at the point of patching)
            // Disable logging here for the guy that found it annoying
            //L.loggingEnabled = true;

            //L.Log("I am become L the logger of words!");
            //L.Log("Patching... hold my parka");
            var harmony = new Harmony("com.github.harmony.rimworld.mod.Hold_Open_Opens_Doors");

            //var harmony = HarmonyInstance.Create("com.github.harmony.rimworld.mod.Hold_Open_Opens_Doors");
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            //L.Log("Patching complete!");

            // End force enable logging
            L.loggingEnabled = false;
        }
    }

    // Main entry point for current map
    // Passthrough postfix to change effect of button delegate
    [HarmonyPatch(typeof(Building))]
    [HarmonyPatch("GetGizmos")]
    class Patch_Building : Building
    {
        // Postfix onto the GetGizmo method
        public static void Postfix(Building __instance)
        {
            string doorName = __instance.def.defName;
            Type type = __instance.GetType();
            bool inheritedDoor = false; // true if a mod like LinkedDoors inherits and overrides

            // Check base class in case of mods like LinkedDoors inheriting Building_Door (do before anything else or the objects will not contain the properties)
            if (__instance.GetType().BaseType == typeof(Building_Door))
            {
                type = __instance.GetType().BaseType;
                //L.Log("Got door base type, mod has inherited Building_Door: " + type);
                Building_Door baseDoor = __instance as Building_Door;
                if (baseDoor != null) type = baseDoor.GetType().BaseType;

                // Set instance to the type for calls to work
                //__instance = __instance.GetType().BaseType as Building_Door;
                inheritedDoor = true;
            }

            // No pawn interacting
            if (!PawnInteracting(__instance, type))
            {
                // If user has not disallowed the door
                if (!HOODSettings.disableOnDoors || (HOODSettings.disableOnDoors && !HOODSettings.disallowedDoors.Contains(doorName)))
                {
                    //L.Log("Getting props...");

                    // Get properties and fields for checks
                    var powerComp_Field = type.GetField("powerComp", BindingFlags.Public | BindingFlags.Instance);
                    var Open_Property = type.GetProperty("Open", BindingFlags.Public | BindingFlags.Instance);
                    //var ticksUntilClose_Field = type.GetField("ticksUntilClose", BindingFlags.NonPublic | BindingFlags.Instance);
                    var holdOpenInt_Field = type.GetField("holdOpenInt", BindingFlags.NonPublic | BindingFlags.Instance);
                    var blockedOpenMomentary_Property = type.GetProperty("BlockedOpenMomentary", BindingFlags.Public | BindingFlags.Instance);

                   // L.Log("Got prop refs");

                    // Get the values of the fields and properties
                    CompPowerTrader powerComp = null;
                    if(powerComp_Field != null) powerComp = (CompPowerTrader)powerComp_Field.GetValue(__instance); // We may not have a power comp
                    bool HoldOpen = false; // Assume false
                    if (holdOpenInt_Field != null) HoldOpen = (bool)holdOpenInt_Field.GetValue(__instance);
                    bool Open = false; // Assume false
                    if (Open_Property != null) Open = (bool)Open_Property.GetValue(__instance, new object[0]);
                    bool blockedOpenMomentary = false; // Assume it isn't
                    if(blockedOpenMomentary_Property != null) blockedOpenMomentary = (bool)blockedOpenMomentary_Property.GetValue(__instance, new object[0]);

                    //L.Log("Got prop vals");

                    // Check the door for power; true if door is receiving power or door does not need power
                    bool hasPower = powerComp != null ? powerComp.PowerOn : true;

                    L.Log("Props: " + hasPower + "-" + HoldOpen + "-" + Open + "-" + blockedOpenMomentary);

                    // If user has disabled checking for power
                    if (!HOODSettings.checkPower || (HOODSettings.checkPower && hasPower))
                    {
                        // Door should be opened/closed
                        if (HoldOpen && !Open)
                        {
                            ActionDoor(__instance, true, 110, 110, inheritedDoor);
                        }
                        else if (!HoldOpen && Open)
                        {
                            if (!blockedOpenMomentary)
                                ActionDoor(__instance, false, 110, 110, inheritedDoor); // Don't close the door if it is blocked open momentarily
                        }
                    }
                }
            }
        }

        // Returns true if the pawn is interacting with the door, used to stop race condition where door opens and closes forever
        private static bool PawnInteracting(Building building, Type type)
        {
            bool pawnInteracting = false;

            var holdOpenInt_Field = type.GetField("holdOpenInt", BindingFlags.NonPublic | BindingFlags.Instance);
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
        public static void ActionDoor(Building building, bool open, int lastFriendlyTouch, int ticksUntilClose, bool inheritedDoor)
        {
            L.Log("SetBuildingProperties - " + building.def.defName + " ---- " + open + " ---- " + lastFriendlyTouch);

            Type buildingType = building.GetType();
            if (inheritedDoor) buildingType = buildingType.BaseType; // Use base type

            // Flag the door to open/closed
            var openInt = buildingType.GetField("openInt", BindingFlags.NonPublic | BindingFlags.Instance);
            if (openInt != null) openInt.SetValue(building, open);

            // Set the last touched tick so the door reacts
            var lastFriendlyTouchTick = buildingType.GetField("lastFriendlyTouchTick", BindingFlags.NonPublic | BindingFlags.Instance);
            if (lastFriendlyTouchTick != null) lastFriendlyTouchTick.SetValue(building, lastFriendlyTouch);

            // Set ticks to close
            var ticksUntilCloseInt = buildingType.GetField("ticksUntilClose", BindingFlags.NonPublic | BindingFlags.Instance);
            if (ticksUntilCloseInt != null) lastFriendlyTouchTick.SetValue(building, ticksUntilClose);

            // can pawn pass
            /*Pawn pawn = Find.CurrentMap.mapPawns.AllPawns.First();
            var method = buildingType.GetMethod("PawnCanOpen", BindingFlags.Public | BindingFlags.Instance);
            method.Invoke(building, new object[] { pawn });
            //L.Log("method result: " + res);*/

            L.Log("ActionDoor - Actioned a door - " + openInt + " ---- " + lastFriendlyTouchTick + " ---- " + ticksUntilCloseInt);

            // Check for any invisible doors (will return all parts of the door, i.e. invisible doors within double and triple doors etc.)
            List<Thing> invisibleDoors = GetInvisibleDoors(building);

            L.Log("Invisible Door count: " + invisibleDoors.Count);

            // Action jecrells invisible doors, hidden behind his door handler
            foreach (Thing invisibleDoor in invisibleDoors)
            {
                ActionCustomDoor(invisibleDoor, open, lastFriendlyTouch, ticksUntilClose);
            }

            // Clear cache
            Find.CurrentMap.reachability.ClearCache();
        }

        // Gets the invisible doors associated with the passed in door (used for Jecrells mod)
        private static List<Thing> GetInvisibleDoors(Building building)
        {
            List<Thing> invisibleDoors = new List<Thing>();
            Thing invisibleDoor = null;
            string parentDoorId = null;

            IEnumerable<Thing> thingsAtCurrentCell = Find.CurrentMap.thingGrid.ThingsAt(building.Position);

            foreach (Thing thing in thingsAtCurrentCell)
            {
                bool done = false;
                //L.Log("Thing at cell: " + thing);
                if (thing.def.defName.Equals("HeronInvisibleDoor")) // Found it
                {
                    L.Log("Found invisible door: " + thing);
                    invisibleDoor = thing;

                    FieldInfo prop = null;
                    var dynamo = HOODSettings.DynamicCast(thing);
                    if (dynamo != null)
                        prop = dynamo.GetType().GetField("parentDoor", BindingFlags.NonPublic | BindingFlags.Instance);

                    parentDoorId = prop.GetValue(thing).ToString();
                    invisibleDoors.Add(invisibleDoor);

                    done = true;

                    L.Log("Parent Door ID: " + parentDoorId);
                }

                if (done) break;
            }

            // We have 1 invisible door, check if there is another one using the parent
            if (invisibleDoor != null)
            {
                // Check surrounding cells
                foreach (IntVec3 cell in building.CellsAdjacent8WayAndInside())
                {
                    // Check for fancy mod overrides on other things in this cell
                    // So the click gives us the door, not the invisible door, brilliant, we need to get the invisible door by getting the other "door" on this cell
                    IEnumerable<Thing> thingsAtCell = Find.CurrentMap.thingGrid.ThingsAt(cell);

                    foreach (Thing thing in thingsAtCell)
                    {
                        //L.Log("Thing at cell: " + thing);
                        if (thing.def.defName.Equals("HeronInvisibleDoor")) // Found an invisible door
                        {
                            FieldInfo prop = null;
                            var dynamo = HOODSettings.DynamicCast(thing);
                            if (dynamo != null)
                            {
                                prop = dynamo.GetType().GetField("parentDoor", BindingFlags.NonPublic | BindingFlags.Instance);
                            }

                            // Object has a parent door property, and it matches the parent door id we got from the initially clicked door
                            if (prop != null && prop.GetValue(thing).ToString().Equals(parentDoorId) 
                                && !thing.GetUniqueLoadID().Equals(invisibleDoor.GetUniqueLoadID())) // ensure we dont add the same door to the list
                            {
                                L.Log("Found another invisible door (door is larger than 1x1)");
                                invisibleDoors.Add(thing);// as Building_Door;
                            }
                        }
                    }

                    // doors with 2+ should be ignored, Jecrells only use up to 2 unvisible doors anyway
                    if (invisibleDoors.Count >= 2)
                        return invisibleDoors; // we're done here
                }
            }

            Pawn pawn = Find.CurrentMap.mapPawns.AllPawns.First();
            Building_Door door = building as Building_Door;


            return invisibleDoors;
        }

        // Action a custom door (one of Jecrells invisible doors)
        private static void ActionCustomDoor(Thing invisibleDoor, bool open, int lastFriendlyTouch, int ticksUntilClose)
        {
            var dynamo = HOODSettings.DynamicCast(invisibleDoor); // cast the invisible doors thing with fancy dynamo
            if (dynamo != null)
            {
                // This is a handler, so get the base type
                Type baseType = dynamo.GetType().BaseType;

                // Set the hidden fields
                var invisibleOpenInt = baseType.GetField("openInt", BindingFlags.NonPublic | BindingFlags.Instance);
                if (invisibleOpenInt != null)
                {
                    //L.Log("Invisible door open state (pre): " + invisibleOpenInt.GetValue(invisibleDoor));
                    invisibleOpenInt.SetValue(invisibleDoor, open);
                    //L.Log("Invisible door open state(post): " + invisibleOpenInt.GetValue(invisibleDoor));
                }

                var invisiblelastFriendlyTouchTick = baseType.GetField("lastFriendlyTouchTick", BindingFlags.NonPublic | BindingFlags.Instance);
                if (invisiblelastFriendlyTouchTick != null) invisiblelastFriendlyTouchTick.SetValue(invisibleDoor, lastFriendlyTouch);

                // TicksUntilClose to determine when to close the door
                var invisibleTicksUntilClose = baseType.GetField("ticksUntilClose", BindingFlags.NonPublic | BindingFlags.Instance);
                if (invisibleTicksUntilClose != null) invisibleTicksUntilClose.SetValue(invisibleDoor, ticksUntilClose);

                // Check what the state of the invisible door variables are
                L.Log("Actioned an invisible door oooOOoOOOoooOooo - " + invisibleOpenInt + " ---- " + invisiblelastFriendlyTouchTick + " ---- " + invisibleTicksUntilClose);
            }
        }
    }
}
