using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using CevoAILib;

namespace AI
{
    /// <summary>
    /// Per empire persistant data storage. You can store whatever you like in here, up to 4 KB, and it will be saved and loaded with
    /// the rest of the game. I recommend that you design the in memory layout in MyData for compactness, and wrap it with accessors
    /// in Persistent designed for convenience.
    /// </summary>
    unsafe class Persistent
    {
        /// <summary>
        /// Layout in game memory of your custom per-empire data storage. The definition and contents have no meaning other than what
        /// you give them, so change this however you like with the following caveats:
        ///   - No references or pointers. These will not keep their meaning through save and reload.
        ///   - Total size is limited to 4 KB.
        /// 
        /// Only the instance that is provided for you by the game engine will be stored in saved games.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct MyData
        {
            public int Version;
            public fixed short EstimatedStrength[Cevo.MaxNumberOfNations];
            public Advance LastResearch;
            public fixed sbyte BiggestFriend[Cevo.MaxNumberOfNations];
        }

        public class SpyReports : APersistentDictionary<PersistentCityId, CitySpyReport, SpyReports>
        {
            public const int Id = 1;

            private SpyReports(AEmpire empire, int id) : base(empire, id) { }

            public static SpyReports Get(AEmpire empire, int id) => Index[empire][id];

            public static void Create(AEmpire empire, int id)
            {
                fixed (int* data = new[] {Id, id})
                {
                    empire.Play(Protocol.cExCollectionCreate, 0, data);
                }
            }

            public static SpyReports GetOrCreate(AEmpire empire, int id)
            {
                if (Index.TryGetValue(empire, out IDictionary<int, SpyReports> dict)
                    && dict.TryGetValue(id, out SpyReports reports))
                    return reports;
                Create(empire, id);
                return Get(empire, id);
            }

            public static void Handle(AEmpire empire, int baseCommand, int instanceId, int[][] data)
            {
                switch (baseCommand)
                {
                    case Protocol.cExCollectionCreate:
                        new SpyReports(empire, instanceId);
                        break;
                    case Protocol.cExCollectionDelete:
                        Index[empire].Remove(instanceId);
                        break;
                    default:
                        Get(empire, instanceId).Handle(baseCommand, data);
                        break;
                }
            }

            protected override int ClassId => Id;
            protected override int[] SerializeKey(PersistentCityId key) => key.Serialize();
            protected override PersistentCityId DeserializeKey(int[] serialized) =>
                PersistentCityId.Deserialize(serialized);

            protected override int[] SerializeValue(CitySpyReport value) => value.Serialize();
            protected override CitySpyReport DeserializeValue(int[] serialized) =>
                new CitySpyReport(TheEmpire, serialized);
        }

        private readonly Empire TheEmpire;
        private readonly MyData* Data;

        public Persistent(Empire empire, MyData* dataPtr)
        {
            if (sizeof(MyData) > 4096)
                throw new Exception("Persistent data size exceeds limit!");

            TheEmpire = empire;
            Data = dataPtr;

            // Initialization of the Data block *only*. Creating persistent collections here will not save and reload
            // correctly.
            Data->LastResearch = Advance.None;
            for (int nationID = 0; nationID < Cevo.MaxNumberOfNations; nationID++)
            {
                Data->EstimatedStrength[nationID] = 100;
                Data->BiggestFriend[nationID] = (sbyte) ((IId) Nation.None.Id).Index;
            }
        }

        public void NewGame()
        {
            // Create any initial persistent collection instances here.
            SpyReports.Create(TheEmpire, 1);
            OldSpyReports = SpyReports.Get(TheEmpire, 1);
        }

        public void Resume()
        {
            // Fetch any persistent collection references here. The reload process will have already restored them.
            OldSpyReports = SpyReports.Get(TheEmpire, 1);
        }

        public SpyReports OldSpyReports { get; private set; }

        public int Version
        {
            get => Data->Version;
            set => Data->Version = value;
        }

        public int EstimatedStrength(Nation nation) => Data->EstimatedStrength[((IId) nation.Id).Index];

        public void SetEstimatedStrength(Nation nation, int strength)
        {
            Data->EstimatedStrength[((IId) nation.Id).Index] = (short)strength;
        }

        public Advance LastResearch
        {
            get => Data->LastResearch;
            set => Data->LastResearch = value;
        }

        public Nation BiggestFriendOf(Nation nation) => new Nation(TheEmpire, new NationId(Data->BiggestFriend[((IId) nation.Id).Index]));

        public void SetBiggestFriendOf(Nation nation, Nation friend)
        {
            Data->BiggestFriend[((IId) nation.Id).Index] = (sbyte) ((IId) friend.Id).Index;
        }
    }
}
