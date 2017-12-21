using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using AI;

namespace CevoAILib
{
    enum SpyMission { SabotageProduction = 1, StealMaps = 2, CollectThirdNationKnowledge = 3, PrepareDossier = 4, PrepareMilitaryReport = 5 }

    struct BattleOutcome
    {
        public readonly int EndHealthOfAttacker;
        public readonly int EndHealthOfDefender;

        public BattleOutcome(int endHealthOfAttacker, int endHealthOfDefender)
        {
            EndHealthOfAttacker = endHealthOfAttacker;
            EndHealthOfDefender = endHealthOfDefender;
        }

        public override string ToString() => $"A{EndHealthOfAttacker} D{EndHealthOfDefender}";
    }

    /// <summary>
    /// basic unit information as available for both own and foreign units
    /// </summary>
    interface IUnitInfo
    {
        Nation Nation { get; }
        ModelBase Model { get; }
        Location Location { get; }
        bool AreOtherUnitsPresent { get; }
        bool IsLoaded { get; }
        int Speed { get; } // usually same as Model.Speed
        bool IsTerrainResistant { get; }
        int Experience { get; }
        int ExperienceLevel { get; }
        int Health { get; }
        bool IsFortified { get; }
        int Load { get; }
        int Fuel { get; }
        Job Job { get; }
    }

    unsafe class UnitList : IReadableIdIndexedArray<UnitId, Unit>
    {
        private readonly EmpireData* EmpirePtr;
        private readonly UnitData.Ptr UnitsPtr;
        private readonly AEmpire TheEmpire;
        private readonly IdIndexedList<UnitId, Unit> UnitObjects = new IdIndexedList<UnitId, Unit>();
        private Dictionary<PersistentUnitId, Unit> IdLookup = new Dictionary<PersistentUnitId, Unit>();

        public UnitList(AEmpire theEmpire)
        {
            TheEmpire = theEmpire;
            EmpirePtr = theEmpire.Data;
            UnitsPtr = EmpirePtr->UnitsData;
            theEmpire.OnStartOfTurnOrResume += Refresh;
        }

        /// <summary>
        /// This value will always be correct at the start of your turn, but will not reflect losses until your next
        /// turn. If you need a guaranteed accurate count even after losing units, iterate the list and count the
        /// iterations.
        /// </summary>
        public int Count => EmpirePtr->NumUnits;

        public Unit this[UnitId id] => UnitObjects[id];

        public Unit this[PersistentUnitId persistentId] => IdLookup[persistentId];

        public bool TryGetValue(PersistentUnitId persistentId, out Unit unit) =>
            IdLookup.TryGetValue(persistentId, out unit);

        private void Refresh()
        {
            Dictionary<PersistentUnitId, Unit> oldIdLookup = IdLookup;
            UnitObjects.Clear();
            IdLookup = new Dictionary<PersistentUnitId, Unit>();

            foreach (UnitId unitId in UnitId.Range(Count))
            {
                if (oldIdLookup.TryGetValue(UnitsPtr[unitId]->PersistentId, out Unit unit))
                    unit.UpdateId(unitId);
                else
                    unit = new Unit(TheEmpire, unitId);
                UnitObjects.Add(unit);
                IdLookup[unit.PersistentId] = unit;
            }
        }

        public IEnumerator<Unit> GetEnumerator() => UnitObjects.Where(unit => unit.Exists).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// own unit, abstract base class
    /// </summary>
    abstract unsafe class AUnit : IUnitInfo
    {
        /// <summary>
        /// movement points an own or foreign unit has per turn, considering damage and wonders
        /// </summary>
        /// <param name="unit">the unit</param>
        /// <returns>movement points</returns>
        public static int UnitSpeed(IUnitInfo unit)
        {
            if (unit.Model.Domain == ModelDomain.Sea)
            {
                int speed = unit.Model.Speed;
                if (unit.Nation.HasWonder(Building.MagellansExpedition))
                    speed += 200;
                if (unit.Health < 100)
                    speed = ((speed - 250) * unit.Health / 5000) * 50 + 250;
                return speed;
            }
            else
                return unit.Model.Speed;
        }

        protected readonly AEmpire TheEmpire;
        public UnitId Id;
        private UnitData* Data;
        private readonly PersistentUnitId CachedPersistentId;

