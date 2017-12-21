using System;
using System.Collections.Generic;
using System.Diagnostics;
using Common;

namespace CevoAILib
{
    struct TravelDistance
    {
        public static TravelDistance Invalid => new TravelDistance(-1, 0, true);

        public readonly int Turns;
        public readonly int MovementLeft;
        public readonly bool NewTurn; 
        // NewTurn does not correspond to Turns since Turns might already be increased for mountain delay or hostile
        // terrain recovery although location is reached within same turn

        public TravelDistance(int turns, int movementLeft, bool newTurn)
        {
            if (turns < 0)
            {
                Turns = -1;
                MovementLeft = 0;
                NewTurn = true;
            }
            else
            {
                Turns = turns;
                MovementLeft = movementLeft;
                NewTurn = newTurn;
            }
        }

        public override string ToString() => $"{Turns}.{MovementLeft}";

        public static bool operator <(TravelDistance d1, TravelDistance d2) => Comparison(d1, d2) < 0;
        public static bool operator >(TravelDistance d1, TravelDistance d2) => Comparison(d1, d2) > 0;
        public static bool operator ==(TravelDistance d1, TravelDistance d2) => Comparison(d1, d2) == 0;
        public static bool operator !=(TravelDistance d1, TravelDistance d2) => Comparison(d1, d2) != 0;
        public static bool operator <=(TravelDistance d1, TravelDistance d2) => Comparison(d1, d2) <= 0;
        public static bool operator >=(TravelDistance d1, TravelDistance d2) => Comparison(d1, d2) >= 0;
        public override bool Equals(object obj)
        {
            Debug.Assert(obj is TravelDistance);
            return Comparison(this, (TravelDistance) obj) == 0;
        }

        public override int GetHashCode() => (Turns + 2) << 12 - MovementLeft;

        public static int Comparison(TravelDistance d1, TravelDistance d2) =>
            ((d1.Turns + 2) << 12) - d1.MovementLeft - ((d2.Turns + 2) << 12) + d2.MovementLeft;
    }

    class Sprawl : IEnumerable<Location>
    {
        protected readonly AEmpire TheEmpire;
        protected readonly LocationId OriginId;
        protected readonly int StartValue;
        protected LocationId ApproachLocationId = new LocationId(-1);
        protected bool ApproachLocationWasIterated = false;
        protected readonly AddressPriorityQueue Q;
        protected LocationId CurrentLocationId = new LocationId(-1);
        protected int CurrentValue = 0;
        protected IdIndexedArray<LocationId, LocationId> Backtrace;
        private SprawlEnumerator Enumerator = null;

        public Sprawl(AEmpire empire, Location origin, int startValue)
        {
            TheEmpire = empire;
            OriginId = origin.Id;
            StartValue = startValue;
            Q = new AddressPriorityQueue(empire.Map.Size - 1);
            Backtrace = new IdIndexedArray<LocationId, LocationId>(empire.Map.Size);
        }

        public bool WasIterated(Location location)
        {
            if (location.Id == ApproachLocationId)
                return ApproachLocationWasIterated;
            else
            {
                int distance = Q.Distance(((IId) location.Id).Index);
                return distance != AddressPriorityQueue.Unknown && distance != AddressPriorityQueue.Disallowed;
            }
        }

        public Location[] Path(Location location)
        {
            if (WasIterated(location))
            {
                int stepCount = 0;
                LocationId locationId = location.Id;
                if (locationId == ApproachLocationId)
                    locationId = Backtrace[locationId];
                while (locationId != OriginId)
                {
                    stepCount++;
                    locationId = Backtrace[locationId];
                }
                Location[] result = new Location[stepCount];
                locationId = location.Id;
                if (locationId == ApproachLocationId)
                    locationId = Backtrace[locationId];
                while (locationId != OriginId)
                {
                    stepCount--;
                    result[stepCount] = new Location(TheEmpire, locationId);
                    locationId = Backtrace[locationId];
                }
                return result;
            }
            else
                return null; // not reached yet
        }

