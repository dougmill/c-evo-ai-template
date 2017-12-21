using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using AI;

namespace CevoAILib
{
    // All structs and enums defined in this file represent the values and layout of data in memory that is shared with
    // the game server. Do not change their values, fields, field order, or size unless you are certain your changes
    // match the game server's data representation.
    //
    // Almost all fields in these structs are read only - writing to them could corrupt game state. The only exception
    // is the Status fields in units, cities, models, and foreign cities. For writing to these Status fields, take care
    // that you are writing to the actual shared instance, not a copied value; this requires passing the struct around
    // by pointer instead of value.
    //
    // These structs are instantiated by the server and should never be created by template code, with the occasional
    // exception of ids.

    #region Common
    /// <summary>
    /// Interface for Id numbers that also operate as indices into one or more arrays of data in shared memory. Some
    /// of these are permanent, others are valid only within a single turn. If the specific id's summary doesn't state
    /// it's permanent, assume it isn't.
    /// </summary>
    interface IId
    {
        int Index { get; }
        bool IsValid { get; }
    }

    /// <summary>
    /// A type safe generic array, readable and enumerable but not necessarily writable, indexed by a particular type
    /// of Id number. This interface is for use by template code, not a shared memory data structure.
    /// </summary>
    interface IReadableIdIndexedArray<Id, out T> : IReadOnlyCollection<T> where Id : IId
    {
        T this[Id id] { get; }
    }

    /// <summary>
    /// A type safe generic array, indexed by a particular type of Id number. This is a class for use by template code,
    /// not a shared memory data structure.
    /// </summary>
    class IdIndexedArray<Id, T> : IReadableIdIndexedArray<Id, T> where Id : IId
    {
        private readonly T[] Array;

        public int Count => Array.Length;

        public IdIndexedArray(int size) => Array = new T[size];

        public IdIndexedArray(Id lastId) => Array = new T[lastId.Index + 1];

        /// <summary>
        /// Sets all elements in the array to the given initial value.
        /// </summary>
        public IdIndexedArray(int size, T fillValue)
        {
            Array = new T[size];
            for (int i = 0; i < size; i++)
                Array[i] = fillValue;
        }

        public T this[Id id]
        {
            get => Array[id.Index];
            set => Array[id.Index] = value;
        }

        /// <summary>
        /// Creates a new array with the given size, and copies this array's elements into it. If the new size is a
        /// reduction in size, any elements beyond the end of the new size will be omitted.
        /// </summary>
        public IdIndexedArray<Id, T> Copy(int newSize)
        {
            var copy = new IdIndexedArray<Id, T>(newSize);
            System.Array.Copy(Array, copy.Array, Math.Min(Array.Length, newSize));
            return copy;
        }

        public void Clear()
        {
            System.Array.Clear(Array, 0, Array.Length);
        }

        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Array).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => Array.GetEnumerator();

