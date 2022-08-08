﻿using System;
using System.Collections.Generic;
using System.IO;
using SMLHelper.V2.Handlers;
using SubnauticaRandomiser.RandomiserObjects;
using static LootDistributionData;

namespace SubnauticaRandomiser.Logic
{
    /// <summary>
    /// Handles everything related to randomising fragments.
    /// </summary>
    internal class FragmentLogic
    {
        private readonly CoreLogic _logic;
        
        private static Dictionary<TechType, List<string>> _classIdDatabase;
        private RandomiserConfig _config { get { return _logic._config; } }
        private EntitySerializer _masterDict { get { return _logic._masterDict; } }
        private Random _random { get { return _logic._random; } }
        private readonly List<Biome> _allBiomes;
        private readonly List<Biome> _availableBiomes;
        private static readonly Dictionary<string, TechType> _fragmentDataPaths = new Dictionary<string, TechType>
        {
            { "BaseBioReactor_Fragment", TechType.BaseBioReactorFragment },
            { "BaseNuclearReactor_Fragment", TechType.BaseNuclearReactorFragment },
            { "BatteryCharger_Fragment", TechType.BatteryChargerFragment },
            { "Beacon_Fragment", TechType.BeaconFragment },
            { "Constructor_Fragment", TechType.ConstructorFragment },
            { "Constructor_Fragment_InCrate", TechType.ConstructorFragment },
            { "CyclopsBridge_Fragment", TechType.CyclopsBridgeFragment },
            { "CyclopsEngine_Fragment", TechType.CyclopsEngineFragment },
            { "CyclopsHull_Fragment_Large", TechType.CyclopsHullFragment },
            { "CyclopsHull_Fragment_Medium", TechType.CyclopsHullFragment },
            { "Exosuit_Fragment", TechType.ExosuitFragment },
            { "ExosuitDrillArmfragment", TechType.ExosuitDrillArmFragment },
            { "ExosuitGrapplingArmfragment", TechType.ExosuitGrapplingArmFragment },
            { "ExosuitPropulsionArmfragment", TechType.ExosuitPropulsionArmFragment },
            { "ExosuitTorpedoArmfragment", TechType.ExosuitTorpedoArmFragment },
            { "GravSphere_Fragment", TechType.GravSphereFragment },
            { "LaserCutterFragment", TechType.LaserCutterFragment },
            { "LaserCutterFragment_InCrate", TechType.LaserCutterFragment },
            { "ledlightfragment", TechType.LEDLightFragment },
            { "moonpoolfragment", TechType.MoonpoolFragment },
            { "PowerCellCharger_Fragment", TechType.PowerCellChargerFragment },
            { "powertransmitterfragment", TechType.PowerTransmitterFragment },
            { "PropulsionCannonJunkFragment", TechType.PropulsionCannonFragment },
            { "scannerroomfragment", TechType.BaseMapRoomFragment },
            { "SeaglideJunkFragment", TechType.SeaglideFragment },
            { "Seamoth_Fragment", TechType.SeamothFragment },
            { "StasisRifleJunkFragment", TechType.StasisRifleFragment },
            { "ThermalPlant_Fragment", TechType.ThermalPlantFragment },
            { "Workbench_Fragment", TechType.WorkbenchFragment }
        };
        internal List<SpawnData> AllSpawnData;

        /// <summary>
        /// Handle the logic for everything related to fragments.
        /// </summary>
        internal FragmentLogic(CoreLogic coreLogic, List<BiomeCollection> biomeList)
        {
            _logic = coreLogic;
            
            _allBiomes = GetAvailableFragmentBiomes(biomeList);
            _availableBiomes = GetAvailableFragmentBiomes(biomeList);
            AllSpawnData = new List<SpawnData>();
        }
        
