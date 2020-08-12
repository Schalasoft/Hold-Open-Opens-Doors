using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

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

    // Patches the Hold Open door gizmo to also close/open doors
    [HarmonyPatch]
    class Patch_Building_GetGizmos
    {
        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var targetMethods = GenTypes.AllSubclasses(typeof(Building))
                .Where(type => AccessTools.Field(type, "holdOpenInt") != null)
                .Select(type => AccessTools.Method(type, nameof(Building.GetGizmos)).GetDeclaredMember())
                .Distinct();
            //Log.Message("HOOD :: Patch_Building_GetGizmos: " + targetMethods.Join(m => m.DeclaringType.ToString()));
            return targetMethods;
        }

        // Passthrough postfix patch
        [HarmonyPostfix]
        private static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> gizmos, Building __instance)
        {
            foreach (var gizmo in gizmos)
            {
                var yieldGizmo = gizmo;
                if (gizmo is Command_Toggle toggle && toggle.Label == holdOpenLabel)
                {
                    if (!(toggle is CustomHoldOpenToggle))
                        yieldGizmo = new CustomHoldOpenToggle(toggle, __instance);
                }
                yield return yieldGizmo;
            }
        }

        private static readonly string holdOpenLabel = "CommandToggleDoorHoldOpen".Translate();
    }

    // Exists as its own type so that above patch can easily detect whether the custom toggle action is already added
    class CustomHoldOpenToggle : Command_Toggle
    {
        private readonly Building door;

        public CustomHoldOpenToggle(Command_Toggle orig, Building door)
        {
            defaultLabel = orig.defaultLabel;
            defaultDesc = orig.defaultDesc;
            hotKey = orig.hotKey;
            icon = orig.icon;
            isActive = orig.isActive;
            toggleAction = orig.toggleAction + HoldOpenOpensDoors;
            this.door = door;
        }

        private void HoldOpenOpensDoors()
        {
            string doorName = door.def.defName;
            Type type = door.GetType();

            // If user has not disallowed the door
            if (!HOODSettings.disableOnDoors || (HOODSettings.disableOnDoors && !HOODSettings.disallowedDoors.Contains(doorName)))
            {
                // Get the values of the fields and properties
                // Fields and properties could be cached for performance, but this is not perf-critical code, so not bothering to do so
                CompPowerTrader powerComp = (CompPowerTrader)AccessTools.Field(type, "powerComp")?.GetValue(door);
                bool HoldOpen = (bool)(AccessTools.Field(type, "holdOpenInt")?.GetValue(door) ?? false);
                bool Open = (bool)(AccessTools.Property(type, "Open")?.GetValue(door) ?? false);

                // Check the door for power; true if door is receiving power or door does not need power
                bool hasPower = powerComp == null || powerComp.PowerOn;

                string disallowedDoors = "None";
                if (HOODSettings.disallowedDoors.Count > 0) string.Join(",", HOODSettings.disallowedDoors);
                L.Log(
                    "Door properties: "
                    + "Door name (" + doorName + ") - "
                    + "hasPower (" + hasPower + ") - "
                    + "HoldOpen (" + HoldOpen + ") - "
                    + "Open (" + Open + ") - "
                    );
                L.Log(
                    "HOOD settings: "
                    + "checkPower (" + HOODSettings.checkPower + ") - "
                    + "disableOnDoors (" + HOODSettings.disableOnDoors + ") - "
                    + "disallowedDoors (" + disallowedDoors + ")"
                    );

                // If user has disabled checking for power
                if (!HOODSettings.checkPower || (HOODSettings.checkPower && hasPower))
                {
                    bool targetOpenState = HoldOpen || !Open;
                    // Delay executing action until all gizmo actions across all selected objects have processed
                    // as a workaround for Linkable Doors changing open state for linked doors immediately
                    LongEventHandler.ExecuteWhenFinished(() => ActionDoor(door, door.GetType(), targetOpenState));
                }
            }
        }

        // Takes a Building to allow for the possibility of overriding of Building_Door and mods not inheriting from Building_Door
        private static void ActionDoor(Building door, Type type, bool targetOpenState)
        {
            // Fields and properties could be cached for performance, but this is not perf-critical code, so not bothering to do so
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
