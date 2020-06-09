using HugsLib;
using HugsLib.Settings;
using Verse;
using System.Linq;
using System.Collections.Generic;
using System;
using System.Reflection;
using RimWorld;

namespace HOOD
{
    public class HOODSettings : ModBase
    {
        private static string mModIdentifier = "Hold_Open_Opens_Doors";

        private List<SettingHandle<bool>> settingsList = new List<SettingHandle<bool>>();
        public static List<String> disallowedDoors = new List<String>();
        public static bool disableOnDoors = false;
        public static bool checkPower = true;

        public override string ModIdentifier
        {
            get { return mModIdentifier; }
        }

        // Main entry point for main menu
        public override void DefsLoaded()
        {
            UpdateEnableLogging(); // Do first so all the code logs before all the settings are loaded normally
            SetTitle();
            L.Log("Settings populating...");
            PopulateSettings(GetDoorBuildings());
            UpdateSettings();
        }

        // Set the settings title
        private void SetTitle()
        {
            Settings.EntryName = "Hold Open Opens Doors";
        }

        // Populate the settings menu with items, includings doors from core and mods
        private void PopulateSettings(List<ThingDef> things)
        {
            Settings.GetHandle<bool>("CheckPower", "Enable power check", "When enabled this checks that the door has power to open/close (for added realism)", true);
            Settings.GetHandle<bool>("DisableOnDoors", "Disable on selected doors", "When enabled this stops immediate opening/closing on the selected doors below (for added realism)", false);
            Settings.GetHandle<bool>("EnableLogging", "Enable logging", "Enable logging", false);

            foreach (ThingDef thing in things)
            {
                String className = thing.ToString();
                bool enabled = false;
                //if (className.Equals("Door")) enabled = true; // Pre set door to be disabled for realism

                if (thing.HasComp(typeof(CompPower))) enabled = true; // Pre set door to be enabled by default in list if it has power
                
                // Don't add Jecrells invisible door to the settings, but keep it in the list of doors for actioning
                if (!thing.label.ToLower().Equals("invisible door"))
                {
                    // Display name
                    String displayName = thing.label;
                    String description = thing.description;

                    settingsList.Add(Settings.GetHandle<bool>(className, displayName, description, enabled));
                }
            }

            L.Log("Settings populated!");
        }

        // Update the enable logging flag, and also the Logger
        private void UpdateEnableLogging()
        {
            Settings.GetHandle<bool>("EnableLogging", "Enable logging", "Enable logging", false); // set up the item before grabbing it, need to setup correctly
            SettingHandle<bool> newEnableLogging = Settings.GetHandle<bool>("EnableLogging");
            if (newEnableLogging != null) L.loggingEnabled = newEnableLogging.Value;
        }

        // Update the settings to reflect the new values, this then updates the list of dissallowed doors
        private void UpdateSettings()
        {
            L.Log("Settings updating...");

            SettingHandle<bool> newCheckPower = Settings.GetHandle<bool>("CheckPower");
            if(newCheckPower != null) checkPower = newCheckPower.Value;
            SettingHandle<bool> newDisableOnDoors = Settings.GetHandle<bool>("DisableOnDoors");
            if(newDisableOnDoors != null) disableOnDoors = newDisableOnDoors.Value;
            UpdateEnableLogging();

            UpdateDisallowedDoors();

            L.Log("Settings updated!");
        }

        // Update the list of dissallowed doors to contain the selected doors
        private void UpdateDisallowedDoors()
        {
            IEnumerable<SettingHandle<bool>> dissallowedDoorSettings = settingsList.Where<SettingHandle<bool>>(s => s != null && s.Value == true);
            List<String> dissallowedDoorList = new List<String>();

            foreach(SettingHandle<bool> dissallowedDoorSetting in dissallowedDoorSettings)
            {
                String name = dissallowedDoorSetting.Name;

                dissallowedDoorList.Add(name);
            }

            disallowedDoors = dissallowedDoorList;
        }

        // Dynamooo! Dynamic cast used for reflecting private fields from mod objects
        private static T DynamicCast<T>(T obj) where T : class
        {
            var type = obj.GetType();
            return Activator.CreateInstance(type) as T;
        }

        // Get all the door buildings from core, and from mods
        private List<ThingDef> GetDoorBuildings()
        {
            // Get all buildings, filter down as much as possible to find buildings that 'may' be doors
            IEnumerable<ThingDef> thingDefs = DefDatabase<ThingDef>.AllDefs.Where<ThingDef>(def => def != null && def.building != null
            && !def.IsBlueprint 
            && !def.IsFrame 
            && !def.IsMeat 
            && !def.IsWeapon
            && !def.IsMeleeWeapon
            && !def.IsWeaponUsingProjectiles
            && !def.IsRangedWeapon
            && !def.IsApparel
            && !def.IsDrug
            && !def.IsPleasureDrug
            && !def.IsAddictiveDrug
            && !def.IsNonMedicalDrug
            && !def.IsStuff
            && !def.IsTable
            && !def.IsWorkTable
            && !def.IsMedicine
            && !def.IsIngestible
            && !def.IsCorpse
            && !def.IsCommsConsole
            && !def.IsBed
            && !def.IsArt
            && !def.IsFilth
            && !def.IsFoodDispenser
            && !def.IsLeather
            && !def.IsMetal
            && !def.IsNutritionGivingIngestible
            && !def.IsShell
            && !def.IsSmoothable
            && !def.IsSmoothed
            && !def.EverHaulable
            // Building checks
            && !def.building.SupportsPlants
            && !def.building.isSittable // Chairs I guess
            && def.building.canPlaceOverWall // doors can be placed over walls, not much else can
            );
            
            L.Log("Finding doors...");

            List<ThingDef> doors = new List<ThingDef>();
            foreach (ThingDef thingDef in thingDefs)
            {
                if(thingDef.building != null && thingDef.thingClass != null)
                {
                    Type thingDefType = thingDef.building.GetType();

                    Thing thing = (Thing)Activator.CreateInstance(thingDef.thingClass);

                    FieldInfo prop = null;
                    var dynamo = DynamicCast(thing);
                    if (dynamo != null)
                    {
                        prop = dynamo.GetType().GetField("holdOpenInt", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (prop == null) prop = dynamo.GetType().BaseType.GetField("holdOpenInt", BindingFlags.NonPublic | BindingFlags.Instance); // Try the base type
                    }

                    if (prop != null || thingDef.defName.Equals("HeronInvisibleDoor"))
                    {
                        String ohMy = "";
                        if (thingDef.defName.Equals("HeronInvisibleDoor"))
                            ohMy = " (invisible door handler? Let's dance...!) ";

                        L.Log("Found a door - " + thingDef.label + " in the assembly " + thingDef.thingClass + ohMy);
                        doors.Add(thingDef);
                    }
                }
            }

            // Log door count
            LogDoorCount(doors.Count);
            
            return doors;
        }

        // Log the door count with some flavour
        private static void LogDoorCount(int doorCount)
        {
            if (L.loggingEnabled)
            {
                string suffix;
                if (doorCount == 0)
                    suffix = "not even one door? That's not good! Scrambling fairies...! j/k I don't have the budget for that, check your mod load order broseph";
                else if (doorCount < 3)
                    suffix = "tastes like vanilla!";
                else if (doorCount < 7)
                    suffix = "phew!";
                else
                    suffix = "I also like to live dangerously!";

                L.Log("Found " + doorCount + " doors..." + suffix);
            }
        }

        // Settings changed event handler
        public override void SettingsChanged()
        {
            base.SettingsChanged();

            UpdateSettings();
        }
    }
}