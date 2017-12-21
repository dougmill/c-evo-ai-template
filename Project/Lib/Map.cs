using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using AI;

namespace CevoAILib
{
    enum SpecialResourceType { None = 0, Basic = 1, Science = 2 }

    sealed unsafe class Map
    {
        private readonly AEmpire TheEmpire;
        public readonly int Size;
        public readonly int SizeX;
        public readonly int SizeY;
        /// <summary>
        /// The percentage of the world that is land, for randomly generated maps.
        /// </summary>
        public readonly int LandMass;

        public Map(AEmpire empire, int sizeX, int sizeY, int landMass)
        {
            TheEmpire = empire;
            Size = sizeX * sizeY;
            SizeX = sizeX;
            SizeY = sizeY >> 1;
            LandMass = landMass;
            Ground = empire.Data->MapData;
            ObservedLast = empire.Data->LocationLastObservedTurns;
            Territory = empire.Data->Territory;

            NeighborLocations = new IdIndexedArray<LocationId, Location[]>(Size);
            NeighborOtherLocations = new IdIndexedArray<LocationId, OtherLocation[]>(Size);
            Distance5AreaLocations = new IdIndexedArray<LocationId, Location[]>(Size);
            Distance5AreaOtherLocations = new IdIndexedArray<LocationId, OtherLocation[]>(Size);
            JobInfo = new IdIndexedArray<LocationId, JobWorkRemaining[]>(Size);
        }

        public void InitLocationsCache()
        {
            for (int i = 0; i < Size; i++)
            {
                var location = new Location(TheEmpire, new LocationId(i));
                NeighborLocations[location.Id] = (from rc in RC.NeighborRelativeCoordinates
                                                  where (location + rc).IsValid
                                                  select location + rc).ToArray();
                NeighborOtherLocations[location.Id] = (from otherLocation in NeighborLocations[location.Id]
                                                       select new OtherLocation(otherLocation, otherLocation - location)).ToArray();
                Distance5AreaLocations[location.Id] = (from rc in RC.Distance5AreaRelativeCoordinates
                                                       where (location + rc).IsValid
                                                       select location + rc).ToArray();
                Distance5AreaOtherLocations[location.Id] = (from otherLocation in Distance5AreaLocations[location.Id]
                                                            select new OtherLocation(otherLocation, otherLocation - location))
                                                            .ToArray();
            }
        }

        /// <summary>
        /// during own turn, trigger refresh of display of all values set using Location.SetDebugDisplay
        /// (not necessary in the end of the turn because refresh happens automatically then)
        /// </summary>
        public void RefreshDebugDisplay()
        {
            TheEmpire.Play(Protocol.sRefreshDebugMap);
        }

        #region template internal stuff
        /// <summary>
        /// INTERNAL - only access from CevoAILib classes!
        /// </summary>
        public readonly IdIndexedArray<LocationId, Location[]> NeighborLocations;

        /// <summary>
        /// INTERNAL - only access from CevoAILib classes!
        /// </summary>
        public readonly IdIndexedArray<LocationId, OtherLocation[]> NeighborOtherLocations;

        /// <summary>
        /// INTERNAL - only access from CevoAILib classes!
        /// </summary>
        public readonly IdIndexedArray<LocationId, Location[]> Distance5AreaLocations;

        /// <summary>
        /// INTERNAL - only access from CevoAILib classes!
        /// </summary>
        public readonly IdIndexedArray<LocationId, OtherLocation[]> Distance5AreaOtherLocations;

        /// <summary>
        /// INTERNAL - only access from CevoAILib classes!
        /// </summary>
        public readonly LocationData.Ptr Ground;

        /// <summary>
        /// INTERNAL - only access from CevoAILib classes!
        /// </summary>
        public readonly TurnLastObservedPtr ObservedLast;

        /// <summary>
        /// INTERNAL - only access from CevoAILib classes!
        /// </summary>
        public readonly TerritoryPtr Territory;