        protected AUnit(AEmpire empire, UnitId id)
        {
            TheEmpire = empire;
            Id = id;
            Data = empire.Data->UnitsData[id];
            CachedPersistentId = PersistentId;
        }

        public override string ToString() => $"{Model}@{Data->LocationId}";

        #region IUnitInfo members
        public Nation Nation => TheEmpire.Us;
        ModelBase IUnitInfo.Model => Model;
        public Location Location => new Location(TheEmpire, Data->LocationId);

        /// <summary>
        /// whether other units are present at the same location
        /// </summary>
        public bool AreOtherUnitsPresent => (Data->Flags & UnitFlags.PartOfStack) != 0;

        /// <summary>
        /// whether unit is loaded to a ship or plane
        /// </summary>
        public bool IsLoaded => Data->TransportId.IsValid;

        /// <summary>
        /// movement points this unit has per turn, considering damage and wonders
        /// </summary>
        public int Speed => UnitSpeed(this);

        /// <summary>
        /// whether this unit passes hostile terrain without damage
        /// </summary>
        public bool IsTerrainResistant => Model.IsTerrainResistant || TheEmpire.Us.HasWonder(Building.HangingGardens);

        /// <summary>
        /// Experience points collected.
        /// </summary>
        public int Experience => Data->Experience;

        /// <summary>
        /// Experience level as applied in combat. 0 = Green ... 4 = Elite.
        /// </summary>
        public int ExperienceLevel => Experience / Cevo.ExperienceLevelCost;

        public int Health => Data->Health;
        public bool IsFortified => (Data->Flags & UnitFlags.Fortified) != 0;

        /// <summary>
        /// total number of units loaded to this unit
        /// </summary>
        public int Load => TroopLoad + AirLoad;

        /// <summary>
        /// Fuel remaining, not necessarily the same as Model.Fuel.
        /// </summary>
        public int Fuel => Data->Fuel;

        /// <summary>
        /// settler job this unit is currently doing
        /// </summary>
        public Job Job => Data->Job;
        #endregion

        public PersistentUnitId PersistentId => Data->PersistentId;
        public Model Model => TheEmpire.Models[Data->ModelId];
        public bool Exists => PersistentId == CachedPersistentId && Data->LocationId.IsValid;
        public bool IsConscripts => (Data->Flags & UnitFlags.Conscripts) != 0;
        public int MovementLeft => Data->MovementLeft;
        public bool MustPauseForMountains => (Data->Flags & UnitFlags.DelayedByMountain) != 0;
        public bool WasWithdrawn => (Data->Flags & UnitFlags.Withdrawn) != 0;
        public bool CausesUnrest => Location.MayCauseUnrest && !Model.IsCivil;
        public int TroopLoad => Data->TroopLoad;
        public int AirLoad => Data->AirLoad;
        public bool AreBombsLoaded => (Data->Flags & UnitFlags.BombsLoaded) != 0;

        /// <summary>
        /// home city, null if none
        /// </summary>
        public City Home => Data->HomeCityId.IsValid ? TheEmpire.Cities[Data->HomeCityId] : null;

        /// <summary>
        /// ship or aircraft by which this unit is currently transported, null if not transported
        /// </summary>
        public Unit Transport => Data->TransportId.IsValid ? TheEmpire.Units[Data->TransportId] : null;

        /// <summary>
        /// persistent custom value
        /// </summary>
        public int Status
        {
            get => Data->Status;
            set => Data->Status = value;
        }

        #region effective methods
        /// <summary>
        /// Move unit to a certain location.
        /// Moves along shortest possible path considering all known information.
        /// Only does the part of the move that is possible within this turn.
        /// If move has to be continued next turn the return value has the Error property Incomplete.
        /// Operation breaks even if it could be continued within the turn if a new foreign unit or city is spotted,
        /// in this case the result has the NewUnitOrCitySpotted property set.
        /// Hostile terrain is considered to find a compromise between damage and reaching the target fast.
        /// </summary>
        /// <param name="target">target location</param>
        /// <returns>result of operation</returns>
        public PlayResult MoveTo__Turn(Location target) =>
            target == Location ? PlayResult.NoChange : MoveTo(target, false);