        protected enum StepValidity { Ok, ForbiddenStep, ForbiddenLocation }

        protected virtual StepValidity Step(LocationId fromLocationId, LocationId toLocationId, int distance, int fromValue, ref int toValue)
        {
            toValue = fromValue + distance;
            return StepValidity.Ok;
        }

        protected virtual bool MoveNext()
        {
            bool approached = false;
            if (CurrentLocationId.IsValid && CurrentLocationId != ApproachLocationId)
            { // first check to reach neighbors from last iterated location
                foreach (OtherLocation otherLocation in TheEmpire.Map.NeighborOtherLocations[CurrentLocationId])
                {
                    LocationId nextLocationId = otherLocation.Location.Id;
                    if (nextLocationId == ApproachLocationId && !ApproachLocationWasIterated)
                    {
                        Backtrace[nextLocationId] = CurrentLocationId;
                        approached = true;
                    }
                    else if (Q.Distance(((IId) nextLocationId).Index) == AddressPriorityQueue.Unknown)
                    {
                        int nextValue = 0;
                        switch (Step(CurrentLocationId, nextLocationId, otherLocation.RC.Distance, CurrentValue, ref nextValue))
                        {
                            case StepValidity.Ok:
                            {
                                if (Q.Offer(((IId) nextLocationId).Index, nextValue))
                                    Backtrace[nextLocationId] = CurrentLocationId;
                                break;
                            }
                            case StepValidity.ForbiddenStep: break; // just don't offer
                            case StepValidity.ForbiddenLocation: Q.Disallow(((IId) nextLocationId).Index); break; // don't try to reach from any direction again
                        }
                    }
                }
            }

            if (approached)
            {
                CurrentLocationId = ApproachLocationId;
                ApproachLocationWasIterated = true;
                return true;
            }
            else
            {
                bool retval = Q.TakeClosest(out int nextIdNum, out CurrentValue);
                CurrentLocationId = new LocationId(nextIdNum);
                return retval;
            }
        }

        void EnumerationEnded()
        {
            if (Enumerator == null)
                throw new Exception("Error in Sprawl: Only started foreach loop can end!");
            Enumerator.DisposeEvent -= EnumerationEnded;
            Enumerator = null;
        }

        #region IEnumerable members
        private class SprawlEnumerator : IEnumerator<Location>
        {
            private readonly Sprawl Parent;
            public SprawlEnumerator(Sprawl parent) => Parent = parent;

            public delegate void DisposeEventHandler();
            public event DisposeEventHandler DisposeEvent;
            public void Reset() => throw new NotSupportedException();
            public bool MoveNext() => Parent.MoveNext();
            public void Dispose() => DisposeEvent();
            public Location Current => new Location(Parent.TheEmpire, Parent.CurrentLocationId);
            object System.Collections.IEnumerator.Current => Current;
        }

        public IEnumerator<Location> GetEnumerator()
        {
            if (Enumerator != null)
                throw new Exception("Sprawl: Nested iteration is not supported!");
            Q.Clear();
            Q.Offer(((IId) OriginId).Index, StartValue);
            CurrentLocationId = new LocationId(-1);
            ApproachLocationWasIterated = false;
            Enumerator = new SprawlEnumerator(this);
            Enumerator.DisposeEvent += EnumerationEnded;
            return Enumerator;
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        #endregion
    }

    /// <summary>
    /// A formation iterator, where the formation might be an island, waters or coherent tiles of the Shore terrain type only.
    /// Tiles not discovered yet always count as end of formation and are never iterated.
    /// </summary>
    class RestrictedSprawl : Sprawl
    {
        public enum TerrainGroup { AllLand, AllWater, Shore }

        private readonly TerrainGroup Restriction;

        public RestrictedSprawl(AEmpire empire, Location origin, TerrainGroup restriction)
            : base(empire, origin, 0) => Restriction = restriction;

        public int Distance(Location location)
        {
            int distance = Q.Distance(((IId) location.Id).Index);
            if (distance != AddressPriorityQueue.Unknown && distance != AddressPriorityQueue.Disallowed)
                return distance;
            else
                return -1; // not reached yet
        }