        /// <summary>
        /// INTERNAL - only access from CevoAILib classes!
        /// </summary>
        public readonly IdIndexedArray<LocationId, JobWorkRemaining[]> JobInfo;
        #endregion
    }

    struct OtherLocation
    {
        public readonly Location Location;
        /// <summary>
        /// The displacement of Location relative to the base location this was calculated from.
        /// </summary>
        public readonly RC RC;

        // location is the other location, RC the coordinate relative to an origin tile not stored
        public OtherLocation(Location location, RC rc)
        {
            Location = location;
            RC = rc;
        }
    }

    struct JobWorkRemaining
    {
        public readonly bool IsStarted;
        public readonly int Amount;

        public JobWorkRemaining(int required, int done) => (IsStarted, Amount) = (done != 0, required - done);
        private JobWorkRemaining(bool isStarted, int amount) => (IsStarted, Amount) = (isStarted, amount);
        public JobWorkRemaining Minus(int amount) => new JobWorkRemaining(true, Amount > amount ? Amount - amount : 0);
    }

    unsafe struct Location
    {
        private readonly AEmpire TheEmpire;
        public readonly LocationId Id;
        private readonly LocationData* Data;

        public Location(AEmpire empire, LocationId id)
        {
            TheEmpire = empire;
            Id = id;
            Data = TheEmpire.Map.Ground + id;
        }

        public override string ToString() => Id.ToString();

        public int YCoordinate => ((IId) Id).Index / TheEmpire.Map.SizeX;

        public int XCoordinate
        {
            get
            {
                int y = YCoordinate;
                return (((IId) Id).Index - y * TheEmpire.Map.SizeX) * 2 + (y & 1);
            }
        }

        /// <summary>
        /// See RC.TileDisplacements for what a "tile" means here.
        /// </summary>
        public RC PositionInTile
        {
            get
            {
                int wrap = TheEmpire.Map.SizeX;
                int index = ((IId) Id).Index;
                int y = index / wrap;
                int x = index - y * wrap;
                int dy = y % 16;
                int dx = ((x - 4 * (y / 16) + 300) % 10) * 2 + (dy & 1);
                return new RC(dx, dy);
            }
        }

        public static bool operator ==(Location location1, Location location2) => location1.Id == location2.Id;
        public static bool operator !=(Location location1, Location location2) => location1.Id != location2.Id;

        public override int GetHashCode() => Id.GetHashCode();
        public override bool Equals(object obj)
        {
            Debug.Assert(obj is Location);
            return Id == ((Location) obj).Id;
        }

        public static RC operator -(Location location1, Location location2)
        {
            if (!location1.IsValid || !location2.IsValid)
                throw new ArgumentException(
                    $"Attempted to find difference between {location1} and {location2} but at least one is not valid.");
            int wrap = location2.TheEmpire.Map.SizeX;
            int index1 = ((IId) location1.Id).Index;
            int y1 = index1 / wrap;
            int x1 = index1 - y1 * wrap;
            int index2 = ((IId) location2.Id).Index;
            int y2 = index2 / wrap;
            int x2 = index2 - y2 * wrap;
            int dx = (x1 * 2 + (y1 & 1) - (x2 * 2 + (y2 & 1)) + 3 * wrap) % (2 * wrap) - wrap;
            int dy = y1 - y2;
            return new RC(dx, dy);
        }

        public static Location operator +(Location location, RC RC)
        {
            if (!location.IsValid)
                throw new ArgumentException($"Attempted to add RC {RC} to invalid location {location}.");
            int wrap = location.TheEmpire.Map.SizeX;
            int index = ((IId) location.Id).Index;
            int y0 = index / wrap;
            int otherLocationIndex = (index + ((RC.x + (y0 & 1) + wrap * 2) >> 1)) % wrap + wrap * (y0 + RC.y);
            if (otherLocationIndex >= location.TheEmpire.Map.Size)
                otherLocationIndex = -0x1000;
            return new Location(location.TheEmpire, new LocationId(otherLocationIndex));
        }