        /// <summary>
        /// Move unit adjacent to a certain location.
        /// Moves along shortest possible path considering all known information.
        /// Only does the part of the move that is possible within this turn.
        /// If move has to be continued next turn the return value has the Error property Incomplete.
        /// Operation breaks even if it could be continued within the turn if a new foreign unit or city is spotted,
        /// in this case the result has the NewUnitOrCitySpotted property set.
        /// Hostile terrain is considered to find a compromise between damage and reaching the target fast.
        /// </summary>
        /// <param name="target">location to move adjacent to</param>
        /// <returns>result of operation</returns>
        public PlayResult MoveToNeighborOf__Turn(Location target) =>
            target.IsNeighborOf(Location) ? PlayResult.NoChange : MoveTo(target, true);

        // internal
        private PlayResult MoveTo(Location target, bool approach)
        {
            if (!target.IsValid)
                return new PlayResult(PlayError.InvalidLocation);
            if (MustPauseForMountains)
                return new PlayResult(PlayError.Incomplete);

            // pathfinding necessary
            TravelSprawl sprawl = approach ? new TravelSprawl(TheEmpire, this, target) : new TravelSprawl(TheEmpire, this);
            foreach (Location reachedLocation in sprawl)
            {
                if (reachedLocation == target)
                    break;
            }
            if (!sprawl.WasIterated(target))
                return new PlayResult(PlayError.NoWay);

            Location[] path = sprawl.Path(target);
            foreach (Location step in path)
            {
                if (sprawl.Distance(step).NewTurn)
                    return new PlayResult(PlayError.Incomplete); // has to be continued next turn
                if (!IsTerrainResistant && Location.OneTurnHostileDamage == 0 && step.OneTurnHostileDamage > 0)
                { // recover before passing hostile terrain?
                    int damageToNextNonHostileLocation = sprawl.DamageToNextNonHostileLocation(Location, target);
                    if (damageToNextNonHostileLocation >= 100)
                        return new PlayResult(PlayError.NoWay);
                    else if (Location.OneTurnHostileDamage == 0 && Health <= damageToNextNonHostileLocation)
                        return new PlayResult(PlayError.RecoverFirst);
                }

                PlayResult result = Step__Turn(step);
                if (!result.OK || result.UnitRemoved || result.NewUnitOrCitySpotted)
                    return result;
            }
            return PlayResult.Success;
        }

        /// <summary>
        /// Move unit to neighbor location.
        /// Causes loading to transport if:
        /// (1) unit is ground unit and target location is water and has transport present
        /// (2) unit is aircraft and target location has carrier present
        /// </summary>
        /// <param name="target">location to move to, must be neighbor of current location</param>
        /// <returns>result of operation</returns>
        public PlayResult Step__Turn(Location target)
        {
            if (!target.IsValid)
                return new PlayResult(PlayError.InvalidLocation);
            RC targetRC = target - Location;
            if (targetRC.Distance > 3)
                return new PlayResult(PlayError.RulesViolation);

            int moveCommand = Protocol.sMoveUnit + (((targetRC.x) & 7) << 4) + (((targetRC.y) & 7) << 7);
            HashSet<City> citiesChanged = null;
            if (Load > 0)
            {
                if ((!target.HasForeignUnit && target.MayCauseUnrest != Location.MayCauseUnrest) || // crossing border changing unrest
                    TheEmpire.TestPlay(moveCommand, Id).UnitRemoved) // transport will die
                { // reports of all home cities of transported units will become invalid
                    citiesChanged = new HashSet<City>();
                    foreach (Unit unit in TheEmpire.Units)
                        if (unit.Transport == this && unit.Home != null && !citiesChanged.Contains(unit.Home))
                            citiesChanged.Add(unit.Home);
                }
            }
            bool causedUnrestBefore = CausesUnrest;
            PlayResult result = TheEmpire.Play(moveCommand, Id);
            if (result.Effective)
            {
                if (Home != null && (!Exists || CausesUnrest != causedUnrestBefore))
                    Home.InvalidateReport();
                if (citiesChanged != null)
                    foreach (City city in citiesChanged)
                        city.InvalidateReport();

                // This flag is cleared when the tech steal is executed, so it being set means that *this* move triggered it.
                if (TheEmpire.HadEvent__Turn((EmpireEvents)Protocol.phStealTech)) // capture with temple of zeus
                    TheEmpire.StealAdvance();
            }
            return result;
        }