        /// <summary>
        /// Randomise the spawn points for a given fragment.
        /// </summary>
        /// <param name="entity">The fragment entity to randomise.</param>
        /// <param name="depth">The maximum depth to consider.</param>
        /// <returns>The SpawnData that was newly added to the EntitySerializer.</returns>
        /// <exception cref="ArgumentException">Raised if the fragment name is invalid.</exception>
        internal SpawnData RandomiseFragment(LogicEntity entity, int depth)
        {
            if (!_classIdDatabase.TryGetValue(entity.TechType, out List<string> idList))
                throw new ArgumentException("Failed to find fragment '" + entity + "' in classId database!");
            
            LogHandler.Debug("Randomising fragment " + entity + " for depth " + depth);

            // HACK for now, only consider the first entry in the ID list.
            string classId = idList[0];
            SpawnData spawnData = new SpawnData(classId);

            // Determine how many different biomes the fragment should spawn in.
            int biomeCount = _random.Next(3, _config.iMaxBiomesPerFragment);

            for (int i = 0; i < biomeCount; i++)
            {
                // Choose a suitable biome which is also accessible at this depth.
                Biome biome = GetRandom(_availableBiomes.FindAll(x => x.AverageDepth <= depth));
                // In case no good biome is available, ignore overpopulation restrictions and choose any.
                biome ??= GetRandom(_allBiomes.FindAll(x => x.AverageDepth <= depth));

                // Ensure the biome can actually be used for creating valid BiomeData.
                if (!Enum.TryParse(biome.Name, out BiomeType biomeType))
                {
                    LogHandler.Warn("  Failed to parse biome to enum: " + biome.Name);
                    // i--;
                    continue;
                }
                biome.Used++;

                // Remove the biome from the pool if it gets too populated.
                if (biome.Used >= 5)
                    _availableBiomes.Remove(biome);

                RandomiserBiomeData data = new RandomiserBiomeData
                {
                    Biome = biomeType,
                    Count = 1,
                    Probability = CalcFragmentSpawnRate(biome)
                };

                spawnData.AddBiomeData(data);
                LogHandler.Debug("  Adding fragment to biome: " + data.Biome.AsString() + ", " + data.Probability);
            }

            ApplyRandomisedFragment(entity, spawnData);
            return spawnData;
        }
        
        /// <summary>
        /// Go through all the BiomeData in the game and reset any fragment spawn rates to 0.0f, effectively "deleting"
        /// them from the game until the randomiser has decided on a new distribution.
        /// </summary>
        internal static void ResetFragmentSpawns()
        {
            LogHandler.Debug("---Resetting vanilla fragment spawn rates---");

            // For the rest of all the randomisation, we need TechTypes to classId.
            // Unfortunately, for just this once, we need the opposite.
            Dictionary<string, TechType> fragmentDatabase = ReverseClassIdDatabase();

            // Grab a copy of all vanilla BiomeData. This loads it fresh from disk
            // and will thus be unaffected by any existing randomisation.
            LootDistributionData loot = LootDistributionData.Load(LootDistributionData.dataPath);

            foreach (KeyValuePair<BiomeType, DstData> keyValuePair in loot.dstDistribution)
            {
                BiomeType biome = keyValuePair.Key;
                DstData dstData = keyValuePair.Value;

                foreach (PrefabData prefab in dstData.prefabs)
                {
                    // Ensure the prefab is actually a fragment.
                    if (fragmentDatabase.ContainsKey(prefab.classId))
                    {
                        // Whatever spawn chance there was before, set it to 0.
                        LootDistributionHandler.EditLootDistributionData(prefab.classId, biome, 0f, 0);
                        //LogHandler.Debug("Reset spawn chance to 0 for " + fragmentDatabase[prefab.classId].AsString() + " in " + biome.AsString());
                    }
                }
            }

            LogHandler.Debug("---Completed resetting vanilla fragment spawn rates---");
        }
        
        /// <summary>
        /// Get all biomes that have fragment rate data, i.e. which contained fragments in vanilla.
        /// TODO: Can be expanded to include non-vanilla ones.
        /// </summary>
        /// <param name="collections">A list of all biomes in the game.</param>
        /// <returns>A list of Biomes with active fragment spawn rates.</returns>
        private List<Biome> GetAvailableFragmentBiomes(List<BiomeCollection> collections)
        {
            List<Biome> biomes = new List<Biome>();

            foreach (BiomeCollection col in collections)
            {
                if (!col.HasBiomes)
                    continue;

                foreach (Biome b in col.BiomeList)
                {
                    if (b.FragmentRate != null)
                        biomes.Add(b);
                }
            }
            LogHandler.Debug("---Total biomes suitable for fragments: "+biomes.Count);
            return biomes;
        }