        public static Location operator -(Location location, RC RC) => location + new RC(-RC.x, -RC.y);

        /// <summary>
        /// true if location is on the map, false if beyond upper or lower edge of the map
        /// </summary>
        public bool IsValid => Id.IsValid;

        /// <summary>
        /// set number shown on debug map
        /// </summary>
        /// <param name="value">number, 0 for nothing</param>
        public void SetDebugDisplay(int value)
        {
            if (Id.IsValid)
                TheEmpire.Data->DebugMap[Id] = value;
        }

        /// <summary>
        /// Set of all adjacent locations.
        ///
        /// All locations returned are on the map. Usually the array has 8 elements, but it's less if the location is
        /// close to the upper or lower edge of the map. The result is in order from north to south, then west to east.
        ///
        /// Treat the returned array as immutable. It is cached, so alterations will affect the return value of future
        /// calls.
        /// </summary>
        public Location[] Neighbors => TheEmpire.Map.NeighborLocations[Id];

        /// <summary>
        /// Set of all adjacent locations with their respective relative coordinates.
        ///
        /// All locations returned are on the map. Usually the array has 8 elements, but it's less if the location is
        /// close to the upper or lower edge of the map. The result is in order from north to south, then west to east.
        ///
        /// Treat the returned array as immutable. It is cached, so alterations will affect the return value of future
        /// calls.
        /// </summary>
        public OtherLocation[] NeighborsAndOffsets => TheEmpire.Map.NeighborOtherLocations[Id];

        /// <summary>
        /// Set of all locations with a distance of 5 or less, including the location itself. This is the city radius,
        /// and also it's the extended visibility radius of units.
        ///
        /// All locations returned are on the map. Usually the array has 21 elements, but it's less if the location is
        /// close to the upper or lower edge of the map. The result is in order from north to south, then west to east.
        ///
        /// Treat the returned array as immutable. It is cached, so alterations will affect the return value of future
        /// calls.
        /// </summary>
        public Location[] Distance5Area => TheEmpire.Map.Distance5AreaLocations[Id];

        /// <summary>
        /// Set of all locations with a distance of 5 or less, including the location itself, with their relative
        /// coordinates. This is the city radius, and also it's the extended visibility radius of units.
        ///
        /// All locations returned are on the map. Usually the array has 21 elements, but it's less if the location is
        /// close to the upper or lower edge of the map. The result is in order from north to south, then west to east.
        ///
        /// Treat the returned array as immutable. It is cached, so alterations will affect the return value of future
        /// calls.
        /// </summary>
        public OtherLocation[] Distance5AreaAndOffsets => TheEmpire.Map.Distance5AreaOtherLocations[Id];
        
        /// <summary>
        /// whether this location is adjacent to another one
        /// </summary>
        /// <param name="otherLocation">the other location</param>
        /// <returns>true if adjacent, false if not adjacent, also false if identical</returns>
        public bool IsNeighborOf(Location otherLocation) => Neighbors.Contains(otherLocation);

        #region basic info
        /// <summary>
        /// Simulation of latitude, returns value between -90 and 90.
        /// (May be used for strategic consideration and climate estimation.)
        /// </summary>
        public int Latitude => 90 - YCoordinate * 180 / ((TheEmpire.Map.SizeY << 1) - 1);

        /// <summary>
        /// whether the tile at this location was visible to an own unit or city at any point in the game
        /// </summary>
        public bool IsDiscovered => Data->IsDiscovered;

        /// <summary>
        /// whether the tile is visible to an own unit or city in this turn
        /// </summary>
        public bool IsObserved => Data->IsObserved;

        /// <summary>
        /// whether the tile is visible to an own special commando or spy plane in this turn
        /// </summary>
        public bool IsSpiedOut => Data->IsSpiedOut;