        /// <summary>
        /// Attack a unit. Moves along shortest possible path considering all known information.
        /// Only does the part of the move that is possible within this turn.
        /// If move has to be continued next turn the return value has the Error property Incomplete.
        /// Hostile terrain is considered to find a compromise between damage and reaching the target fast.
        /// </summary>
        /// <param name="target">unit to attack</param>
        /// <returns>result of operation</returns>
        public PlayResult Attack__Turn(Location target)
        {
            if (!target.IsValid)
                return new PlayResult(PlayError.InvalidLocation);
            PlayResult moved = MoveToNeighborOf__Turn(target);
            if (!moved.OK || moved.UnitRemoved || moved.NewUnitOrCitySpotted)
                return moved;
            else
                return Step__Turn(target);
        }

        /// <summary>
        /// Attack a unit. Moves along shortest possible path considering all known information.
        /// Only does the part of the move that is possible within this turn.
        /// If move has to be continued next turn the return value has the Error property Incomplete.
        /// Operation breaks even if it could be continued within the turn if a new foreign unit or city is spotted,
        /// in this case the result has the NewUnitOrCitySpotted property set.
        /// Hostile terrain is considered to find a compromise between damage and reaching the target fast.
        /// </summary>
        /// <param name="target">unit to attack</param>
        /// <returns>result of operation</returns>
        public PlayResult Attack__Turn(IUnitInfo target) => Attack__Turn(target.Location);

        /// <summary>
        /// Attack a city. If city is defended, attack defender. If city is undefended, capture (Ground) or bombard (Sea, Air) it.
        /// Moves along shortest possible path considering all known information.
        /// Only does the part of the move that is possible within this turn.
        /// If move has to be continued next turn the return value has the Error property Incomplete.
        /// Operation breaks even if it could be continued within the turn if a new foreign unit or city is spotted,
        /// in this case the result has the NewUnitOrCitySpotted property set.
        /// Hostile terrain is considered to find a compromise between damage and reaching the target fast.
        /// </summary>
        /// <param name="target">city to attack</param>
        /// <returns>result of operation</returns>
        public PlayResult Attack__Turn(ICity target) => Attack__Turn(target.Location);

        public PlayResult DoSpyMission__Turn(SpyMission mission, Location target)
        {
            if (!target.IsValid)
                return new PlayResult(PlayError.InvalidLocation);
            PlayResult result = TheEmpire.Play(Protocol.sSetSpyMission + ((int) mission << 4));
            if (!result.OK)
                return result;
            else
            {
                result = MoveToNeighborOf__Turn(target);
                if (!result.OK || result.UnitRemoved || result.NewUnitOrCitySpotted)
                    return result;
                else
                    return Step__Turn(target);
            }
        }

        public PlayResult DoSpyMission__Turn(SpyMission mission, ICity city) =>
            DoSpyMission__Turn(mission, city.Location);

        //bool MoveForecast__Turn(ToLoc; var RemainingMovement: integer)
        //{
        //    return true; // todo !!!
        //}

        //bool AttackForecast__Turn(ToLoc,AttackMovement; var RemainingHealth: integer)
        //{
        //    return true; // todo !!!
        //}

        //bool DefenseForecast__Turn(euix,ToLoc: integer; var RemainingHealth: integer)
        //{
        //    return true; // todo !!!
        //}

        /// <summary>
        /// Disband unit. If located in city producing a unit, utilize material.
        /// </summary>
        /// <returns>result of operation</returns>
        public PlayResult Disband__Turn()
        {
            City city = Location.OwnCity;

            HashSet<City> citiesChanged = null;
            if (Load > 0)
            {
                citiesChanged = new HashSet<City>();
                foreach (Unit unit in TheEmpire.Units)
                {
                    if (unit.Transport == this && unit.Home != null && !citiesChanged.Contains(unit.Home))
                        citiesChanged.Add(unit.Home);
                }
            }

            PlayResult result = TheEmpire.Play(Protocol.sRemoveUnit, Id);
            if (result.OK)
            {
                Home?.InvalidateReport();
                city?.InvalidateReport(); // in case unit was utilized
                if (citiesChanged != null)
                {
                    foreach (City city1 in citiesChanged)
                        city1.InvalidateReport();
                }
            }
            return result;
        }