        /// <summary>
        /// Calculate the spawn rate for an entity in the given biome.
        /// </summary>
        /// <param name="biome">The biome.</param>
        /// <returns>The spawn rate.</returns>
        private float CalcFragmentSpawnRate(Biome biome)
        {
            // Set a percentage between Min and Max% of the biome's combined original spawn rates.
            float percentage = _config.fFragmentSpawnChanceMin + (float)_random.NextDouble()
                * (_config.fFragmentSpawnChanceMax - _config.fFragmentSpawnChanceMax);
            return percentage * biome.FragmentRate ?? 0.0f;
        }
        
        /// <summary>
        /// Assemble a dictionary of all relevant prefabs with their unique classId identifier.
        /// </summary>
        private static void PrepareClassIdDatabase()
        {
            _classIdDatabase = new Dictionary<TechType, List<string>>();

            // Get the unique identifier of every single prefab currently loaded
            // by the game.
            var keys = UWE.PrefabDatabase.prefabFiles.Keys;

            foreach (string classId in keys)
            {
                string dataPath = UWE.PrefabDatabase.prefabFiles[classId];

                // If the prefab is not part of the predefined dictionary of fragments,
                // discard it and continue. Acts as a filter for only those fragments
                // which have actual BiomeData used by the game.
                if (!_fragmentDataPaths.TryGetValue(Path.GetFileName(dataPath), out TechType type))
                    continue;

                if (!_classIdDatabase.ContainsKey(type))
                    _classIdDatabase.Add(type, new List<string> { classId });
                else
                    _classIdDatabase[type].Add(classId);

                //LogHandler.Debug("KEY: " + classId + ", VALUE: " + UWE.PrefabDatabase.prefabFiles[classId] + ", TECHTYPE: " + type.AsString());
            }
        }
        
        /// <summary>
        /// Reverse the classId dictionary to allow for ID to TechType matching.
        /// </summary>
        /// <returns>The inverted dictionary.</returns>
        internal static Dictionary<string, TechType> ReverseClassIdDatabase()
        {
            Dictionary<string, TechType> database = new Dictionary<string, TechType>();

            foreach (KeyValuePair<TechType, List<string>> kv in _classIdDatabase)
            {
                foreach (string classId in kv.Value)
                {
                    // Ensure no duplicates.
                    if (!database.ContainsKey(classId))
                    {
                        database.Add(classId, kv.Key);
                        //LogHandler.Debug("Added to reversed fragment database: " + kv.Key.AsString());
                    }
                }
            }

            return database;
        }
        
        /// <summary>
        /// Re-apply spawnData from a saved game. This will fail to catch all existing fragment spawns if called in a
        /// previously randomised game.
        /// </summary>
        internal static void ApplyMasterDict(EntitySerializer masterDict)
        {
            Init();
            
            foreach (TechType key in masterDict.SpawnDataDict.Keys)
            {
                SpawnData spawnData = masterDict.SpawnDataDict[key];
                LootDistributionHandler.EditLootDistributionData(spawnData.ClassId, spawnData.GetBaseBiomeData());
            }
        }
        
        /// <summary>
        /// Add modified SpawnData to the game and any place it needs to go to be stored for later use.
        /// </summary>
        /// <param name="entity">The entity to modify spawn rates for.</param>
        /// <param name="spawnData">The modified SpawnData to use.</param>
        internal void ApplyRandomisedFragment(LogicEntity entity, SpawnData spawnData)
        {
            entity.SpawnData = spawnData;

            AllSpawnData.Add(spawnData);
            _masterDict.AddSpawnData(entity.TechType, spawnData);

            LootDistributionHandler.EditLootDistributionData(spawnData.ClassId, spawnData.GetBaseBiomeData());
        }

        /// <summary>
        /// Get the classId for the given TechType.
        /// </summary>
        private static string GetClassId(TechType type)
        {
            return CraftData.GetClassIdForTechType(type);
        }

        /// <summary>
        /// Get a random element from the given list.
        /// </summary>
        /// <param name="list">The list to choose a member from.</param>
        /// <typeparam name="T">The type of the objects contained in the list.</typeparam>
        /// <returns>A random element of the list.</returns>
        private T GetRandom<T>(List<T> list)
        {
            if (list is null || list.Count == 0)
                return default(T);

            return list[_random.Next(0, list.Count)];
        }

        /// <summary>
        /// Force Subnautica and SMLHelper to index and cache the classIds, setup the databases, and prepare a blank
        /// slate by removing all existing fragment spawns from the game.
        /// </summary>
        public static void Init()
        {
            // This forces SMLHelper (and the game) to cache the classIds.
            // Without this, anything below will fail.
            _ = GetClassId(TechType.Titanium);

            PrepareClassIdDatabase();
            ResetFragmentSpawns();
        }