        /// <summary>
        /// turn in which the tile was visible to an own unit or city last
        /// </summary>
        public int TurnObservedLast => TheEmpire.Map.ObservedLast[Id];

        /// <summary>
        /// whether an own city at this location would be protected by the great wall
        /// </summary>
        public bool IsGreatWallProtected => Data->IsGreatWallProtected;

        /// <summary>
        /// Whether tile can not be moved to because it's in the territory of a nation that we are in peace with but not allied.
        /// </summary>
        public bool IsDisallowedTerritory => Data->IsDisallowedTerritory;

        /// <summary>
        /// whether units located here have 2 tiles observation range (distance 5) instead of adjacent locations only
        /// </summary>
        public bool ProvidesExtendedObservationRange => Data->ProvidesExtendedObservationRange;
        #endregion

        #region terrain
        /// <summary>
        /// Exact terrain type including special resources.
        /// </summary>
        public Terrain Terrain => Data->Terrain;

        /// <summary>
        /// Base terrain type not including special resources.
        /// </summary>
        public Terrain BaseTerrain => Data->BaseTerrain;

        /// <summary>
        /// Whether it's a water tile (terrain Ocean or Shore).
        /// </summary>
        public bool IsWater => Data->IsWater;

        /// <summary>
        /// What type of special resource this location would contain if it were ground, not plains/grassland, and any prerequisite
        /// technology is researched.
        /// </summary>
        public SpecialResourceType ResourceIfGround
        {
            get
            {
                RC position = PositionInTile;
                return RC.PositionsOfBasicResourcesInTile.Contains(position) ? SpecialResourceType.Basic :
                       RC.PositionsOfScienceResourcesInTile.Contains(position) ? SpecialResourceType.Science :
                       SpecialResourceType.None;
            }
        }

        /// <summary>
        /// What type of special resource this location would contain if it were shore and any prerequisite technology is researched.
        /// </summary>
        public SpecialResourceType ResourceIfShore
        {
            get
            {
                RC position = PositionInTile;
                return position.x == 0 && position.y == 0 ? SpecialResourceType.Science :
                       RC.PositionsOfBasicResourcesInTile.Contains(position) ? SpecialResourceType.Basic :
                       SpecialResourceType.None;
            }
        }

        /// <summary>
        /// Which of Grassland or Plains this location would be if it were one of the two. Useful for planning transformations.
        /// </summary>
        public Terrain GrasslandOrPlains
        {
            get
            {
                int wrap = TheEmpire.Map.SizeX;
                int index = ((IId) Id).Index;
                int y = index / wrap;
                int x = (index - y * wrap) * 2;
                return ((x + 3 * y + 3) & 4) == 0 ? Terrain.Grassland : Terrain.Plains;
            }
        }

        /// <summary>
        /// Gets the only easily cacheable value with regard to work done on a job here - how much is left to do.
        /// </summary>
        public PlayResult GetWorkRemaining(Job job, out JobWorkRemaining work)
        {
            if (TheEmpire.Map.JobInfo[Id] == null)
            {
                PlayResult result = GetJobProgress__Turn(Job.None, out JobProgress _);
                if (result.OK)
                {
                    Debug.Assert(TheEmpire.Map.JobInfo[Id] != null);
                    work = TheEmpire.Map.JobInfo[Id][(int) job];
                }
                else
                {
                    work = new JobWorkRemaining();
                }
                return result;
            }
            else
            {
                work = TheEmpire.Map.JobInfo[Id][(int) job];
                return PlayResult.Success;
            }
        }

        /// <summary>
        /// Gets the only easily cacheable value with regard to work done on a job here - how much is left to do.
        /// Treat the returned array as immutable. It is cached, so any changes will affect future calls.
        /// </summary>
        public PlayResult GetWorkRemaining(out JobWorkRemaining[] work)
        {
            if (TheEmpire.Map.JobInfo[Id] == null)
            {
                PlayResult result = GetJobProgress__Turn(Job.None, out JobProgress _);
                if (result.OK)
                {
                    Debug.Assert(TheEmpire.Map.JobInfo[Id] != null);
                    work = TheEmpire.Map.JobInfo[Id];
                }
                else
                {
                    work = new JobWorkRemaining[Protocol.nJob];
                }
                return result;
            }
            else
            {
                work = TheEmpire.Map.JobInfo[Id];
                return PlayResult.Success;
            }
        }