        /// <summary>
        /// start settler job
        /// </summary>
        /// <param name="job">the job to start</param>
        /// <returns>result of operation</returns>
        public PlayResult StartJob__Turn(Job job) => TheEmpire.Play(Protocol.sStartJob + ((int)job << 4), Id);

        /// <summary>
        /// set home of unit in city it's located in
        /// </summary>
        /// <returns>result of operation</returns>
        public PlayResult SetHomeHere__Turn()
        {
            City oldHome = Home;
            PlayResult result = TheEmpire.Play(Protocol.sSetUnitHome, Id);
            if (result.OK)
            {
                oldHome?.InvalidateReport();
                Home?.InvalidateReport();
            }
            return result;
        }

        /// <summary>
        /// load unit to transport at same location
        /// </summary>
        /// <returns>result of operation</returns>
        public PlayResult LoadToTransport__Turn() => TheEmpire.Play(Protocol.sLoadUnit, Id);

        /// <summary>
        /// unload unit from transport
        /// </summary>
        /// <returns>result of operation</returns>
        public PlayResult UnloadFromTransport__Turn() => TheEmpire.Play(Protocol.sUnloadUnit, Id);

        /// <summary>
        /// if this unit is a transport, select it as target for subsequent loading of units
        /// </summary>
        /// <returns></returns>
        public PlayResult SelectAsTransport__Turn() => TheEmpire.Play(Protocol.sSelectTransport, Id);

        /// <summary>
        /// add unit to the city it's located in
        /// </summary>
        /// <returns>result of operation</returns>
        public PlayResult AddToCity__Turn()
        {
            City city = Location.OwnCity;
            PlayResult result = TheEmpire.Play(Protocol.sAddToCity, Id);
            if (result.OK)
            {
                Home?.InvalidateReport();
                city?.InvalidateReport();
            }
            return result;
        }
        #endregion

        #region template internal stuff
        /// <summary>
        /// INTERNAL - only call from CevoAILib classes!
        /// </summary>
        public void UpdateId(UnitId newId)
        {
            Id = newId;
            Data = TheEmpire.Data->UnitsData[newId];
            Debug.Assert(CachedPersistentId == Data->PersistentId);
        }
        #endregion
    }

    unsafe struct MovingUnit : IUnitInfo
    {
        private readonly AEmpire TheEmpire;
        private MovingUnitData Data;

        public MovingUnit(AEmpire empire, MovingUnitData* data)
        {
            TheEmpire = empire;
            Data = *data;
        }

        public override string ToString() => Model.ToString();

        #region IUnitInfo members
        public Nation Nation => new Nation(TheEmpire, Data.NationId);
        ModelBase IUnitInfo.Model => Model;
        Location IUnitInfo.Location => new Location(TheEmpire, Data.FromLocation);
        public bool AreOtherUnitsPresent => (Data.Flags & UnitFlags.PartOfStack) != 0;
        public bool IsLoaded => false;
        public int Speed => AUnit.UnitSpeed(this);
        public bool IsTerrainResistant => Model.IsTerrainResistant || Nation.HasWonder(Building.HangingGardens);
        public int Experience => Data.Experience;
        public int ExperienceLevel => Experience / Cevo.ExperienceLevelCost;
        int IUnitInfo.Health => Data.HealthBefore;
        public bool IsFortified => (Data.Flags & UnitFlags.Fortified) != 0;
        public int Load => Data.Load;
        public int Fuel => Data.Fuel;
        public Job Job => Job.None;
        #endregion

        public ForeignModel Model => TheEmpire.ForeignModels[Data.ModelId];
        public Location FromLocation => new Location(TheEmpire, Data.FromLocation);
        public Location ToLocation => FromLocation + Movement;
        public RC Movement => Data.Movement;
        public int HealthBefore => Data.HealthBefore;
        public int HealthAfter => Data.HealthAfter;
        public int DefenderHealthAfter => Data.DefenderHealthAfter;
    }

    /// <summary>
    /// A unit that belongs to another nation. Do not keep references to instances of this class, they may become stale
    /// without notice when the location is attacked or investigated during your turn, or for various other reasons in
    /// other players' turns.
    /// </summary>
    sealed class ForeignUnit : IUnitInfo
    {
        private readonly AEmpire TheEmpire;
        private readonly ForeignUnitData Data;

        public ForeignUnit(AEmpire empire, ForeignUnitData data)
        {
            TheEmpire = empire;
            Data = data;
        }