        // -------------------------------------------
        // -------------------------------------------
        // -------------------------------------------
        // This is really just for testing purposes.
        internal void DumpBiomeDataEntities()
        {
            LogHandler.Debug("---Dumping BiomeData---");

            // Grab a copy of all vanilla BiomeData. This loads it fresh from disk
            // and will thus be unaffected by any existing randomisation.
            LootDistributionData loot = LootDistributionData.Load(LootDistributionData.dataPath);
            var keys = UWE.PrefabDatabase.prefabFiles.Keys;

            LogHandler.Debug("---Dumping valid prefabs");
            foreach (string classId in keys)
            {
                if (!loot.GetPrefabData(classId, out SrcData data))
                    continue;
                
                // Any prefab with BiomeData will end up in the log files. This is
                // the case even if that BiomeData specifies 0.0 spawn chance across
                // the board and is thus "empty".
                LogHandler.Debug("KEY: " + classId + ", VALUE: " + UWE.PrefabDatabase.prefabFiles[classId]);
            }
            
            LogHandler.Debug("---Dumping Biomes");
            BiomeType[] biomes = (BiomeType[])Enum.GetValues(typeof(BiomeType));
            foreach (BiomeType biome in biomes)
            {
                if (loot.GetBiomeLoot(biome, out DstData distributionData))
                {
                    int valid = 0;
                    int validFragments = 0;
                    float sum = 0f;
                    float sumFragments = 0f;
                    foreach (var prefab in distributionData.prefabs)
                    {
                        if (string.IsNullOrEmpty(prefab.classId) || prefab.classId.Equals("None"))
                            continue;

                        valid++;
                        sum += prefab.probability;

                        if (UWE.WorldEntityDatabase.TryGetInfo(prefab.classId, out UWE.WorldEntityInfo info)){
                            if (info != null && !info.techType.Equals(TechType.None) && info.techType.AsString().ToLower().Contains("fragment"))
                            {
                                validFragments++;
                                sumFragments += prefab.probability;
                            }
                        }
                    }
                    //LogHandler.Debug("BIOME: " + biome.AsString() + ", VALID ENTRIES: " + valid + ", SUM: " + sum + ", OF WHICH FRAGMENTS: " + sumFragments);
                    LogHandler.Debug(string.Format("{0}\t{1} entries\t{2} fragments\t{3} totalspawnrate\t{4} totalfragmentrate", biome.AsString(), valid, validFragments, sum, sumFragments));
                }
                else
                {
                    //LogHandler.Debug("No DstData for biome " + biome.AsString());
                    LogHandler.Debug(string.Format("{0}\tNONE\t\t", biome.AsString()));
                }
            }
        }
        
        // This is kinda redundant and a leftover from early testing.
        internal void EditBiomeData(string classId, List<LootDistributionData.BiomeData> distribution)
        {
            LootDistributionHandler.EditLootDistributionData(classId, distribution);
        }

        internal void Test()
        {
            LogHandler.Debug("titanium " + GetClassId(TechType.Titanium));
            LogHandler.Debug("peeper " + GetClassId(TechType.Peeper));
            LogHandler.Debug("reaper " + GetClassId(TechType.ReaperLeviathan));
            LogHandler.Debug("acid " + GetClassId(TechType.AcidMushroom));
            LogHandler.Debug("seamothfragment " + GetClassId(TechType.SeamothFragment));
            LogHandler.Debug("cyclopshullfragment " + GetClassId(TechType.CyclopsHullFragment));

            PrepareClassIdDatabase();

            BiomeData b = new BiomeData();
            b.biome = BiomeType.SafeShallows_Grass;
            b.count = 1;
            b.probability = 0.8f;

            BiomeData b2 = new BiomeData();
            b2.biome = BiomeType.GrassyPlateaus_TechSite_Scattered;
            b2.count = 1;
            b2.probability = 0.9f;

            List<BiomeData> list = new List<BiomeData>();
            list.Add(b);
            list.Add(b2);

            EditBiomeData(_classIdDatabase[TechType.BaseNuclearReactorFragment][0], list);
            //DumpBiomeDataEntities();
        }
    }
}