        /// <summary>
        /// Gets the resources this location would produce if worked, accounting for terrain improvements, technology,
        /// special resources (including Spaceport effect if you have it), and government. Does not include effects of
        /// city improvements (so farmland is ignored) or the Lighthouse.
        /// </summary>
        public PlayResult GetResources(out BaseResourceSet resources)
        {
            TileInfo info;
            PlayResult result = TheEmpire.Play(Protocol.sGetTileInfo, Id, &info);
            resources = info.Resources;
            return result;
        }

        /// <summary>
        /// damage dealt to a unit which is not resistant to hostile terrain if that unit stays at this location for a full turn
        /// </summary>
        public int OneTurnHostileDamage => Data->OneTurnHostileDamage;

        public bool HasRiver => Data->HasRiver;
        public bool HasRoad => Data->HasRoad;
        public bool HasRailRoad => Data->HasRailRoad;
        public bool HasCanal => Data->HasCanal;
        public bool IsPolluted => Data->IsPolluted;

        /// <summary>
        /// Which, if any, of the mutually exclusive terrain improvements is built on this tile.
        /// </summary>
        public TerrainAlterations Improvement => Data->Improvement;
        #endregion

        /// <summary>
        /// Query progress of a specific settler job at this location
        /// </summary>
        /// <param name="job">the job</param>
        /// <param name="progress">the progress</param>
        /// <returns>result of operation</returns>
        public PlayResult GetJobProgress__Turn(Job job, out JobProgress progress)
        {
            fixed (JobProgress* jobProgressData = new JobProgress[Protocol.nJob])
            {
                PlayResult result = TheEmpire.Play(Protocol.sGetJobProgress, Id, jobProgressData);
                progress = jobProgressData[(int) job];
                if (result.OK)
                {
                    JobWorkRemaining[] workRemaining = new JobWorkRemaining[Protocol.nJob];
                    for (int j = 0; j < Protocol.nJob; j++)
                    {
                        workRemaining[j] = new JobWorkRemaining(jobProgressData[j].Required, jobProgressData[j].Done);
                    }
                    TheEmpire.Map.JobInfo[Id] = workRemaining;
                }
                return result;
            }
        }

        /// <summary>
        /// Nation to who's territory this location belongs. Nation.None if none.
        /// </summary>
        public Nation TerritoryNation
        {
            get
            {
                NationId id = TheEmpire.Map.Territory[Id];
                return id.IsValid ? new Nation(TheEmpire, id) : Nation.None;
            }
        }

        /// <summary>
        /// Whether a non-civil unit will cause unrest in it's home city if placed at this location.
        /// </summary>
        public bool MayCauseUnrest
        {
            get
            {
                switch (TheEmpire.Government)
                {
                    case Government.Republic:
                    case Government.FutureSociety:
                        {
                            NationId id = TheEmpire.Map.Territory[Id];
                            return id.IsValid && TheEmpire.RelationTo(new Nation(TheEmpire, id)) < Relation.Alliance;
                        }
                    case Government.Democracy:
                        {
                            NationId id = TheEmpire.Map.Territory[Id];
                            return !id.IsValid || TheEmpire.RelationTo(new Nation(TheEmpire, id)) < Relation.Alliance;
                        }
                    default:
                        return false;
                }
            }
        }

        #region unit info
        public bool HasOwnUnit => Data->HasOwnUnit;
        public bool HasOwnZoCUnit => Data->HasOwnZoCUnit;
        public bool HasForeignUnit => Data->HasForeignUnit;
        public bool HasAnyUnit => Data->HasAnyUnit;
        public bool HasForeignSubmarine => Data->HasForeignSubmarine;
        public bool HasForeignStealthUnit => Data->HasForeignStealthUnit;
        public bool IsInForeignZoC => Data->IsInForeignZoC;