        protected override StepValidity Step(LocationId fromLocationId, LocationId toLocationId, int distance, int fromValue, ref int toValue)
        {
            Location toLocation = new Location(TheEmpire, toLocationId);
            toValue = fromValue + distance;
            switch (Restriction)
            {
                default: // TerrainGroup.AllLand:
                    if (toLocation.IsDiscovered && !toLocation.IsWater)
                        return StepValidity.Ok;
                    else
                        return StepValidity.ForbiddenLocation;

                case TerrainGroup.AllWater:
                    if (toLocation.IsDiscovered && toLocation.IsWater)
                        return StepValidity.Ok;
                    else
                        return StepValidity.ForbiddenLocation;

                case TerrainGroup.Shore:
                    if (toLocation.BaseTerrain == Terrain.Shore)
                        return StepValidity.Ok;
                    else
                        return StepValidity.ForbiddenLocation;
            }
        }
    }

    /// <summary>
    /// A unit movement iterator.
    /// </summary>
    class TravelSprawl : Sprawl
    {
        [Flags]
        public enum Options
        {
            None = 0x00, IgnoreBlocking = 0x001, IgnoreZoC = 0x002, IgnoreTreaty = 0x004, EmptyPlanet = 0x007,
            ZeroCostRailroad = 0x010, TerrainResistant = 0x020, Overweight = 0x040, Alpine = 0x080, Navigation = 0x100
        }

        private const int NewTurn = 0x1;

        protected ModelDomain Domain;
        protected int Speed;
        protected Options MovementOptions;
        protected int BaseDifficultMoveCost;
        protected int BaseRailroadMoveCost;

        /// <summary>
        /// Create general unit movement iterator for an existing or hypothetical unit.
        /// </summary>
        /// <param name="empire">empire</param>
        /// <param name="nation">nation of the unit</param>
        /// <param name="origin">initial unit location</param>
        /// <param name="domain">unit domain</param>
        /// <param name="unitSpeed">speed of the unit</param>
        /// <param name="initialMovementLeft">initial movement points left</param>
        /// <param name="movementOptions">options</param>
        public TravelSprawl(AEmpire empire, Nation nation, Location origin, ModelDomain domain, int unitSpeed, int initialMovementLeft, Options movementOptions)
            : base(empire, origin, (unitSpeed - initialMovementLeft) << 1)
        {
            Domain = domain;
            Speed = unitSpeed;
            MovementOptions = movementOptions;
            if (nation != empire.Us)
                MovementOptions |= Options.EmptyPlanet; // default location info relates to own nation, so it can't be considered then
            if (nation.HasWonder(Building.ShinkansenExpress))
                MovementOptions |= Options.ZeroCostRailroad;
            if (nation.HasWonder(Building.HangingGardens))
                MovementOptions |= Options.TerrainResistant;
            BaseDifficultMoveCost = 100 + (unitSpeed - 150) / 5;
            if ((MovementOptions & Options.ZeroCostRailroad) != 0)
                BaseRailroadMoveCost = 0;
            else
                BaseRailroadMoveCost = (unitSpeed / 50) * 4;
        }

        /// <summary>
        /// Unit movement iterator for an own unit.
        /// </summary>
        /// <param name="empire">empire</param>
        /// <param name="unit">the unit</param>
        public TravelSprawl(AEmpire empire, AUnit unit)
            : this(empire, unit.Nation, unit.Location, unit.Model.Domain, unit.Speed, unit.MovementLeft, Options.None)
        {
            SetOptionsFromUnit(unit);
        }

        /// <summary>
        /// Special unit movement iterator for planning additional movement after another not yet executed movement.
        /// </summary>
        /// <param name="empire">empire</param>
        /// <param name="unit">the unit</param>
        /// <param name="startLocation">location the unit will be at to start this movement</param>
        /// <param name="movementLeft">movement left over after the previous movement</param>
        public TravelSprawl(AEmpire empire, AUnit unit, Location startLocation, int movementLeft)
            : this(empire, unit.Nation, startLocation, unit.Model.Domain, unit.Speed, movementLeft, Options.None)
        {
            SetOptionsFromUnit(unit);
        }