        public override string ToString()
        {
            string str = $"{{{typeof(Id).Name}: {typeof(T).Name}";
            for (int i = 0; i < Array.Length; i++)
                str += $", {i}: {Array[i]}";
            str += "}";
            return str;
        }
    }

    /// <summary>
    /// A type safe generic list, readable and enumerable but not necessarily writable, indexed by a particular type
    /// of Id number. This interface is for use by template code, not a shared memory data structure.
    /// </summary>
    interface IReadableIdIndexedList<Id, out T> : IReadOnlyList<T> where Id : IId
    {
        T this[Id id] { get; }
        T Last { get; }
    }

    /// <summary>
    /// A type safe generic list, indexed by a particular type of Id number. This is a class for use by template code,
    /// not a shared memory data structure.
    /// </summary>
    class IdIndexedList<Id, T> : IList<T>, IReadableIdIndexedList<Id, T> where Id : IId
    {
        private readonly List<T> List = new List<T>();

        public void Add(T item) => List.Add(item);
        public void Clear() => List.Clear();
        public bool Contains(T item) => List.Contains(item);
        public void CopyTo(T[] array, int arrayIndex) => List.CopyTo(array, arrayIndex);
        public bool Remove(T item) => List.Remove(item);
        public int Count => List.Count;
        public bool IsReadOnly => false;

        int IList<T>.IndexOf(T item) => List.IndexOf(item);
        void IList<T>.Insert(int index, T item) => List.Insert(index, item);
        void IList<T>.RemoveAt(int index) => List.RemoveAt(index);
        T IList<T>.this[int index]
        {
            get => List[index];
            set => List[index] = value;
        }

        T IReadOnlyList<T>.this[int index] => List[index];

        public T this[Id id]
        {
            get => List[id.Index];
            set => List[id.Index] = value;
        }

        public T Last => List[List.Count - 1];

        public IEnumerator<T> GetEnumerator() => List.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString()
        {
            string str = $"{{{typeof(Id).Name}: {typeof(T).Name}";
            for (int i = 0; i < List.Count; i++)
                str += $", {i}: {List[i]}";
            str += "}";
            return str;
        }
    }
    #endregion

    #region Cities
    /// <summary>
    /// Non-indexing permanent id of a city, either own or foreign. Globally unique. Size 2 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 2)]
    struct PersistentCityId
    {
        private readonly short Id;

        public bool IsValid => Id >= 0;
        public NationId Founder => new NationId(Id >> 12);
        public int SerialNumber => Id & 0xFFF;

        private PersistentCityId(short id) => Id = id;

        public int[] Serialize() => new int[] {Id};
        public static PersistentCityId Deserialize(int[] serialized) => new PersistentCityId((short) serialized[0]);

        public static readonly PersistentCityId WonderDestroyedId = new PersistentCityId(-2);
        public static readonly PersistentCityId WonderUnbuiltId = new PersistentCityId(-1);

        public static bool operator ==(PersistentCityId id1, PersistentCityId id2) => id1.Id == id2.Id;
        public static bool operator !=(PersistentCityId id1, PersistentCityId id2) => id1.Id != id2.Id;

        public override int GetHashCode() => Id;
        public override bool Equals(object obj)
        {
            Debug.Assert(obj is PersistentCityId);
            return Id == ((PersistentCityId) obj).Id;
        }

        public override string ToString() => $"Founder: {Founder}, Number: {SerialNumber}";
    }

    /// <summary>
    /// Events that can happen to a city, size 2 bytes.
    /// </summary>
    [Flags]
    enum CityEvents : ushort
    {
        CivilDisorder = 0x0001,
        ProductionComplete = 0x0002,
        PopulationGrowth = 0x0004,
        PopulationDecrease = 0x0008,
        UnitDisbanded = 0x0010,
        ImprovementSold = 0x0020,
        ProductionSabotaged = 0x0040,
        MaximumSizeReached = 0x0080,
        Pollution = 0x0100,
        CityUnderSiege = 0x0200,
        WonderAlreadyExists = 0x0400,
        EmigrationDelayed = 0x0800,
        CityFounded = 0x1000,
        TakeoverComplete = 0x2000
    }

    /// <summary>
    /// Special options for unit production, size 2 bytes.
    /// </summary>
    [Flags]
    enum UnitProductionOptions : short
    {
        None = 0,
        AllowDisbandCity = Protocol.cpDisbandCity,
        AsConscripts = Protocol.cpConscripts,
        Filter = AllowDisbandCity | AsConscripts
    }

    /// <summary>
    /// Improvements a city can build, size 2 bytes.
    /// </summary>
    enum Building : short
    {
        None = 28,

        Pyramids = 0, TempleOfZeus = 1, HangingGardens = 2, Colossus = 3, Lighthouse = 4,
        GreatLibrary = 5, Oracle = 6, SunTsusWarAcademy = 7, LeonardosWorkshop = 8, MagellansExpedition = 9,
        MichelangelosChapel = 10, NewtonsCollege = 12, BachsCathedral = 13,
        StatueOfLiberty = 15, EiffelTower = 16, HooverDam = 17, ShinkansenExpress = 18, ManhattanProject = 19,
        MIRSpaceStation = 20,
        HighestWonderValue = 20,
        WonderRange = 28, // for logic only, < WonderRange means wonder (better use Cevo.Pedia(Building).Kind)

        Barracks = 29, Granary = 30, Temple = 31, Marketplace = 32, Library = 33, Courthouse = 34,
        CityWalls = 35, Aqueduct = 36, Bank = 37, Cathedral = 38, University = 39,
        Harbor = 40, Theater = 41, Factory = 42, MfgPlant = 43, RecyclingCenter = 44,
        PowerStation = 45, HydroelectricDam = 46, NuclearPlant = 47, OffshorePlatform = 48, TownHall = 49,
        SewerSystem = 50, Supermarket = 51, Superhighways = 52, ResearchLab = 53, SAM = 54,
        CoastalFortress = 55, Airport = 56, Dockyard = 57,

        FirstStateImprovement = 58, Palace = 58, GreatWall = 59, Colosseum = 60, Observatory = 61, MilitaryAcademy = 62,
        CommandBunker = 63, AlgaePlant = 64, StockExchange = 65, SpacePort = 66, LastStateImprovement = 66,

        ColonyShipComponent = 67, PowerModule = 68, HabitationModule = 69,

        MaxValidValue = 69
    }

    /// <summary>
    /// Representation of what a city is building, size 2 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 2)]
    struct CityProject
    {
        private readonly short Project;

        public bool IsUnit => (Project & Protocol.cpImp) == 0;
        public ModelId UnitInProduction => IsUnit ? new ModelId((short) (Project & Protocol.cpIndex)) : ModelId.Invalid;
        public ForeignOwnModelId ForeignUnitInProduction =>
            IsUnit ? new ForeignOwnModelId((short) (Project & Protocol.cpIndex)) : ForeignOwnModelId.Invalid;

        public bool IsBuilding => (Project & Protocol.cpImp) != 0 && Project != ((short) Building.None | Protocol.cpImp);
        public Building BuildingInProduction => IsBuilding ? (Building) (Project & Protocol.cpIndex) : Building.None;

        public bool IsTradeGoods => Project == ((short) Building.None | Protocol.cpImp);

        public static bool operator ==(CityProject p1, CityProject p2) => p1.Project == p2.Project;
        public static bool operator !=(CityProject p1, CityProject p2) => p1.Project != p2.Project;

        public override int GetHashCode() => Project;
        public override bool Equals(object obj)
        {
            Debug.Assert(obj is CityProject);
            return Project == ((CityProject) obj).Project;
        }

        public override string ToString() => IsUnit ? $"Unit Model {UnitInProduction}"
            : IsBuilding ? $"{BuildingInProduction}"
            : "Trade Goods";
    }

    #region Own Cities
    /// <summary>
    /// Id of own city, size 2 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 2)]
    struct CityId : IId
    {
        private readonly short Id;

        public bool IsValid => Id >= 0;

        public CityId(short id) => Id = id;

        int IId.Index => Id;

        public static bool operator ==(CityId id1, CityId id2) => id1.Id == id2.Id;
        public static bool operator !=(CityId id1, CityId id2) => id1.Id != id2.Id;

        public override int GetHashCode() => Id;
        public override bool Equals(object obj)
        {
            Debug.Assert(obj is CityId);
            return Id == ((CityId) obj).Id;
        }

        public override string ToString() => Id.ToString();

        /// <summary>
        /// Iterates from the first valid id through the given number of ids.
        /// </summary>
        public static IEnumerable<CityId> Range(int count)
        {
            for (short id = 0; id < count; id++)
                yield return new CityId(id);
        }

        /// <summary>
        /// Iterates from the first valid id through the given id.
        /// </summary>
        public static IEnumerable<CityId> Range(CityId end)
        {
            for (short id = 0; id <= end.Id; id++)
                yield return new CityId(id);
        }

        /// <summary>
        /// Iterates from the start id to the end id, including both end points as well as all ids between.
        /// </summary>
        public static IEnumerable<CityId> Range(CityId start, CityId end)
        {
            for (short id = start.Id; id <= end.Id; id++)
                yield return new CityId(id);
        }
    }

    /// <summary>
    /// More user-friendly view of the highly compact server-side representation of what locations a city is working.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 4)]
    struct ExploitedTiles
    {
        /// <summary>
        /// A bit array indicating which tiles are currently used by the city. Bit indices cover the 4x7 (e/w x n/s)
        /// rectangle that includes the city radius, starting with the northwest corner in the ones bit and going in
        /// order, west to east then north to south.
        /// </summary>
        private readonly int Tiles;

        private static readonly int[] AdjustedDeBruijnBitPositions = {
            0, 0, 0, 1, 0, 11, 0, 0, 0, 18, 16, 0, 19, 13, 2, 5,
            0, 0, 10, 0, 17, 15, 12, 0, 20, 9, 14, 4, 8, 3, 7, 6
        };

        /// <summary>
        /// Note that this is recalculated on every access.
        /// </summary>
        public int NumberOfExploitedLocations
        {
            get
            {
                // magic (http://graphics.stanford.edu/~seander/bithacks.html#CountBitsSetParallel)
                unchecked
                {
                    int temp = Tiles - ((Tiles >> 1) & 0x55555555);
                    temp = (temp & 0x33333333) + ((temp >> 2) & 0x33333333);
                    return ((temp + (temp >> 4) & 0xF0F0F0F) * 0x1010101) >> 24;
                }
            }
        }

        /// <summary>
        /// The locations worked by this set of tiles, if the center is the given location. Recalculated on every call.
        /// </summary>
        public Location[] GetLocations(Location center)
        {
            Location[] locations = new Location[NumberOfExploitedLocations];
            Location[] distance5Area = center.Distance5Area;
            int tiles = Tiles;

            // magic (http://graphics.stanford.edu/~seander/bithacks.html#ZerosOnRightLinear)
            unchecked
            {
                if (center.YCoordinate >= 3)
                {
                    for (int i = 0; i < locations.Length; i++)
                    {
                        int leastSignificantBit = tiles & -tiles;
                        int indexInDistance5Area =
                            AdjustedDeBruijnBitPositions[(uint)(leastSignificantBit * 0x077CB531U) >> 27];
                        locations[i] = center.Distance5Area[indexInDistance5Area];
                        tiles -= leastSignificantBit;
                    }
                }
                else
                {
                    int indexOffset = 21 - distance5Area.Length;
                    for (int i = 0; i < locations.Length; i++)
                    {
                        int leastSignificantBit = tiles & -tiles;
                        int indexInDistance5Area =
                            AdjustedDeBruijnBitPositions[(uint) (leastSignificantBit * 0x077CB531U) >> 27];
                        locations[i] = center.Distance5Area[indexInDistance5Area - indexOffset];
                        tiles -= leastSignificantBit;
                    }
                }
            }

            return locations;
        }

        public static bool operator ==(ExploitedTiles t1, ExploitedTiles t2) => t1.Tiles == t2.Tiles;
        public static bool operator !=(ExploitedTiles t1, ExploitedTiles t2) => t1.Tiles != t2.Tiles;

        public override int GetHashCode() => Tiles;
        public override bool Equals(object obj)
        {
            Debug.Assert(obj is ExploitedTiles);
            return Tiles == ((ExploitedTiles) obj).Tiles;
        }

        public override string ToString()
        {
            int[] indices = new int[NumberOfExploitedLocations];
            int tiles = Tiles;
            unchecked
            {
                for (int i = 0; i < indices.Length; i++)
                {
                    int leastSignificantBit = tiles & -tiles;
                    indices[i] = AdjustedDeBruijnBitPositions[(uint)(leastSignificantBit * 0x077CB531U) >> 27];
                    tiles -= leastSignificantBit;
                }
            }
            return $"Working {indices.Length} tiles: {string.Join(", ", indices)}";
        }
    }

    /// <summary>
    /// Layout in game memory of the list of what improvements a city has.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 72)]
    unsafe struct BuiltImprovementsList
    {
        private fixed bool BuiltImprovements[(int) Building.MaxValidValue / 4 * 4 + 4]; // 72 bytes

        public bool IsBuilt(Building building)
        {
            fixed (bool* b = BuiltImprovements)
            {
                return b[(int) building];
            }
        }

        public static bool operator ==(BuiltImprovementsList b1, BuiltImprovementsList b2)
        {
            for (int i = 0; i <= (int) Building.MaxValidValue; i++)
                if (b1.BuiltImprovements[i] != b2.BuiltImprovements[i])
                    return false;
            return true;
        }
        public static bool operator !=(BuiltImprovementsList b1, BuiltImprovementsList b2)
        {
            for (int i = 0; i <= (int) Building.MaxValidValue; i++)
                if (b1.BuiltImprovements[i] == b2.BuiltImprovements[i])
                    return false;
            return true;
        }

        public override int GetHashCode()
        {
            fixed (bool* list = BuiltImprovements)
            {
                int hash = 1;
                for (int i = 0; i <= (int) Building.MaxValidValue; i++)
                    hash = hash * 17 + (list[i] ? 1 : 0);
                return hash;
            }
        }
        public override bool Equals(object obj)
        {
            Debug.Assert(obj is BuiltImprovementsList);
            return this == (BuiltImprovementsList) obj;
        }

        public override string ToString()
        {
            fixed (bool* list = BuiltImprovements)
            {
                List<Building> buildings = new List<Building>();
                for (int i = 0; i <= (int) Building.MaxValidValue; i++)
                    if (list[i])
                        buildings.Add((Building) i);
                return string.Join(", ", buildings);
            }
        }
    }

    /// <summary>
    /// Layout in game memory of basic information about your own cities. Size 112 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 112)]
    unsafe struct CityData
    {
        public readonly LocationId LocationId; // 4 bytes, 4 total
        public int Status; // 4 bytes, 8 total, writable
        private readonly int padding; // 4 bytes, 12 total, server internal data
        public readonly PersistentCityId PersistentId; // 2 bytes, 14 total
        public readonly short Size; // 2 bytes, 16 total
        public readonly CityProject Project; // 2 bytes, 18 total
        private readonly short padding2; // 2 bytes, 20 total, server internal data (Project at start of turn?)
        public readonly short FoodPile; // 2 bytes, 22 total
        public readonly short PollutionPile; // 2 bytes, 24 total
        public readonly short MaterialPile; // 2 bytes, 26 total
        public readonly short MaterialPileAtTurnStart; // 2 bytes, 28 total
        public readonly CityEvents Events; // 2 bytes, 30 total
        public readonly byte TurnsTillTakeoverComplete; // 1 byte, 31 total
        public readonly byte padding3; // 1 byte, 32 total
        public readonly ExploitedTiles ExploitedTiles; // 4 bytes, 36 total
        private readonly int padding4; // 4 bytes, 40 total, unused
        public readonly BuiltImprovementsList BuiltImprovements; // 72 bytes, 112 total

        /// <summary>
        /// Pointer to an array of CityData that for type safety can only be indexed by CityId, size 4 bytes.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Size = 4)]
        public struct Ptr
        {
            private readonly CityData* Address;

            public CityData* this[CityId id] => Address + ((IId) id).Index;
        }
    }
    #endregion

    #region ForeignCities
    /// <summary>
    /// Id of foreign city, size 2 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 2)]
    struct ForeignCityId : IId
    {
        private readonly short Id;

        public bool IsValid => Id >= 0;

        public ForeignCityId(short id) => Id = id;

        int IId.Index => Id;

        public static bool operator ==(ForeignCityId id1, ForeignCityId id2) => id1.Id == id2.Id;
        public static bool operator !=(ForeignCityId id1, ForeignCityId id2) => id1.Id != id2.Id;

        public override int GetHashCode() => Id;
        public override bool Equals(object obj)
        {
            Debug.Assert(obj is ForeignCityId);
            return Id == ((ForeignCityId) obj).Id;
        }

        public override string ToString() => Id.ToString();

        /// <summary>
        /// Iterates from the first valid id through the given number of ids.
        /// </summary>
        public static IEnumerable<ForeignCityId> Range(int count)
        {
            for (short id = 0; id < count; id++)
                yield return new ForeignCityId(id);
        }

        /// <summary>
        /// Iterates from the first valid id through the given id.
        /// </summary>
        public static IEnumerable<ForeignCityId> Range(ForeignCityId end)
        {
            for (short id = 0; id <= end.Id; id++)
                yield return new ForeignCityId(id);
        }

        /// <summary>
        /// Iterates from the start id to the end id, including both end points as well as all ids between.
        /// </summary>
        public static IEnumerable<ForeignCityId> Range(ForeignCityId start, ForeignCityId end)
        {
            for (short id = start.Id; id <= end.Id; id++)
                yield return new ForeignCityId(id);
        }
    }

    /// <summary>
    /// Buildings that can be readily seen in a foreign city without spying, size 2 bytes.
    /// </summary>
    [Flags]
    enum ObviousBuildings : short
    {
        Capital = 0x01,
        CityWalls = 0x02,
        CoastalFortress = 0x04,
        MissileBattery = 0x08,
        CommandBunker = 0x10,
        Spaceport = 0x20
    }

    /// <summary>
    /// Layout in game memory of information about foreign cities, size 20 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 20)]
    struct ForeignCityData
    {
        public readonly LocationId LocationId; // 4 bytes
        public int Status; // 4 bytes, 8 total, writable
        private readonly int padding; // 4 bytes, 12 total, server internal data
        public readonly NationId OwnerId; // 1 byte, 13 total
        private readonly byte padding2; // 1 byte, 14 total
        public readonly PersistentCityId PersistentId; // 2 bytes, 16 total
        public readonly short Size; // 2 bytes, 18 total
        public readonly ObviousBuildings ObviousBuildings; // 2 bytes, 20 total

        /// <summary>
        /// Pointer to an array of ForeignCityData that for type safety can only be indexed by ForeignCityId, size 4
        /// bytes.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Size = 4)]
        public unsafe struct Ptr
        {
            private readonly ForeignCityData* Address;

            public ForeignCityData* this[ForeignCityId id] => Address + ((IId) id).Index;
        }
    }
    #endregion
    #endregion

    #region Map
    /// <summary>
    /// Id of a location. Permanent and globally unique, as well as indexing. Size 4 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 4)]
    struct LocationId : IId
    {
        private readonly int Id;

        public bool IsValid => Id >= 0;

        public LocationId(int id) => Id = id;

        int IId.Index => Id;

        public static bool operator ==(LocationId id1, LocationId id2) => id1.Id == id2.Id;
        public static bool operator !=(LocationId id1, LocationId id2) => id1.Id != id2.Id;

        public override int GetHashCode() => Id;
        public override bool Equals(object obj)
        {
            Debug.Assert(obj is LocationId);
            return Id == ((LocationId) obj).Id;
        }

        public override string ToString() => Id.ToString();

        /// <summary>
        /// Iterates from the first valid id through the given number of ids.
        /// </summary>
        public static IEnumerable<LocationId> Range(int count)
        {
            for (int id = 0; id < count; id++)
                yield return new LocationId(id);
        }

        /// <summary>
        /// Iterates from the first valid id through the given id.
        /// </summary>
        public static IEnumerable<LocationId> Range(LocationId end)
        {
            for (int id = 0; id <= end.Id; id++)
                yield return new LocationId(id);
        }

        /// <summary>
        /// Iterates from the start id to the end id, including both end points as well as all ids between.
        /// </summary>
        public static IEnumerable<LocationId> Range(LocationId start, LocationId end)
        {
            for (int id = start.Id; id <= end.Id; id++)
                yield return new LocationId(id);
        }
    }

    /// <summary>
    /// Terrain types, size 4 bytes.
    /// </summary>
    [Flags]
    enum Terrain : int
    {
        Unknown = 0x1F, DeadLands = 0x01000003, // Deadlands is Desert plus a special flag
        Ocean = 0x00, Shore = 0x01, Grassland = 0x02, Desert = 0x03, Prairie = 0x04, Tundra = 0x05, Arctic = 0x06,
        Swamp = 0x07, Plains = Grassland | BasicSpecial, Forest = 0x09, Hills = 0x0A, Mountains = 0x0B,
        BaseTerrainMask = 0x0100001F,
        NonDeadlandsBaseTerrainMask = 0x1F,
        LandTerrainMask = 0x1E,
        Fish = BasicSpecial | Shore,
        Oasis = BasicSpecial | Desert,
        Wheat = BasicSpecial | Prairie,
        Gold = BasicSpecial | Tundra,
        Ivory = BasicSpecial | Arctic,
        Peat = BasicSpecial | Swamp,
        Game = BasicSpecial | Forest,
        Wine = BasicSpecial | Hills,
        Iron = BasicSpecial | Mountains,
        Manganese = ScienceSpecial | Shore,
        Oil = ScienceSpecial | Desert,
        Bauxite = ScienceSpecial | Prairie,
        Gas = ScienceSpecial | Tundra,
        MineralWater = ScienceSpecial | Forest,
        Coal = ScienceSpecial | Hills,
        Diamonds = ScienceSpecial | Mountains,
        Cobalt = 0x02000000 | DeadLands,
        Uranium = 0x04000000 | DeadLands,
        Mercury = 0x06000000 | DeadLands,
        BasicSpecial = 0x20, ScienceSpecial = 0x40,
        SpecialMask = BasicSpecial | ScienceSpecial, ModernSpecialMask = 0x06000000,
        TerrainMask = 0x0700007F
    }

    /// <summary>
    /// Alterations that can be done to a location, mostly worker improvements, size 4 bytes.
    /// </summary>
    [Flags]
    enum TerrainAlterations : int
    {
        None = 0x0,
        Irrigation = 0x1000, Farm = 0x2000, Mine = 0x3000, Fortress = 0x4000, Base = 0x5000,
        MainImprovementMask = 0xF000,
        River = 0x0080, Road = 0x0100, Railroad = 0x0200, Canal = 0x0400, Pollution = 0x0800
    }

    /// <summary>
    /// Non-terrain information about a location, size 4 bytes.
    /// </summary>
    [Flags]
    enum LocationFlags : int
    {
        ProtectedByGreatWall = 0x00010000,
        SpiedOut = 0x00020000,
        ForeignStealthUnitPresent = 0x00040000,
        ForeignSubmarinePresent = 0x00080000,
        Observed = 0x00100000,
        OccupiedByMe = 0x00200000,
        HasUnit = 0x00400000,
        HasOwnUnit = HasUnit | OccupiedByMe,
        HasCity = 0x00800000,
        HasOwnCity = HasCity | OccupiedByMe,
        ProjectsMyZoC = 0x10000000,
        InEnemyZoC = 0x20000000,
        BlockedByTreaty = 0x40000000
    }

    /// <summary>
    /// Layout in game memory of location information, size 4 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 4)]
    struct LocationData
    {
        private readonly int Data;

        #region basic info
        /// <summary>
        /// Whether the tile at this location was visible to an own unit or city at any point in the game
        /// </summary>
        public bool IsDiscovered => ((Terrain) Data & Terrain.BaseTerrainMask) != Terrain.Unknown;

        /// <summary>
        /// Whether the tile is visible to an own unit or city in this turn
        /// </summary>
        public bool IsObserved => ((LocationFlags) Data & LocationFlags.Observed) != 0;

        /// <summary>
        /// Whether the tile is visible to an own special commando or spy plane in this turn
        /// </summary>
        public bool IsSpiedOut => ((LocationFlags) Data & LocationFlags.SpiedOut) != 0;

        /// <summary>
        /// Whether an own city at this location would be protected by the great wall
        /// </summary>
        public bool IsGreatWallProtected => ((LocationFlags) Data & LocationFlags.ProtectedByGreatWall) != 0;

        /// <summary>
        /// Whether tile cannot be moved to because it's in the territory of a nation that we are in peace with but not
        /// allied.
        /// </summary>
        public bool IsDisallowedTerritory => ((LocationFlags) Data & LocationFlags.BlockedByTreaty) != 0;

        /// <summary>
        /// Whether units located here have 2 tiles observation range (distance 5) instead of adjacent locations only
        /// </summary>
        public bool ProvidesExtendedObservationRange => BaseTerrain == Terrain.Mountains ||
                                                        Improvement == TerrainAlterations.Fortress ||
                                                        Improvement == TerrainAlterations.Base;
        #endregion

        #region terrain
        /// <summary>
        /// Exact terrain type including special resources.
        /// </summary>
        public Terrain Terrain => (Terrain) Data & Terrain.TerrainMask;

        /// <summary>
        /// Base terrain type not including special resources.
        /// </summary>
        // Plains is officially considered a base terrain, but it's represented by combining grassland with the first
        // special resource flag, so it needs a special case.
        public Terrain BaseTerrain => ((Terrain) Data & Terrain.TerrainMask) == Terrain.Plains ? Terrain.Plains
                                    : (Terrain) Data & Terrain.BaseTerrainMask;

        /// <summary>
        /// Whether it's a water tile (terrain Ocean or Shore).
        /// </summary>
        public bool IsWater => ((Terrain) Data & Terrain.LandTerrainMask) == 0;

        private static readonly MovementKind[] RawTerrainMovementKind =
        {
            MovementKind.Plain, //Ocn
            MovementKind.Plain, //Sho
            MovementKind.Plain, //Gra
            MovementKind.Plain, //Dst
            MovementKind.Plain, //Pra
            MovementKind.Plain, //Tun
            MovementKind.Difficult, //Arc
            MovementKind.Difficult, //Swa
            MovementKind.Difficult, //-
            MovementKind.Difficult, //For
            MovementKind.Difficult, //Hil
            MovementKind.Mountains, //Mou
            MovementKind.Difficult, //-
            MovementKind.Difficult, //-
            MovementKind.Difficult, //-
            MovementKind.Plain, //Unexplored
        };

        public MovementKind MovementKind => RawTerrainMovementKind[Data & 0xF];

        /// <summary>
        /// Damage dealt to a unit which is not resistant to hostile terrain if that unit stays at this location for a
        /// full turn.
        /// </summary>
        public int OneTurnHostileDamage
        {
            get
            {
                if ((Data & ((int) LocationFlags.HasCity
                           | (int) TerrainAlterations.River
                           | (int) TerrainAlterations.Canal)) != 0
                    || ((TerrainAlterations) Data & TerrainAlterations.MainImprovementMask) == TerrainAlterations.Base)
                    return 0;
                // desert but not an oasis
                if (((Terrain) Data & (Terrain.NonDeadlandsBaseTerrainMask | Terrain.BasicSpecial)) == Terrain.Desert)
                    return Cevo.DamagePerTurnInDesert;
                if (((Terrain) Data & Terrain.BaseTerrainMask) == Terrain.Arctic)
                    return Cevo.DamagePerTurnInArctic;
                return 0;
            }
        }

        public bool HasRiver => (Data & (int) TerrainAlterations.River) != 0;
        public bool HasRoad =>
            (Data & ((int) (TerrainAlterations.Road | TerrainAlterations.Railroad) | (int) LocationFlags.HasCity)) != 0;
        // Note that railroad technology is not required for cities to give the movement benefit of railroad.
        public bool HasRailRoad => (Data & ((int) TerrainAlterations.Railroad | (int) LocationFlags.HasCity)) != 0;
        // Ships can move into a city the same as canal squares, but cities don't count as canals for ground move cost.
        public bool HasCanal => (Data & (int) TerrainAlterations.Canal) != 0;
        public bool IsPolluted => (Data & (int) TerrainAlterations.Pollution) != 0;

        /// <summary>
        /// Which, if any, of the mutually exclusive terrain improvements is built on this tile.
        /// </summary>
        public TerrainAlterations Improvement => (TerrainAlterations) Data & TerrainAlterations.MainImprovementMask;
        #endregion

        #region unit info
        public bool HasOwnUnit => ((LocationFlags) Data & LocationFlags.HasOwnUnit) == LocationFlags.HasOwnUnit;
        public bool HasOwnZoCUnit => ((LocationFlags) Data & LocationFlags.ProjectsMyZoC) != 0;
        public bool HasForeignUnit => ((LocationFlags) Data & LocationFlags.HasOwnUnit) == LocationFlags.HasUnit;
        public bool HasAnyUnit => ((LocationFlags) Data & LocationFlags.HasUnit) != 0;
        public bool HasForeignSubmarine => ((LocationFlags) Data & LocationFlags.ForeignSubmarinePresent) != 0;
        public bool HasForeignStealthUnit => ((LocationFlags) Data & LocationFlags.ForeignStealthUnitPresent) != 0;
        public bool IsInForeignZoC => ((LocationFlags) Data & LocationFlags.InEnemyZoC) != 0;
        #endregion

        #region city info
        public bool HasOwnCity => ((LocationFlags) Data & LocationFlags.HasOwnCity) == LocationFlags.HasOwnCity;
        public bool HasForeignCity => ((LocationFlags) Data & LocationFlags.HasOwnCity) == LocationFlags.HasCity;
        public bool HasAnyCity => ((LocationFlags) Data & LocationFlags.HasCity) != 0;
        #endregion

        /// <summary>
        /// Pointer to (an array of) LocationData that for type safety can only be indexed by LocationId, size 4 bytes.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Size = 4)]
        public unsafe struct Ptr
        {
            private readonly LocationData* Address;

            public LocationData this[LocationId id] => Address[((IId) id).Index];

            public static LocationData* operator +(Ptr ptr, LocationId id) => ptr.Address + ((IId) id).Index;
        }
    }
    #endregion

    #region Models
    /// <summary>
    /// Non-indexing permanent and globally unique id of a unit design, either own or foreign, size 2 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 2)]
    struct GlobalModelId
    {
        private readonly short Id;

        public bool IsValid => Id >= 0;
        public NationId Developer => new NationId(Id >> 12);
        public int SerialNumber => Id & 0xFFF;

        public static bool operator ==(GlobalModelId id1, GlobalModelId id2) => id1.Id == id2.Id;
        public static bool operator !=(GlobalModelId id1, GlobalModelId id2) => id1.Id != id2.Id;

        public override int GetHashCode() => Id;
        public override bool Equals(object obj)
        {
            Debug.Assert(obj is GlobalModelId);
            return Id == ((GlobalModelId) obj).Id;
        }

        public override string ToString() => Id.ToString();
    }

    /// <summary>
    /// The type of a model, including special units, size 1 byte.
    /// </summary>
    enum ModelKind : byte
    {
        OwnDesign = 0x00, ForeignDesign = 0x01, LongBoats = 0x08, TownGuard = 0x10, Glider = 0x11,
        Slaves = 0x21, Settlers = 0x22, SpecialCommando = 0x23, Freight = 0x24,
        // Note the Engineers value does not exist in actual in-game code. Game code uses the Settlers kind for them,
        // with extra speed and terrain resistance. Value chosen here is Settlers with an extra otherwise unused bit
        // that still fits in a byte. The shared memory struct does not use this value, anything that wants to expose
        // it directly from shared memory will have to check an associated stat when kind is Settlers.
        Engineers = 0x62
    }

    /// <summary>
    /// The terrain category ("domain") that a model travels through, size 1 byte.
    /// </summary>
    enum ModelDomain : byte { Ground = 0, Sea = 1, Air = 2 }

    /// <summary>
    /// Certain categorical properties of models, 4 bytes.
    /// </summary>
    [Flags]
    enum ModelFlags : int { ZoC = 1, Civil = 2, DoubleSupport = 4 }

    /// <summary>
    /// Specific attribute, numeric or boolean, of a unit design.
    /// </summary>
    enum ModelProperty : int
    {
        Weapons = 0, Armor = 1, Mobility = 2, SeaTransport = 3, Carrier = 4,
        Turbines = 5, Bombs = 6, Fuel = 7, AirTransport = 8, FirstBooleanProperty = 9, Navigation = 9,
        RadarSonar = 10, Submarine = 11, Artillery = 12, Alpine = 13, SupplyShip = 14,
        Overweight = 15, AirDefence = 16, SpyPlane = 17, SteamPower = 18, NuclearPower = 19,
        JetEngines = 20, Stealth = 21, Fanatic = 22, FirstStrike = 23, PowerOfWill = 24,
        AcademyTraining = 25, LineProduction = 26, MaxValidValue = 26
    }

    #region Own Models
    /// <summary>
    /// Id of own model, permanent as well as indexing, size 2 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 2)]
    struct ModelId : IId
    {
        private readonly short Id;

        public bool IsValid => Id >= 0;

        public ModelId(short id) => Id = id;

        public static ModelId Invalid = new ModelId(-1);

        int IId.Index => Id;

        public static bool operator ==(ModelId id1, ModelId id2) => id1.Id == id2.Id;
        public static bool operator !=(ModelId id1, ModelId id2) => id1.Id != id2.Id;

        public override int GetHashCode() => Id;
        public override bool Equals(object obj)
        {
            Debug.Assert(obj is ModelId);
            return Id == ((ModelId) obj).Id;
        }

        public override string ToString() => Id.ToString();

        /// <summary>
        /// Iterates from the first valid id through the given number of ids.
        /// </summary>
        public static IEnumerable<ModelId> Range(int count)
        {
            for (short id = 0; id < count; id++)
                yield return new ModelId(id);
        }

        /// <summary>
        /// Iterates from the first valid id through the given id.
        /// </summary>
        public static IEnumerable<ModelId> Range(ModelId end)
        {
            for (short id = 0; id <= end.Id; id++)
                yield return new ModelId(id);
        }

        /// <summary>
        /// Iterates from the start id to the end id, including both end points as well as all ids between.
        /// </summary>
        public static IEnumerable<ModelId> Range(ModelId start, ModelId end)
        {
            for (short id = start.Id; id <= end.Id; id++)
                yield return new ModelId(id);
        }
    }

    /// <summary>
    /// List of feature values, whether 0/1 for booleans or arbitrary numbers for numerics, indexed by ModelProperty
    /// for type safety. Size 28 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 28)]
    unsafe struct ModelFeaturesList
    {
        private fixed byte Features[(int) ModelProperty.MaxValidValue / 4 * 4 + 4];

        public byte this[ModelProperty feature]
        {
            get
            {
                fixed (byte* b = Features)
                {
                    return b[(int) feature];
                }
            }
        }

        public static bool operator ==(ModelFeaturesList m1, ModelFeaturesList m2)
        {
            for (int i = 0; i <= (int) ModelProperty.MaxValidValue; i++)
                if (m1.Features[i] != m2.Features[i])
                    return false;
            return true;
        }
        public static bool operator !=(ModelFeaturesList m1, ModelFeaturesList m2)
        {
            for (int i = 0; i <= (int) ModelProperty.MaxValidValue; i++)
                if (m1.Features[i] == m2.Features[i])
                    return false;
            return true;
        }

        public override int GetHashCode()
        {
            fixed (byte* list = Features)
            {
                int hash = 1;
                for (int i = 0; i <= (int) ModelProperty.MaxValidValue; i++)
                    hash = hash * 17 + list[i];
                return hash;
            }
        }
        public override bool Equals(object obj)
        {
            Debug.Assert(obj is ModelFeaturesList);
            return this == (ModelFeaturesList) obj;
        }

        public override string ToString()
        {
            fixed (byte* list = Features)
            {
                List<string> featureStrings = new List<string>();
                for (int i = 0; i < (int) ModelProperty.FirstBooleanProperty; i++)
                    if (list[i] > 0)
                        featureStrings.Add($"{(ModelProperty) i} {list[i]}");
                for (int i = (int) ModelProperty.FirstBooleanProperty; i <= (int) ModelProperty.MaxValidValue; i++)
                    if (list[i] > 0)
                        featureStrings.Add(((ModelProperty) i).ToString());
                return string.Join(", ", featureStrings);
            }
        }
    }

    /// <summary>
    /// Layout in game memory of data about your own unit designs, 68 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 68)]
    unsafe struct ModelData
    {
        public int Status; // 4 bytes, writable
        private readonly int padding1; // 4 bytes, 8 total, server internal data
        public readonly GlobalModelId GlobalId; // 2 bytes, 10 total
        public readonly short TurnOfIntroduction; // 2 bytes, 12 total
        public readonly short UnitsBuilt; // 2 bytes, 14 total
        public readonly short UnitsLost; // 2 bytes, 16 total
        public readonly ModelKind Kind; // 1 byte, 17 total
        public readonly ModelDomain Domain; // 1 byte, 18 total
        public readonly ushort Attack; // 2 bytes, 20 total
        public readonly ushort Defense; // 2 bytes, 22 total
        public readonly ushort Speed; // 2 bytes, 24 total
        public readonly ushort Cost; // 2 bytes, 26 total
        public readonly ushort StrengthMultiplier; // 2 bytes, 28 total
        public readonly byte TransportMultiplier; // 1 byte, 29 total
        public readonly byte CostMultiplier; // 1 byte, 30 total
        public readonly byte Weight; // 1 byte, 31 total
        public readonly byte MaxWeight; // 1 byte, 32 total
        /// <summary>
        /// Bit array identifying which technologies, out of those that improve models of this one's domain, this model
        /// was developed with. I have no idea why anyone would care, so I haven't bothered making the meaning more
        /// accessible.
        /// </summary>
        public readonly int Upgrades; // 4 bytes, 36 total
        public readonly ModelFlags Flags; // 4 bytes, 40 total
        public readonly ModelFeaturesList Features; // 28 bytes, 68 total

        /// <summary>
        /// Pointer to an array of ModelData that for type safety can only be indexed by ModelId, size 4 bytes.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Size = 4)]
        public struct Ptr
        {
            private readonly ModelData* Address;

            public ModelData* this[ModelId id] => Address + ((IId) id).Index;
        }
    }
    #endregion

    #region Foreign Models
    /// <summary>
    /// Id of a foreign model in your own list of foreign models. Permanent as well as indexing, size 2 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 2)]
    struct ForeignModelId : IId
    {
        private readonly short Id;

        public bool IsValid => Id >= 0;

        public ForeignModelId(short id) => Id = id;

        public static ForeignModelId Invalid = new ForeignModelId(-1);

        int IId.Index => Id;

        public static bool operator ==(ForeignModelId id1, ForeignModelId id2) => id1.Id == id2.Id;
        public static bool operator !=(ForeignModelId id1, ForeignModelId id2) => id1.Id != id2.Id;

        public override int GetHashCode() => Id;
        public override bool Equals(object obj)
        {
            Debug.Assert(obj is ForeignModelId);
            return Id == ((ForeignModelId) obj).Id;
        }

        public override string ToString() => Id.ToString();

        /// <summary>
        /// Iterates from the first valid id through the given number of ids.
        /// </summary>
        public static IEnumerable<ForeignModelId> Range(int count)
        {
            for (short id = 0; id < count; id++)
                yield return new ForeignModelId(id);
        }

        /// <summary>
        /// Iterates from the first valid id through the given id.
        /// </summary>
        public static IEnumerable<ForeignModelId> Range(ForeignModelId end)
        {
            for (short id = 0; id <= end.Id; id++)
                yield return new ForeignModelId(id);
        }

        /// <summary>
        /// Iterates from the start id to the end id, including both end points as well as all ids between.
        /// </summary>
        public static IEnumerable<ForeignModelId> Range(ForeignModelId start, ForeignModelId end)
        {
            for (short id = start.Id; id <= end.Id; id++)
                yield return new ForeignModelId(id);
        }
    }

    /// <summary>
    /// Id of a foreign model in its owner's list of own models. Permanent as well as indexing, size 2 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 2)]
    struct ForeignOwnModelId : IId
    {
        private readonly short Id;

        public bool IsValid => Id >= 0;

        public ForeignOwnModelId(short id) => Id = id;

        public static ForeignOwnModelId Invalid = new ForeignOwnModelId(-1);

        int IId.Index => Id;

        public static bool operator ==(ForeignOwnModelId id1, ForeignOwnModelId id2) => id1.Id == id2.Id;
        public static bool operator !=(ForeignOwnModelId id1, ForeignOwnModelId id2) => id1.Id != id2.Id;

        public override int GetHashCode() => Id;
        public override bool Equals(object obj)
        {
            Debug.Assert(obj is ForeignOwnModelId);
            return Id == ((ForeignOwnModelId) obj).Id;
        }

        public override string ToString() => Id.ToString();

        /// <summary>
        /// Iterates from the first valid id through the given number of ids.
        /// </summary>
        public static IEnumerable<ForeignOwnModelId> Range(int count)
        {
            for (short id = 0; id < count; id++)
                yield return new ForeignOwnModelId(id);
        }

        /// <summary>
        /// Iterates from the first valid id through the given id.
        /// </summary>
        public static IEnumerable<ForeignOwnModelId> Range(ForeignOwnModelId end)
        {
            for (short id = 0; id <= end.Id; id++)
                yield return new ForeignOwnModelId(id);
        }

        /// <summary>
        /// Iterates from the start id to the end id, including both end points as well as all ids between.
        /// </summary>
        public static IEnumerable<ForeignOwnModelId> Range(ForeignOwnModelId start, ForeignOwnModelId end)
        {
            for (short id = start.Id; id <= end.Id; id++)
                yield return new ForeignOwnModelId(id);
        }
    }

    /// <summary>
    /// List of boolean feature values only, indexed by ModelProperty for type safety. Size 4 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 4)]
    struct ForeignModelFeaturesList
    {
        private readonly int FeatureBits;

        public bool this[ModelProperty feature] =>
            (FeatureBits & (1 << (feature - ModelProperty.FirstBooleanProperty))) != 0;

        public static bool operator ==(ForeignModelFeaturesList l1, ForeignModelFeaturesList l2) =>
            l1.FeatureBits == l2.FeatureBits;
        public static bool operator !=(ForeignModelFeaturesList l1, ForeignModelFeaturesList l2) =>
            l1.FeatureBits != l2.FeatureBits;

        public override int GetHashCode() => FeatureBits;
        public override bool Equals(object obj)
        {
            Debug.Assert(obj is ForeignModelFeaturesList);
            return FeatureBits == ((ForeignModelFeaturesList) obj).FeatureBits;
        }

        public override string ToString()
        {
            List<ModelProperty> features = new List<ModelProperty>();
            for (var feature = ModelProperty.FirstBooleanProperty; feature <= ModelProperty.MaxValidValue; feature++)
                if (this[feature])
                    features.Add(feature);
            return string.Join(", ", features);
        }
    }

    /// <summary>
    /// Layout in game memory of data about other nations' unit designs.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct ForeignModelData
    {
        public readonly NationId NationId; // 1 byte
        private readonly byte padding1; // 1 byte, 2 total
        public readonly ForeignOwnModelId OwnersModelId; // 2 bytes, 4 total
        public readonly GlobalModelId GlobalId; // 2 bytes, 6 total
        public readonly ModelKind Kind; // 1 byte, 7 total
        public readonly ModelDomain Domain; // 1 byte, 8 total
        public readonly short Attack; // 2 bytes, 10 total
        public readonly short Defense; // 2 bytes, 12 total
        public readonly short Speed; // 2 bytes, 14 total
        public readonly short Cost; // 2 bytes, 16 total
        public readonly byte TransportCapacity; // 1 byte, 17 total
        public readonly byte CarrierCapacityOrFuel; // 1 byte, 18 total
        public readonly short AttackPlusWithBombs; // 2 bytes, 20 total
        public readonly ForeignModelFeaturesList Features; // 4 bytes, 24 total
        /// <summary>
        /// Index in the list of technologies that improve this model's domain of the highest one that this model has.
        /// I have no idea why anyone would care, so I haven't bothered making the meaning more understandable.
        /// </summary>
        public readonly byte MaxUpgrade; // 1 byte, 25 total
        public readonly byte Weight; // 1 byte, 26 total
        public readonly short DestroyedByUs; // 2 bytes, 28 total

        /// <summary>
        /// Pointer to an array of ForeignModelData that for type safety can only be indexed by ForeignModelId, size 4 bytes.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Size = 4)]
        public unsafe struct Ptr
        {
            private readonly ForeignModelData* Address;

            public ForeignModelData* this[ForeignModelId id] => Address + ((IId) id).Index;
        }
    }
    #endregion
    #endregion

    #region Units
    /// <summary>
    /// Task a worker (or any unit for pillaging) can do to alter a location, size 1 byte.
    /// </summary>
    enum Job : byte
    {
        None = 0, BuildRoad = 1, BuildRailRoad = 2, ClearOrDrain = 3, Irrigate = 4, BuildFarmland = 5, Afforest = 6, BuildMine = 7,
        BuildCanal = 8, Transform = 9, BuildFortress = 10, CleanUp = 11, BuildBase = 12, Pillage = 13, BuildCity = 14
    }

    /// <summary>
    /// Boolean properties of a unit that are independent of its model, whether temporary or permanent. Size 2 bytes.
    /// </summary>
    [Flags]
    enum UnitFlags : short
    {
        Fortified = 0x01, BombsLoaded = 0x02, DelayedByMountain = 0x04, Conscripts = 0x08, Withdrawn = 0x10,
        PartOfStack = 0x80
    }

    #region Own Units
    /// <summary>
    /// Id of own unit, size 2 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 2)]
    struct UnitId : IId
    {
        private readonly short Id;

        public bool IsValid => Id >= 0;

        public UnitId(short id) => Id = id;

        int IId.Index => Id;

        public static bool operator ==(UnitId id1, UnitId id2) => id1.Id == id2.Id;
        public static bool operator !=(UnitId id1, UnitId id2) => id1.Id != id2.Id;

        public override int GetHashCode() => Id;
        public override bool Equals(object obj)
        {
            Debug.Assert(obj is UnitId);
            return Id == ((UnitId) obj).Id;
        }

        public override string ToString() => Id.ToString();

        /// <summary>
        /// Iterates from the first valid id through the given number of ids.
        /// </summary>
        public static IEnumerable<UnitId> Range(int count)
        {
            for (short id = 0; id < count; id++)
                yield return new UnitId(id);
        }

        /// <summary>
        /// Iterates from the first valid id through the given id.
        /// </summary>
        public static IEnumerable<UnitId> Range(UnitId end)
        {
            for (short id = 0; id <= end.Id; id++)
                yield return new UnitId(id);
        }

        /// <summary>
        /// Iterates from the start id to the end id, including both end points as well as all ids between.
        /// </summary>
        public static IEnumerable<UnitId> Range(UnitId start, UnitId end)
        {
            for (short id = start.Id; id <= end.Id; id++)
                yield return new UnitId(id);
        }
    }

    /// <summary>
    /// Permanent non-indexing Id of own unit, size 2 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 2)]
    struct PersistentUnitId
    {
        private readonly short Id;

        public bool IsValid => Id >= 0;

        public static bool operator ==(PersistentUnitId id1, PersistentUnitId id2) => id1.Id == id2.Id;
        public static bool operator !=(PersistentUnitId id1, PersistentUnitId id2) => id1.Id != id2.Id;

        public override int GetHashCode() => Id;
        public override bool Equals(object obj)
        {
            Debug.Assert(obj is PersistentUnitId);
            return Id == ((PersistentUnitId) obj).Id;
        }

        public override string ToString() => Id.ToString();
    }

    /// <summary>
    /// Layout in game memory of data for your own units, size 32 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 32)]
    struct UnitData
    {
        public readonly LocationId LocationId; // 4 bytes
        public int Status; // 4 bytes, 8 total, writable
        private readonly int padding; // 4 bytes, 12 total, server internal data
        public readonly PersistentUnitId PersistentId; // 2 bytes, 14 total
        public readonly ModelId ModelId; // 2 bytes, 16 total
        public readonly CityId HomeCityId; // 2 bytes, 18 total
        public readonly UnitId TransportId; // 2 bytes, 20 total
        public readonly short MovementLeft; // 2 bytes, 22 total
        public readonly sbyte Health; // 1 byte, 23 total
        public readonly sbyte Fuel; // 1 byte, 24 total
        public readonly Job Job; // 1 byte, 25 total
        public readonly byte Experience; // 1 byte, 26 total
        public readonly byte TroopLoad; // 1 byte, 27 total
        public readonly byte AirLoad; // 1 byte, 28 total
        public readonly UnitFlags Flags; // 2 bytes, 30 total
        private readonly short padding2; // 2 bytes, 32 total

        /// <summary>
        /// Pointer to an array of UnitData that for type safety can only be indexed by UnitId, size 4 bytes.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Size = 4)]
        public unsafe struct Ptr
        {
            private readonly UnitData* Address;

            public UnitData* this[UnitId id] => Address + ((IId) id).Index;
        }
    }
    #endregion

    #region Foreign Units
    /// <summary>
    /// Layout in game memory of data for other empires' units, size 16 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 16)]
    struct ForeignUnitData
    {
        public readonly LocationId LocationId; // 4 bytes
        public readonly ForeignOwnModelId OwnersModelId; // 2 bytes, 6 total
        public readonly ForeignModelId ModelId; // 2 bytes, 8 total
        public readonly NationId NationId; // 1 byte, 9 total
        public readonly sbyte Health; // 1 byte, 10 total
        public readonly sbyte Fuel; // 1 byte, 11 total
        public readonly Job Job; // 1 byte, 12 total
        public readonly byte Experience; // 1 byte, 13 total
        public readonly sbyte Load; // 1 byte, 14 total
        public readonly UnitFlags Flags; // 2 bytes, 16 total
    }
    #endregion
    #endregion

    #region Nations
    /// <summary>
    /// Id of a nation/empire, permanent as well as indexing, size 1 byte.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 1)]
    struct NationId : IId
    {
        private readonly sbyte Id;

        public bool IsValid => Id >= 0;

        public NationId(sbyte id) => Id = id;

        public NationId(int id) => Id = (sbyte)id;

        int IId.Index => Id;

        public static bool operator ==(NationId id1, NationId id2) => id1.Id == id2.Id;
        public static bool operator !=(NationId id1, NationId id2) => id1.Id != id2.Id;

        public override int GetHashCode() => Id;
        public override bool Equals(object obj)
        {
            Debug.Assert(obj is NationId);
            return Id == ((NationId) obj).Id;
        }

        public override string ToString() => Id.ToString();

        /// <summary>
        /// Iterates from the first valid id through the given number of ids.
        /// </summary>
        public static IEnumerable<NationId> Range(int count)
        {
            for (sbyte id = 0; id < count; id++)
                yield return new NationId(id);
        }

        /// <summary>
        /// Iterates from the first valid id through the given id.
        /// </summary>
        public static IEnumerable<NationId> Range(NationId end)
        {
            for (sbyte id = 0; id <= end.Id; id++)
                yield return new NationId(id);
        }

        /// <summary>
        /// Iterates from the start id to the end id, including both end points as well as all ids between.
        /// </summary>
        public static IEnumerable<NationId> Range(NationId start, NationId end)
        {
            for (sbyte id = start.Id; id <= end.Id; id++)
                yield return new NationId(id);
        }

        /// <summary>
        /// Bit flags indexed by nation id, size 4 bytes.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Size = 4)]
        public struct BitArray
        {
            private readonly int Bits;

            public bool this[NationId id] => (Bits & (1 << ((IId) id).Index)) != 0;

            public int Count
            {
                get
                {
                    // magic (http://graphics.stanford.edu/~seander/bithacks.html#CountBitsSetParallel)
                    unchecked
                    {
                        int temp = Bits - ((Bits >> 1) & 0x55555555);
                        temp = (temp & 0x33333333) + ((temp >> 2) & 0x33333333);
                        return ((temp + (temp >> 4) & 0xF0F0F0F) * 0x1010101) >> 24;
                    }

                }
            }
        }

        /// <summary>
        /// Fixed size array of integers that for type safety can only be indexed by NationId, large enough to allow
        /// for all valid values of NationId. Size 60 bytes.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Size = 60)]
        public unsafe struct IntArray
        {
            private fixed int Data[Cevo.MaxNumberOfNations];

            public int this[NationId id]
            {
                get
                {
                    fixed (int* i = Data)
                    {
                        return i[((IId) id).Index];
                    }
                }
            }
        }
    }

    /// <summary>
    /// What level of treaty, if any, one empire has with another. Size 4 bytes
    /// </summary>
    enum Relation : int { NoContact = -1, NoTreaty = 0, Peace = 2, FriendlyContact = 3, Alliance = 4, Identity = 5 }

    /// <summary>
    /// Nominally supposed to represent how one empire regards its relations with another. Changes only if specifically
    /// set by the AI and has no game mechanics effect. It is no longer displayed in the game UI, and is considered
    /// obsolete. Size 4 bytes.
    /// </summary>
    enum Attitude : int
    {
        Hostile = 0, Icy = 1, Uncooperative = 2, Neutral = 3, Receptive = 4, Cordial = 5, Enthusiastic = 6
    }

    /// <summary>
    /// The form of government an empire has, size 4 bytes.
    /// </summary>
    enum Government : int
    {
        Anarchy = 0, Despotism = 1, Monarchy = 2, Republic = 3, Fundamentalism = 4, Communism = 5, Democracy = 6,
        FutureSociety = 7
    }

    /// <summary>
    /// Technological research available in game. Size 4 bytes.
    /// </summary>
    enum Advance : int
    {
        None = -1, MilitaryResearch = 0x800,

        AdvancedFlight = 0, AmphibiousWarfare = 1, Astronomy = 2, AtomicTheory = 3, Automobile = 4,
        Ballistics = 5, Banking = 6, BridgeBuilding = 7, BronzeWorking = 8, CeremonialBurial = 9,
        Chemistry = 10, Chivalry = 11, Composites = 12, CodeOfLaws = 13, CombinedArms = 14,
        CombustionEngine = 15, Communism = 16, Computers = 17, Conscription = 18, Construction = 19,
        TheCorporation = 20, SpaceFlight = 21, Currency = 22, Democracy = 23, Economics = 24,
        Electricity = 25, Electronics = 26, Engineering = 27, Environmentalism = 28, TheWheel = 29,
        Explosives = 30, Flight = 31, Espionage = 32, Gunpowder = 33, HorsebackRiding = 34,
        ImpulseDrive = 35, Industrialization = 36, SmartWeapons = 37, Invention = 38, IronWorking = 39,
        TheLaser = 40, NuclearPower = 41, Literature = 42, TheInternet = 43, Magnetism = 44,
        MapMaking = 45, Masonry = 46, MassProduction = 47, Mathematics = 48, Medicine = 49,
        Metallurgy = 50, Miniaturization = 51, MobileWarfare = 52, Monarchy = 53, Mysticism = 54,
        Navigation = 55, NuclearFission = 56, Philosophy = 57, Physics = 58, Plastics = 59,
        Poetry = 60, Pottery = 61, Radio = 62, Recycling = 63, Refrigeration = 64,
        Monotheism = 65, TheRepublic = 66, Robotics = 67, Rocketry = 68, Railroad = 69,
        Sanitation = 70, Science = 71, Writing = 72, Seafaring = 73, SelfContainedEnvironment = 74,
        Stealth = 75, SteamEngine = 76, Steel = 77, SyntheticFood = 78, Tactics = 79,
        Theology = 80, TheoryOfGravity = 81, Trade = 82, TransstellarColonization = 83, University = 84,
        AdvancedRocketry = 85, WarriorCode = 86, Alphabet = 87, Polytheism = 88, Refining = 89,
        ComputingTechnology = 90, NanoTechnology = 91, MaterialTechnology = 92, ArtificialIntelligence = 93,

        FirstCommon = 0, LastCommon = 89, FirstFuture = 90, LastFuture = 93,
        MaxValidValue /* (not counting military) */ = 93
    }

    /// <summary>
    /// The state of research for a technology, size 1 byte.
    /// </summary>
    enum AdvanceStatus : sbyte
    {
        NotResearched = -2, HalfResearched = -1, Researched = 0
    }

    /// <summary>
    /// Cheat options that can be turned on or off, size 4 bytes.
    /// </summary>
    [Flags]
    enum CheatOptions : int
    {
        None = 0,
        AllTechs = 0x0001,
        CitiesBuildFast = 0x0002,
        ResearchFast = 0x0004,
        CitiesGrowFast = 0x0008,
        SeeAll = 0x0010,
        ContactAll = 0x0020,
        FreeShipResources = 0x0040
    }

    /// <summary>
    /// Flags for whether certain events happened this turn. Some of them can happen multiple times, but each flag is
    /// just on/off. Size 4 bytes.
    /// </summary>
    [Flags]
    enum EmpireEvents : int
    {
        ResearchComplete = 0x0001,
        StealTech = 0x0002, // steal with Temple of Zeus
        AnarchyOver = 0x0008,
        GliderLost = 0x0100,
        AircraftLost = 0x0200,
        PeaceViolation = 0x0400,
        PeaceEvacuation = 0x0800,
        /// <summary>
        /// ShipComplete is only sent to player 0, the supervisor/tournament runner or main human player, at which point
        /// the game ends.
        /// </summary>
        ShipComplete = 0x2000,
        /// <summary>
        /// TimeUp is only sent to player 0, the supervisor/tournament runner or main human player, at which point the
        /// game ends.
        /// </summary>
        TimeUp = 0x4000,
        // When the destroyed player's turn comes up, it receives a cTurn command with this event. This is the last
        // command it ever receives, and the standard handling of it is to return without calling Empire.OnTurn.
        Destroyed = 0x8000,
        GameOverMask = 0xF000
    }

    /// <summary>
    /// Layout in game memory of one empire's relations with each of the other empires, size 60 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 60)]
    unsafe struct TreatyList
    {
        // actually an array of Relation but C# doesn't allow combining that with fixed
        private fixed int TreatyVals[Cevo.MaxNumberOfNations];

        public Relation this[NationId id]
        {
            get
            {
                fixed (int* i = TreatyVals)
                {
                    return (Relation) i[((IId) id).Index];
                }
            }
        }

        public override string ToString()
        {
            fixed (int* list = TreatyVals)
            {
                var relations = new List<string>();
                foreach (NationId nationId in NationId.Range(Cevo.MaxNumberOfNations))
                    relations.Add($"{nationId}: {(Relation) list[((IId) nationId).Index]}");
                return string.Join(", ", relations);
            }
        }
    }

    /// <summary>
    /// Layout in game memory of a list of the research states of every technology in the game, whether for your own
    /// empire or a foreign one, indexed by Advance for type safety. Size 96 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 96)]
    unsafe struct TechStatusList
    {
        // actually an array of AdvanceStatus, but C# doesn't allow combining that with fixed
        private fixed sbyte Techs[(int) Advance.MaxValidValue / 4 * 4 + 4];

        public AdvanceStatus this[Advance advance]
        {
            get
            {
                fixed (sbyte* b = Techs)
                {
                    return (AdvanceStatus) b[(int)advance];
                }
            }
        }

        public override string ToString()
        {
            fixed (sbyte* list = Techs)
            {
                var statuses = new List<string>();
                for (Advance advance = 0; advance <= Advance.MaxValidValue; advance++)
                    statuses.Add($"{advance}: {(AdvanceStatus) list[(int) advance]}");
                return string.Join(", ", statuses);
            }
        }
    }

    #region Own Empire
    /// <summary>
    /// Pointer to an array of turn numbers as shorts that are indexed by LocationId for type safety. Used for tracking
    /// what turn each location was last observed on. Size 4 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 4)]
    unsafe struct TurnLastObservedPtr
    {
        private readonly short* Ptr;

        public short this[LocationId id]
        {
            get => Ptr[((IId) id).Index];
            set => Ptr[((IId) id).Index] = value;
        }

        public static short* operator +(TurnLastObservedPtr ptr, LocationId id) => ptr.Ptr + ((IId) id).Index;
    }

    /// <summary>
    /// Pointer to an array of nation ids that for type safety can only be indexed by LocationId. Used for tracking
    /// nation territories. Size 4 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 4)]
    unsafe struct TerritoryPtr
    {
        private readonly NationId* Address;

        public NationId this[LocationId id] => Address[((IId) id).Index];
    }

    /// <summary>
    /// Layout in game memory of information about a specific wonder, size 8 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 8)]
    struct WonderInfo
    {
        public readonly PersistentCityId CityId; // 2 bytes
        private readonly short padding; // 2 bytes, 4 total
        public readonly NationId OwnerId; // 1 byte, 5 total
        private readonly byte padding2; // 1 byte, 6 total
        private readonly short padding3; // 2 bytes, 8 total

        public bool IsDestroyed => CityId == PersistentCityId.WonderDestroyedId;
        public bool CanBeBuilt => CityId == PersistentCityId.WonderUnbuiltId;
        public bool Exists => CityId.IsValid;
        public bool IsEffective => OwnerId.IsValid;
        public bool IsExpired => Exists && !IsEffective;

        public override string ToString() =>
            CanBeBuilt ? "Not yet built"
            : IsDestroyed ? "Destroyed"
            : IsEffective ? $"City {CityId}, Owner {OwnerId}"
            : $"City {CityId}, Expired";
    }

    /// <summary>
    /// List of information about all wonders, indexed by Building for type safety. Size 224 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 224)]
    unsafe struct WondersList
    {
        private fixed long Wonders[28]; // really WonderInfo type, but C# won't allow that with fixed

        public WonderInfo this[Building wonder]
        {
            get
            {
                Debug.Assert(wonder < Building.WonderRange);
                fixed (void* v = Wonders)
                {
                    return ((WonderInfo*) v)[(int) wonder];
                }
            }
        }

        public override string ToString()
        {
            fixed (long* list = Wonders)
            {
                var wondersList = new List<string>();
                for (Building wonder = 0; wonder <= Building.HighestWonderValue; wonder++)
                    wondersList.Add($"{wonder}: {((WonderInfo*) list)[(int) wonder]}");
                return string.Join(", ", wondersList);
            }
        }
    }

    /// <summary>
    /// Layout in game memory of information about a colony ship's construction, size 12 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 12)]
    struct ColonyShipParts
    {
        public readonly int ComponentCount;
        public readonly int PowerCount;
        public readonly int HabitationCount;
        public int this[Building part]
        {
            get
            {
                switch (part)
                {
                    case Building.ColonyShipComponent: return ComponentCount;
                    case Building.PowerModule: return PowerCount;
                    case Building.HabitationModule: return HabitationCount;
                    default: return 0;
                }
            }
        }

        public bool IsComplete => ComponentCount >= Cevo.CompleteColonyShip.ComponentCount
                                  && PowerCount >= Cevo.CompleteColonyShip.PowerCount
                                  && HabitationCount >= Cevo.CompleteColonyShip.HabitationCount;

        public ColonyShipParts(int componentCount, int powerCount, int habitationCount) =>
            (ComponentCount, PowerCount, HabitationCount) = (componentCount, powerCount, habitationCount);

        public override string ToString() => $"C{ComponentCount} P{PowerCount} H{HabitationCount}";
    }

    /// <summary>
    /// Layout in game memory of the list of all colony ships in the game, indexed by NationId for type safety. Size
    /// 180 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 180)]
    unsafe struct ColonyShipsList
    {
        private fixed int Ships[3 * Cevo.MaxNumberOfNations]; // really ColonyShipParts, but C# won't allow that with fixed

        public ColonyShipParts this[NationId nation]
        {
            get
            {
                Debug.Assert(nation.IsValid);
                fixed (void* v = Ships)
                {
                    return ((ColonyShipParts*) v)[((IId) nation).Index];
                }
            }
        }

        public override string ToString()
        {
            fixed (void* list = Ships)
            {
                var shipsList = new List<string>();
                foreach (NationId nationId in NationId.Range(Cevo.MaxNumberOfNations))
                    shipsList.Add($"{nationId}: {((ColonyShipParts*) list)[((IId) nationId).Index]}");
                return string.Join(", ", shipsList);
            }
        }
    }

    /// <summary>
    /// Layout in game memory of the record of which State Improvements you currently have, indexed for type safety by
    /// Building. Size 44 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 44)]
    unsafe struct StateImprovementsList
    {
        private fixed bool Built[(Building.MaxValidValue - Building.WonderRange) / 4 * 4 + 4];

        public bool IsBuilt(Building building)
        {
            Debug.Assert(building >= Building.FirstStateImprovement && building <= Building.LastStateImprovement);
            fixed (bool* b = Built)
            {
                return b[building - Building.WonderRange];
            }
        }

        public override string ToString()
        {
            fixed (bool* b = Built)
            {
                var list = new List<Building>();
                for (Building building = Building.FirstStateImprovement;
                    building <= Building.LastStateImprovement;
                    building++)
                    if (b[building - Building.WonderRange])
                        list.Add(building);
                return string.Join(", ", list);
            }
        }
    }

    /// <summary>
    /// Flags used in game memory about a past battle, size 1 byte.
    /// </summary>
    [Flags]
    enum BattleRecordFlags : byte { EnemyAttack = 1, MyUnitLost = 2, EnemyUnitLost = 4 }
    /// <summary>
    /// Whether we attacked or defended in a battle.
    /// </summary>
    enum BattleType { Attack, Defend }
    /// <summary>
    /// Which unit(s) died in a battle.
    /// </summary>
    enum BattleResult { Loss, Victory, BothDead }

    /// <summary>
    /// Layout in game memory of information about a past battle, size 16 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 16)]
    struct BattleRecordData
    {
        public readonly NationId EnemyId; // 1 byte
        private readonly BattleRecordFlags Flags; // 1 byte, 2 total
        public readonly short Turn; // 2 bytes, 4 total
        public readonly ModelId ModelId; // 2 bytes, 6 total
        public readonly ForeignModelId EnemyModelId; // 2 bytes, 8 total
        public readonly LocationId DefenderLocationId; // 4 bytes, 12 total
        public readonly LocationId AttackerLocationId; // 4 bytes, 16 total

        public BattleType BattleType =>
            (Flags & BattleRecordFlags.EnemyAttack) == 0 ? BattleType.Attack : BattleType.Defend;

        public BattleResult BattleResult => (Flags & BattleRecordFlags.MyUnitLost) == 0 ? BattleResult.Victory
                                          : (Flags & BattleRecordFlags.EnemyUnitLost) == 0 ? BattleResult.Loss
                                          : BattleResult.BothDead;

        public override string ToString()
        {
            string verb = BattleType == BattleType.Attack ? "attacked" : "defended against";
            string outcome = BattleResult == BattleResult.Victory ? "won"
                : BattleResult == BattleResult.Loss ? "lost"
                : "tied";
            return $"Our model {ModelId} {verb} enemy {EnemyId} model {EnemyModelId} in location " +
                   $"{DefenderLocationId}, attacking from {AttackerLocationId} on turn {Turn}. We {outcome}.";
        }
    }

    /// <summary>
    /// Layout in game memory of a full record of all battles we have had, including the count of how many battles.
    /// Size 8 bytes, because the array is referenced by pointer rather than inline.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 8)]
    unsafe struct BattleHistoryData : IEnumerable<BattleRecordData>
    {
        public readonly int Count;
        private readonly BattleRecordData* Ptr;

        public BattleRecordData* this[int i]
        {
            get
            {
                Debug.Assert(i < Count);
                return Ptr + i;
            }
        }

        private BattleRecordData GetRecord(int i) => Ptr[i];

        public IEnumerator<BattleRecordData> GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
                yield return GetRecord(i);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => string.Join(" ", this);
    }

    /// <summary>
    /// Pointer to a writable array of integers each associated with a location, indexed by LocationId for type safety.
    /// Used for debugging information. Size 4 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 4)]
    unsafe struct DebugMap
    {
        private readonly int* Ptr;

        public int this[LocationId id]
        {
            get => Ptr[((IId) id).Index];
            set => Ptr[((IId) id).Index] = value;
        }
    }

    /// <summary>
    /// Layout in game memory of the entry point for all per-empire data that does not require server calls to fetch.
    /// Size 2048 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 2048)]
    unsafe struct EmpireData
    {
        public readonly Persistent.MyData* CustomData; // 4 bytes
        public readonly LocationData.Ptr MapData; // 4 bytes, 8 total
        public readonly TurnLastObservedPtr LocationLastObservedTurns; // 4 bytes, 12 total
        public readonly TerritoryPtr Territory; // 4 bytes, 16 total
        public readonly UnitData.Ptr UnitsData; // 4 bytes, 20 total
        public readonly CityData.Ptr CitiesData; // 4 bytes, 24 total
        public readonly ModelData.Ptr ModelsData; // 4 bytes, 28 total
        public readonly ForeignUnitData* ForeignUnitsData; // 4 bytes, 32 total
        public readonly ForeignCityData.Ptr ForeignCitiesData; // 4 bytes, 36 total
        public readonly ForeignModelData.Ptr ForeignModelsData; // 4 bytes, 40 total
        public readonly EmpireReport.PtrArray EmpireReports; // 60 bytes, 100 total
        public readonly CheatOptions CheatOptions; // 4 bytes, 104 total
        public readonly int CurrentTurn; // 4 bytes, 108 total
        public readonly NationId.BitArray AliveNations; // 4 bytes, 112 total
        public readonly EmpireEvents EmpireEvents; // 4 bytes, 116 total
        public readonly int AnarchyStartedTurn; // 4 bytes, 120 total, <0 if not in anarchy
        public readonly int Credibility; // 4 bytes, 124 total
        public readonly int MaxCredibility; // 4 bytes, 128 total
        public readonly int NumUnits; // 4 bytes, 132 total
        public readonly int NumCities; // 4 bytes, 136 total
        public readonly int NumModels; // 4 bytes, 140 total
        public readonly int NumForeignDefendedLocations; // 4 bytes, 144 total
        public readonly int NumForeignCities; // 4 bytes, 148 total
        public readonly int NumForeignModels; // 4 bytes, 152 total
        public readonly Government Government; // 4 bytes, 156 total
        public readonly int Money; // 4 bytes, 160 total
        public readonly int TaxRate; // 4 bytes, 164 total
        public readonly int WealthRate; // 4 bytes, 168 total
        public readonly int ResearchPile; // 4 bytes, 172 total
        public readonly Advance Researching; // 4 bytes, 176 total
        public ModelData ResearchingModel; // 68 bytes, 244 total, valid only if Researching = MilitaryResearch
        public readonly TechStatusList Technologies; // 96 bytes, 340 total
        private fixed int Attitudes[Cevo.MaxNumberOfNations]; // 60 bytes, 400 total, technically available, but no game mechanics effect
        public readonly TreatyList Treaties; // 60 bytes, 460 total
        public readonly NationId.IntArray EvacuationStartTurns; // 60 bytes, 520 total
        private fixed int padding1[2 * Cevo.MaxNumberOfNations]; // 120 bytes, 640 total, Tribute information, no longer used
        public readonly WondersList Wonders; // 224 bytes, 864 total
        public readonly ColonyShipsList ColonyShips; // 180 bytes, 1044 total
        public readonly StateImprovementsList StateImprovements; // 44 bytes, 1088 total
        public BattleHistoryData BattleHistoryData; // 8 bytes, 1096 total
        private readonly byte* BorderHelper; // 4 bytes, 1100 total, no longer used
        public readonly NationId.IntArray LastCancelTreatyTurns; // 60 bytes, 1160 total
        public readonly int OracleIncome; // 4 bytes, 1164 total
        public DebugMap DebugMap; // 4 bytes, 1168 total, writable
        private fixed byte padding[880]; // 880 bytes, 2048 total, reserved for future expansion

        /// <summary>
        /// Array of pointers to EmpireData, indexed by NationId for type safety. Size 60 bytes.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Size = 60)]
        public struct PtrArray
        {
            // really an array of EmpireData* but C# won't allow using that in a fixed size array
            private fixed int Pointers[Cevo.MaxNumberOfNations];

            public EmpireData* this[NationId id]
            {
                get
                {
                    fixed (int* i = Pointers)
                    {
                        return (EmpireData*) i[((IId) id).Index];
                    }
                }
            }
        }
    }
    #endregion

    #region Foreign Empires
    /// <summary>
    /// Layout in game memory of a full count of how many of each model a foreign empire has, if available, including
    /// the count of how many models the empire has. Size 2052 bytes, though usually only a small portion of that will
    /// be used.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 2052)]
    unsafe struct ForeignUnitCountArray
    {
        public readonly int Count;
        // Max models per player is currently 256, but comments in source suggest it could be increased up to 1024.
        private fixed short Counts[1024];

        public short this[ForeignOwnModelId id]
        {
            get
            {
                Debug.Assert(((IId) id).Index < Count);
                fixed (short* s = Counts)
                {
                    return s[((IId) id).Index];
                }
            }
        }
    }

    /// <summary>
    /// Layout in game memory of report information on each empire, size 2244 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 2244)]
    struct EmpireReport
    {
        public readonly int TurnOfContact; // 4 bytes, last time you talked with them
        public readonly int TurnOfDossier; // 4 bytes, 8 total
        public readonly int TurnOfMilitaryReport; // 4 bytes, 12 total
        private readonly Attitude Attitude; // 4 bytes, 16 total, technically available, but no game mechanics effect
        public readonly int Credibility; // 4 bytes, 20 total, 0-100, always current, even for empires you haven't met
        public readonly TreatyList Treaties; // 60 bytes, 80 total, last updated on TurnOfDossier
        public readonly Government Government; // 4 bytes, 84 total, last updated on TurnOfDossier
        public readonly int Money; // 4 bytes, 88 total, last updated on TurnOfDossier
        public readonly Advance Researching; // 4 bytes, 92 total, last updated on TurnOfDossier
        public readonly int ResearchPile; // 4 bytes, 96 total, last updated on TurnOfDossier
        public readonly TechStatusList Technologies; // 96 bytes, 192 total, last updated on TurnOfDossier
        public readonly ForeignUnitCountArray UnitCounts; // 2052 bytes, 2244 total last updated on TurnOfMilitaryReport;

        /// <summary>
        /// Array of pointers to EmpireReport, indexed by NationId for type safety. Size 60 bytes.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Size = 60)]
        public unsafe struct PtrArray
        {
            // really an array of EmpireReport* but C# won't allow using that in a fixed size array
            private fixed int Pointers[Cevo.MaxNumberOfNations];

            public EmpireReport* this[NationId id]
            {
                get
                {
                    fixed (int* i = Pointers)
                    {
                        return (EmpireReport*) i[((IId) id).Index];
                    }
                }
            }
        }
    }
    #endregion
    #endregion
}