        /// <summary>
        /// Own unit that would defend an enemy attack to this location. null if no own unit present.
        /// </summary>
        public Unit OwnDefender
        {
            get
            {
                if (!HasOwnUnit)
                    return null;
                int data;
                return !TheEmpire.Play(Protocol.sGetDefender, Id, &data).OK ? null
                    : TheEmpire.Units[new UnitId((short) data)];
            }
        }

        /// <summary>
        /// Foreign unit that would defend an attack to this location. null if no foreign unit present.
        /// </summary>
        public IUnitInfo ForeignDefender => !HasForeignUnit ? null : TheEmpire.ForeignUnits.GetForeignDefender(this);

        /// <summary>
        /// Unit that would defend an attack to this location. null if no unit present.
        /// </summary>
        public IUnitInfo Defender => HasOwnUnit ? OwnDefender : HasForeignUnit ? ForeignDefender : null;
        #endregion

        #region city info
        public bool HasOwnCity => Data->HasOwnCity;
        public bool HasForeignCity => Data->HasForeignCity;
        public bool HasAnyCity => Data->HasAnyCity;

        /// <summary>
        /// Own city at this location. null if no own city present.
        /// </summary>
        public City OwnCity => HasOwnCity ? TheEmpire.Cities[Id] : null;

        /// <summary>
        /// Foreign city at this location. null if no foreign city present.
        /// </summary>
        public ForeignCity ForeignCity => HasForeignCity ? TheEmpire.ForeignCities[Id] : null;

        /// <summary>
        /// City at this location. null if no city present.
        /// </summary>
        public ICity City => HasOwnCity ? (ICity) OwnCity : (HasForeignCity ? ForeignCity : null);

        /// <summary>
        /// Own city that is exploiting this tile. null if not exploited or exploited by foreign city.
        /// </summary>
        /// <returns></returns>
        public City GetExploitingCity__Turn()
        {
            if (!IsValid)
                return null;
            City city = null;
            TileInfo tileInfo;
            if (!TheEmpire.Play(Protocol.sGetCityTileInfo, Id, &tileInfo).OK)
                return null;
            if (tileInfo.ExploitingCity.IsValid)
                city = TheEmpire.Cities[tileInfo.ExploitingCity];
#if DEBUG
            if (city != null && ((city.Location - this).Distance > 5 || !city.ExploitedLocations.Contains(this)))
                throw new Exception("City exploited locations information from server is inconsistent!");
#endif
            return city;
        }

        /// <summary>
        /// Gets the city that is exploiting this tile (null if not exploited or exploited by foreign city), and the
        /// resources it is producing, accounting for terrain improvements, technology, special resources (including
        /// Spaceport effect if you have it), city improvements, wonders, and government
        /// </summary>
        public (City city, BaseResourceSet resources) GetExploitingCityAndResources()
        {
            TileInfo info;
            return TheEmpire.Play(Protocol.sGetCityTileInfo, Id, &info).OK && info.ExploitingCity.IsValid
                ? (TheEmpire.Cities[info.ExploitingCity], info.Resources)
                : (null, new BaseResourceSet());
        }

        /// <summary>
        /// Gets the resources this location would produce if worked, accounting for terrain improvements, technology,
        /// special resources (including Spaceport effect if you have it), city improvements, wonders, and government,
        /// if worked by the given city.
        /// </summary>
        public PlayResult GetResourcesForCity(City city, out BaseResourceSet resources)
        {
            TileInfo info = new TileInfo(city.Id);
            PlayResult result = TheEmpire.Play(Protocol.sGetHypoCityTileInfo, Id, &info);
            resources = info.Resources;
            return result;
        }
        #endregion
    }
}