        /// <summary>
        /// Special unit movement iterator when the goal is to move an own unit adjacent to a certain location.
        /// </summary>
        /// <param name="empire">empire</param>
        /// <param name="unit">the unit</param>
        /// <param name="approachLocation">location to approach to</param>
        public TravelSprawl(AEmpire empire, AUnit unit, Location approachLocation)
            : this(empire, unit)
        {
            ApproachLocationId = approachLocation.Id;
            if (unit.Location != approachLocation)
                Q.Disallow(((IId) ApproachLocationId).Index);
        }

        /// <summary>
        /// Unit movement iterator for a foreign unit.
        /// </summary>
        /// <param name="empire">empire</param>
        /// <param name="unit">the unit</param>
        public TravelSprawl(AEmpire empire, ForeignUnit unit)
            : this(empire, unit.Nation, unit.Location, unit.Model.Domain, unit.Speed, unit.Speed, Options.None)
        {
            SetOptionsFromUnit(unit);
        }

        public TravelDistance Distance(Location location)
        {
            int distance = 0;
            if (location.Id == ApproachLocationId)
            {
                if (!ApproachLocationWasIterated)
                    return TravelDistance.Invalid; // not reached yet

                distance = Q.Distance(((IId) Backtrace[ApproachLocationId]).Index);
            }
            else
                distance = Q.Distance(((IId) location.Id).Index);
            if (distance != AddressPriorityQueue.Unknown && distance != AddressPriorityQueue.Disallowed)
                return new TravelDistance(distance >> 12, Speed - ((distance >> 1) & 0x7FF), (distance & NewTurn) != 0);
            else
                return TravelDistance.Invalid; // not reached yet
        }

        /// <summary>
        /// damage the unit would receive from hostile terrain travelling to a location before it reaches an intermediate non-hostile terrain location
        /// </summary>
        /// <param name="fromLocation">the location to travel from</param>
        /// <param name="toLocation">the location to travel to</param>
        /// <returns>the damage</returns>
        public int DamageToNextNonHostileLocation(Location fromLocation, Location toLocation)
        {
            if ((MovementOptions & Options.TerrainResistant) == 0 && WasIterated(toLocation))
            {
                int damage = 0;
                LocationId locationId = toLocation.Id;
                if (locationId == ApproachLocationId)
                    locationId = Backtrace[locationId];
                int sourceTerrainDamage = 0;
                int sourceDistance = 0;
                int destinationTerrainDamage = new Location(TheEmpire, locationId).OneTurnHostileDamage;
                int destinationDistance = Q.Distance(((IId) locationId).Index);
                while (locationId != fromLocation.Id)
                {
                    locationId = Backtrace[locationId];
                    sourceTerrainDamage = new Location(TheEmpire, locationId).OneTurnHostileDamage;
                    sourceDistance = Q.Distance(((IId) locationId).Index);
                    if (locationId != fromLocation.Id && sourceTerrainDamage == 0)
                        damage = 0;
                    else if ((destinationDistance & NewTurn) != 0)
                    { // move has to wait for next turn
                        if (sourceTerrainDamage > 0 &&
                            ((sourceDistance >> 1) & 0x7FF) < Speed) // movement left
                            damage += (sourceTerrainDamage * (Speed - ((sourceDistance >> 1) & 0x7FF)) - 1) / Speed + 1; // unit spends rest of turn here
                        if (destinationTerrainDamage > 0)
                            damage += (destinationTerrainDamage * ((destinationDistance >> 1) & 0x7FF) - 1) / Speed + 1; // move
                    }
                    else
                    {
                        if (destinationTerrainDamage > 0)
                            damage += (destinationTerrainDamage * (((destinationDistance >> 1) & 0x7FF) - ((sourceDistance >> 1) & 0x7FF)) - 1) / Speed + 1; // move
                    }
                    destinationTerrainDamage = sourceTerrainDamage;
                    destinationDistance = sourceDistance;
                }
                return damage;
            }
            else
                return 0;
        }