        public override string ToString() => $"{Model}@{Data.LocationId}";

        #region IUnitInfo members
        public Nation Nation => new Nation(TheEmpire, Data.NationId);
        ModelBase IUnitInfo.Model => Model;
        public Location Location => new Location(TheEmpire, Data.LocationId);

        /// <summary>
        /// whether other units are present at the same location
        /// </summary>
        public bool AreOtherUnitsPresent => (Data.Flags & UnitFlags.PartOfStack) != 0;

        /// <summary>
        /// Whether a foreign unit is loaded is always unknown, though might be deduced or guessed from the Load of local transports.
        /// </summary>
        public bool IsLoaded => false;

        /// <summary>
        /// movement points this unit has per turn, considering damage and wonders
        /// </summary>
        public int Speed => AUnit.UnitSpeed(this);

        /// <summary>
        /// whether this unit passes hostile terrain without damage
        /// </summary>
        public bool IsTerrainResistant => Model.IsTerrainResistant || Nation.HasWonder(Building.HangingGardens);

        /// <summary>
        /// Experience points collected.
        /// </summary>
        public int Experience => Data.Experience;

        /// <summary>
        /// Experience level as applied in combat. 0 = Green ... 4 = Elite.
        /// </summary>
        public int ExperienceLevel => Experience / Cevo.ExperienceLevelCost;

        public int Health => Data.Health;
        public bool IsFortified => (Data.Flags & UnitFlags.Fortified) != 0;

        /// <summary>
        /// total number of units loaded to this unit
        /// </summary>
        public int Load => Data.Load;

        /// <summary>
        /// Fuel remaining, not necessarily the same as Model.Fuel.
        /// </summary>
        public int Fuel => Data.Fuel;

        /// <summary>
        /// settler job this unit is currently doing
        /// </summary>
        public Job Job => Data.Job;
        #endregion

        public ForeignModel Model => TheEmpire.ForeignModels[Data.ModelId];
    }

    sealed unsafe class ForeignUnitList : IReadOnlyCollection<ForeignUnit>
    {
        private readonly AEmpire TheEmpire;
        private readonly EmpireData* EmpirePtr;
        // Behavior during your turn of the list pointed to here is that it contains only the strongest defender per
        // location, if that defender dies but there's another still there then the new one replaces it in place, if
        // defender dies with no replacement then the unit's location is set to -1, and newly discovered units are
        // appended to the end.
        //
        // Getting the other units in a spied-out stack requires a server call with the sGetUnits command. The list is
        // reset immediately before your turn and immediately after your turn.
        //
        // The logic of updates for other players' actions is more complicated and can result in places in the list
        // changing which location they refer to. I resorted to just redoing the scan of the entire list from scratch on
        // every access when it's not your turn, as I expect this information should rarely be needed outside your turn
        // and making sure I got any more optimized approach right would be difficult.
        private readonly ForeignUnitData* UnitsPtr;
        private int NumScannedIndices;
        private int LastBattleHistoryLength;
        private readonly Dictionary<LocationId, ForeignUnit> Defenders = new Dictionary<LocationId, ForeignUnit>();
        private readonly Dictionary<LocationId, ForeignUnit[]> Stacks = new Dictionary<LocationId, ForeignUnit[]>();
        private readonly HashSet<LocationId> UnSpiedStackLocations = new HashSet<LocationId>();
        private readonly Dictionary<LocationId, int> AddressIndices = new Dictionary<LocationId, int>();
        private readonly HashSet<ForeignUnit> AllForeignUnits = new HashSet<ForeignUnit>();

        public ForeignUnitList(AEmpire empire)
        {
            TheEmpire = empire;
            EmpirePtr = empire.Data;
            UnitsPtr = EmpirePtr->ForeignUnitsData;
            NumScannedIndices = 0;
            LastBattleHistoryLength = EmpirePtr->BattleHistoryData.Count;
            empire.OnStartOfTurnOrResume += Reset;
            ScanAdditionsToInGameList();
        }

        public ForeignUnit GetForeignDefender(Location location)
        {
            CheckForUpdates();
            return Defenders.TryGetValue(location.Id, out ForeignUnit defender) ? defender : null;
        }

        public ForeignUnit[] GetForeignStack(Location location)
        {
            CheckForUpdates();
            return Stacks.TryGetValue(location.Id, out ForeignUnit[] stack) ? stack : null;
        }

        public IReadOnlyCollection<ForeignUnit> AllForeignDefenders
        {
            get
            {
                CheckForUpdates();
                return Defenders.Values;
            }
        }

        public IReadOnlyCollection<ForeignUnit[]> AllForeignStacks
        {
            get
            {
                CheckForUpdates();
                return Stacks.Values;
            }
        }

        private void CheckForUpdates()
        {
            if (!TheEmpire.IsMyTurn)
            {
                Reset();
                return;
            }
            if (NumScannedIndices < EmpirePtr->NumForeignDefendedLocations)
                ScanAdditionsToInGameList();
            for (; LastBattleHistoryLength < EmpirePtr->BattleHistoryData.Count; LastBattleHistoryLength++)
            {
                BattleRecordData* battle = EmpirePtr->BattleHistoryData[LastBattleHistoryLength];
                if (battle->BattleType == BattleType.Attack)
                    RescanLocation(battle->DefenderLocationId);
            }
            foreach (LocationId location in UnSpiedStackLocations)
                if (EmpirePtr->MapData[location].IsSpiedOut)
                    RescanLocation(location);
        }

        private void Reset()
        {
            Defenders.Clear();
            Stacks.Clear();
            UnSpiedStackLocations.Clear();
            AddressIndices.Clear();
            AllForeignUnits.Clear();
            NumScannedIndices = 0;
            LastBattleHistoryLength = EmpirePtr->BattleHistoryData.Count;
            ScanAdditionsToInGameList();
        }

        private void RescanLocation(LocationId id)
        {
            ScanAdditionsToInGameList();

            if (!AddressIndices.TryGetValue(id, out int index)) return;
            AddressIndices.Remove(id);

            if (Defenders.TryGetValue(id, out ForeignUnit defender))
            {
                Defenders.Remove(id);
                AllForeignUnits.Remove(defender);
            }

            if (Stacks.TryGetValue(id, out ForeignUnit[] stack))
            {
                Stacks.Remove(id);
                UnSpiedStackLocations.Remove(id);
                foreach (ForeignUnit unit in stack)
                    AllForeignUnits.Remove(unit);
            }

            ScanAtIndex(index);
        }

        private void ScanAdditionsToInGameList()
        {
            for (; NumScannedIndices < EmpirePtr->NumForeignDefendedLocations; NumScannedIndices++)
                ScanAtIndex(NumScannedIndices);
        }

        private void ScanAtIndex(int i)
        {
            if (!UnitsPtr[i].LocationId.IsValid) return;
            ForeignUnit newDefender = new ForeignUnit(TheEmpire, UnitsPtr[i]);
            AddressIndices[newDefender.Location.Id] = i;
            Defenders[newDefender.Location.Id] = newDefender;
            if (newDefender.AreOtherUnitsPresent)
            {
                ScanStack(newDefender);
                foreach (ForeignUnit unit in Stacks[newDefender.Location.Id])
                    AllForeignUnits.Add(unit);
            }
            else
            {
                AllForeignUnits.Add(newDefender);
            }
        }

        private void ScanStack(ForeignUnit stackDefender)
        {
            if (stackDefender.Location.IsSpiedOut)
            {
                int numUnitsInStack = 0;
                PlayResult result = TheEmpire.Play(Protocol.sGetUnits, stackDefender.Location.Id, &numUnitsInStack);
                Debug.Assert(result.OK);

                ForeignUnit[] stack = new ForeignUnit[numUnitsInStack];
                int startIndex = EmpirePtr->NumForeignDefendedLocations;
                for (int i = startIndex; i < startIndex + numUnitsInStack; i++)
                    stack[i] = new ForeignUnit(TheEmpire, UnitsPtr[i]);
                Stacks[stackDefender.Location.Id] = stack;
            }
            else
            {
                Stacks[stackDefender.Location.Id] = new[] { stackDefender };
                UnSpiedStackLocations.Add(stackDefender.Location.Id);
            }
        }

        #region IReadOnlyCollection members
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public IEnumerator<ForeignUnit> GetEnumerator()
        {
            CheckForUpdates();
            return AllForeignUnits.GetEnumerator();
        }

        public int Count
        {
            get
            {
                CheckForUpdates();
                return AllForeignUnits.Count;
            }
        }
        #endregion
    }
}