        void SetOptionsFromUnit(IUnitInfo unit)
        {
            if (unit.Model.Domain != ModelDomain.Ground || unit.Model.Kind == ModelKind.SpecialCommando)
                MovementOptions |= Options.IgnoreZoC;
            if (unit.Model.Kind == ModelKind.SpecialCommando)
                MovementOptions |= Options.IgnoreTreaty;
            if (Domain != ModelDomain.Ground || unit.IsTerrainResistant)
                MovementOptions |= Options.TerrainResistant;
            if (unit.Model.HasFeature(ModelProperty.Overweight))
                MovementOptions |= Options.Overweight;
            if (unit.Model.HasFeature(ModelProperty.Alpine))
                MovementOptions |= Options.Alpine;
            if (unit.Model.HasFeature(ModelProperty.Navigation))
                MovementOptions |= Options.Navigation;
        }

        protected override StepValidity Step(LocationId fromLocationID, LocationId toLocationID, int distance, int fromValue, ref int toValue)
        {
            switch (Domain)
            {
                default: // case ModelDomain.Ground
                    {
                        LocationData fromTile = TheEmpire.Map.Ground[fromLocationID];
                        LocationData toTile = TheEmpire.Map.Ground[toLocationID];
                        int moveCost = 100;

                        if (!toTile.IsDiscovered)
                            return StepValidity.ForbiddenLocation;

                        if (toTile.HasForeignUnit && (MovementOptions & Options.IgnoreBlocking) == 0)
                            return StepValidity.ForbiddenLocation;

                        if (toTile.IsWater)
                            return StepValidity.ForbiddenLocation;

                        if (toTile.IsDisallowedTerritory && (MovementOptions & Options.IgnoreTreaty) == 0)
                        {
                            if (!fromTile.IsDisallowedTerritory ||
                                new Location(TheEmpire, fromLocationID).TerritoryNation != new Location(TheEmpire, toLocationID).TerritoryNation)
                                return StepValidity.ForbiddenStep; // treaty
                        }

                        if ((MovementOptions & Options.IgnoreZoC) == 0 &&
                            !fromTile.HasAnyCity && // not coming out of city
                            !toTile.HasOwnCity && // not moving into own city
                            fromTile.IsInForeignZoC && // fromLocation in ZoC
                            toTile.IsInForeignZoC && // toLocation in ZoC
                            !toTile.HasOwnZoCUnit) // ZoC not negated by own unit at toLocation
                            return StepValidity.ForbiddenStep; // ZoC violation

                        int terrainDamagePerTurn;
                        if (fromTile.HasRailRoad && toTile.HasRailRoad)
                            moveCost = BaseRailroadMoveCost;
                        else if ((MovementOptions & Options.Alpine) != 0 ||
                            (fromTile.HasRoad && toTile.HasRoad) ||
                            (fromTile.HasRiver && toTile.HasRiver) ||
                            (fromTile.HasCanal && toTile.HasCanal))
                        {
                            moveCost = (MovementOptions & Options.Overweight) != 0 ? 80 : 40;
                        }
                        else
                        {
                            if ((MovementOptions & Options.Overweight) != 0)
                                return StepValidity.ForbiddenStep;

                            switch (toTile.MovementKind)
                            {
                                case MovementKind.Plain: { moveCost = 100; break; }
                                case MovementKind.Difficult: { moveCost = BaseDifficultMoveCost; break; }

                                case MovementKind.Mountains:
                                    {
                                        if (((fromValue >> 1) & 0x7FF) == 0) // only possible in first step
                                            toValue = (fromValue & 0x7FFFF000) + 0x1000 + (Speed << 1);
                                        else
                                        {
                                            toValue = ((fromValue & 0x7FFFF000) + 0x2000 + (Speed << 1)) | NewTurn; // must wait for next turn
                                            terrainDamagePerTurn = fromTile.OneTurnHostileDamage;
                                            if ((MovementOptions & Options.TerrainResistant) == 0
                                                && ((fromValue >> 1) & 0x7FF) < Speed // movement left
                                                && terrainDamagePerTurn != 0)
                                            { // add recovery turns for waiting on hostile terrain
                                                int waitDamage = (terrainDamagePerTurn * (Speed - ((fromValue >> 1) & 0x7FF)) - 1) / Speed + 1;
                                                toValue += ((waitDamage + 4) >> 3) << 12; // actually: toValue += Math.Round(waitDamage / Cevo.RecoveryOutsideCity) << 12
                                            }
                                        }
                                        return StepValidity.Ok;
                                    }
                            }
                        }
                        if (distance == 3)
                            moveCost += moveCost >> 1;
                        int damageMovement = 0;
                        terrainDamagePerTurn = fromTile.OneTurnHostileDamage;
                        if ((MovementOptions & Options.TerrainResistant) == 0
                            && terrainDamagePerTurn != 0)
                            damageMovement = moveCost;

                        if (((fromValue >> 1) & 0x7FF) + moveCost <= Speed && ((fromValue >> 1) & 0x7FF) < Speed)
                            toValue = (fromValue & ~NewTurn) + (moveCost << 1);
                        else
                        {
                            toValue = ((fromValue & 0x7FFFF000) + 0x1000 + (moveCost << 1)) | NewTurn; // must wait for next turn

                            if ((MovementOptions & Options.TerrainResistant) == 0
                                && terrainDamagePerTurn != 0) // arctic or desert but not an oasis
                                damageMovement += Speed - ((fromValue >> 1) & 0x7FF);
                        }
                        if (damageMovement > 0) // add recovery turns for waiting on hostile terrain and moving in it
                        {
                            int damage = (Cevo.DamagePerTurnInDesert * damageMovement - 1) / Speed + 1;
                            toValue += ((damage + 4) >> 3) << 12; // actually: toValue += Math.Round(damage / Cevo.RecoveryOutsideCity) << 12
                        }

                        return StepValidity.Ok;
                    }

                case ModelDomain.Sea:
                    {
                        LocationData toTile = TheEmpire.Map.Ground[toLocationID];
                        if (!toTile.HasCanal && !toTile.IsWater)
                            return StepValidity.ForbiddenLocation;
                        if (toTile.Terrain == Terrain.Ocean && (MovementOptions & Options.Navigation) == 0)
                            return StepValidity.ForbiddenLocation;

                        int moveCost = 100;
                        if (distance == 3)
                            moveCost = 150;
                        if (((fromValue >> 1) & 0x7FF) + moveCost <= Speed && ((fromValue >> 1) & 0x7FF) < Speed)
                            toValue = (fromValue & ~NewTurn) + (moveCost << 1);
                        else
                            toValue = ((fromValue & 0x7FFFF000) + 0x1000 + (moveCost << 1)) | NewTurn; // must wait for next turn
                        return StepValidity.Ok;
                    }

                case ModelDomain.Air:
                    {
                        int moveCost = 100;
                        if (distance == 3)
                            moveCost = 150;
                        if (((fromValue >> 1) & 0x7FF) + moveCost <= Speed && ((fromValue >> 1) & 0x7FF) < Speed)
                            toValue = (fromValue & ~NewTurn) + (moveCost << 1);
                        else
                            toValue = ((fromValue & 0x7FFFF000) + 0x1000 + (moveCost << 1)) | NewTurn; // must wait for next turn
                        return StepValidity.Ok;
                    }
            }
        }
    }

    /// <summary>
    /// An island iterator. Same set of locations as a RestrictedSprawl with AllLand option but with different order of
    /// iteration. Simulates the movement of a standard slow ground unit so tiles that are easier to reach are
    /// iterated earlier.
    /// Tiles not discovered yet count as end of the island and are never iterated.
    /// </summary>
    class ExploreSprawl : TravelSprawl
    {
        public ExploreSprawl(AEmpire empire, Location origin)
            : base(empire, empire.Us, origin, ModelDomain.Ground, 150, 150, Options.EmptyPlanet)
        {
        }
    }
}
