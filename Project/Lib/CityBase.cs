using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using AI;

namespace CevoAILib
{
    unsafe class CityList : IReadableIdIndexedArray<CityId, City>
    {
        private readonly EmpireData* EmpirePtr;
        private readonly CityData.Ptr CitiesPtr;
        private readonly AEmpire TheEmpire;
        private readonly IdIndexedList<CityId, City> CityObjects = new IdIndexedList<CityId, City>();
        private readonly Dictionary<PersistentCityId, City> IdLookup = new Dictionary<PersistentCityId, City>();
        private readonly Dictionary<LocationId, City> LocationLookup = new Dictionary<LocationId, City>();
        private readonly Dictionary<PersistentCityId, City> ObjectsCache = new Dictionary<PersistentCityId, City>();

        public CityList(AEmpire theEmpire)
        {
            TheEmpire = theEmpire;
            EmpirePtr = theEmpire.Data;
            CitiesPtr = EmpirePtr->CitiesData;
            theEmpire.OnStartOfTurnOrResume += Refresh;
        }

        /// <summary>
        /// This value will always be correct during your turn, but will not reflect lost cities until your next turn.
        /// If you need a guaranteed accurate count during another player's turn, iterate the list and count the
        /// iterations.
        /// </summary>
        public int Count => EmpirePtr->NumCities;

        public City this[CityId id]
        {
            get
            {
                if (CityObjects.Count < Count)
                    Update();
                return CityObjects[id];
            }
        }

        public City this[PersistentCityId persistentId]
        {
            get
            {
                if (CityObjects.Count < Count)
                    Update();
                City city = IdLookup[persistentId];
                return city.Exists ? city : throw new KeyNotFoundException();
            }
        }

        public bool TryGetValue(PersistentCityId persistentId, out City city)
        {
            if (CityObjects.Count < Count)
                Update();
            if (IdLookup.TryGetValue(persistentId, out city) && city.Exists)
                return true;
            city = null;
            return false;
        }

        public City this[LocationId locationId]
        {
            get
            {
                if (CityObjects.Count < Count)
                    Update();
                City city = LocationLookup[locationId];
                return city.Exists ? city : throw new KeyNotFoundException();
            }
        }

        public bool TryGetValue(LocationId locationId, out City city)
        {
            if (CityObjects.Count < Count)
                Update();
            if (LocationLookup.TryGetValue(locationId, out city) && city.Exists)
                return true;
            city = null;
            return false;
        }

        public City this[Location location] => this[location.Id];
        public bool TryGetValue(Location location, out City city) => TryGetValue(location.Id, out city);

        private void Refresh()
        {
            CityObjects.Clear();
            IdLookup.Clear();
            LocationLookup.Clear();

            foreach (CityId cityId in CityId.Range(Count))
                RecordCityWithId(cityId);
        }

        private void Update()
        {
            var firstNewId = new CityId((short) CityObjects.Count);
            var lastNewId = new CityId((short) (Count - 1));
            foreach (CityId cityId in CityId.Range(firstNewId, lastNewId))
                RecordCityWithId(cityId);
        }

        private void RecordCityWithId(CityId cityId)
        {
            if (ObjectsCache.TryGetValue(CitiesPtr[cityId]->PersistentId, out City city))
            {
                city.UpdateId(cityId);
            }
            else
            {
                city = new City((Empire) TheEmpire, cityId);
                ObjectsCache[city.PersistentId] = city;
            }
            CityObjects.Add(city);
            IdLookup[city.PersistentId] = city;
            LocationLookup[city.Location.Id] = city;
        }

        public IEnumerator<City> GetEnumerator()
        {
            short i = 0;
            do
            {
                while (i < CityObjects.Count)
                {
                    City city = CityObjects[new CityId(i)];
                    if (city.Exists)
                        yield return city;
                    i++;
                }
                if (CityObjects.Count < Count)
                    Update();
            } while (i < CityObjects.Count);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// Input parameter for City.OptimizeExploitedLocations__Turn method
    /// </summary>
    class ResourceWeights
    {
        public enum Op {Add = 0, Multiply = 1}

        /// <summary>
        /// predefined value: max growth
        /// </summary>
        public static readonly ResourceWeights MaxGrowth =
            new ResourceWeights(120, Op.Add, 0.125, Op.Add, 0.0625, Op.Add, 0.0625, Op.Add);

        /// <summary>
        /// predefined value: max production
        /// </summary>
        public static readonly ResourceWeights MaxProduction =
            new ResourceWeights(0.0625, Op.Add, 120, Op.Add, 30, Op.Add, 1, Op.Add);

        /// <summary>
        /// predefined value: max research
        /// </summary>
        public static readonly ResourceWeights MaxResearch =
            new ResourceWeights(0.0625, Op.Add, 4, Op.Add, 4, Op.Add, 8, Op.Add);

        /// <summary>
        /// predefined value: hurry production
        /// </summary>
        public static readonly ResourceWeights HurryProduction =
            new ResourceWeights(0.5, Op.Multiply, 8, Op.Add, 2, Op.Add, 1, Op.Add);

        /// <summary>
        /// predefined value: hurry research
        /// </summary>
        public static readonly ResourceWeights HurryResearch =
            new ResourceWeights(0.5, Op.Multiply, 1, Op.Add, 1, Op.Add, 1, Op.Add);

        /// <summary>
        /// INTERNAL - only access from CevoAILib classes!
        /// </summary>
        public readonly uint Code;

        /// <summary>
        /// Weights resources using a formula of the shape Pow(A1,wA1) * Pow(A2,wA2) * ... * (B1*wB1 + B2*wB2 + ...).
        /// </summary>
        /// <param name="foodWeight">weight of food</param>
        /// <param name="foodOp">operation for food weight, Multiply = A part, Add = B part of formula</param>
        /// <param name="productionWeight">weight of production</param>
        /// <param name="productionOp">operation for production weight, Multiply = A part, Add = B part of formula</param>
        /// <param name="taxWeight">weight of tax</param>
        /// <param name="taxOp">operation for tax weight, Multiply = A part, Add = B part of formula</param>
        /// <param name="scienceWeight">weight of science</param>
        /// <param name="scienceOp">operation for science weight, Multiply = A part, Add = B part of formula</param>
        public ResourceWeights(double foodWeight, Op foodOp, double productionWeight, Op productionOp, double taxWeight,
            Op taxOp, double scienceWeight, Op scienceOp) => Code = (ItemCode(foodWeight, foodOp) << 24)
                                                                    + (ItemCode(productionWeight, productionOp) << 16)
                                                                    + (ItemCode(taxWeight, taxOp) << 8)
                                                                    + ItemCode(scienceWeight, scienceOp);

        private static uint ItemCode(double weight, Op op)
        {
            int exp = (int) (Math.Log(weight, 2.0) + Math.Log(32.0 / 31.0, 2.0) + 990) - 993;
            if (exp >= 4)
                return 0x3F | ((uint) op << 7); // above maximum

            if (exp < -4)
                exp = -4;
            uint mant = (uint) (weight * (1 << (4 - exp)) / 16.0 + 0.5);
            if (mant > 15)
                mant = 15;
            if (exp < 0)
                return mant | ((uint) (exp + 4) << 4) | ((uint) op << 7) | 0x40;
            else
                return mant | ((uint) exp << 4) | ((uint) op << 7);
        }
    }

    /// <summary>
    /// basic city information as available for both own and foreign cities
    /// </summary>
    interface ICity
    {
        bool Exists { get; }
        PersistentCityId PersistentId { get; }
        Location Location { get; }
        Nation Nation { get; }
        Nation Founder { get; }
        int SerialNo { get; }
        int Size { get; }
        bool Has(Building building);
    }

    interface ICitySpyReport : ICity
    {
        IReadOnlyList<OtherLocation> Area { get; }
        Building BuildingInProduction { get; }
        int Control { get; }
        int Corruption { get; }
        int CurrentProjectCost { get; }
        Location[] ExploitedLocations { get; }
        int FoodPile { get; }
        int FoodSupport { get; }
        int FoodSurplus { get; }
        int FreeSupport { get; }
        int HappinessBalance { get; }
        int Maintenance { get; }
        int MaterialPile { get; }
        int MaterialSupport { get; }
        int MaterialSurplus { get; }
        int Morale { get; }
        int NumberOfExploitedLocations { get; }
        int PollutionPile { get; }
        int PollutionPlus { get; }
        int ScienceOutput { get; }
        int StorageSize { get; }
        int TaxOutput { get; }
        BaseResourceSet TotalResourcesFromArea { get; }
        int TurnsTillTakeoverComplete { get; }
        ModelBase UnitInProduction { get; }
        int Unrest { get; }
        int Wealth { get; }

        bool AreaSpans(Location otherLocation);
        bool HadEvent__Turn(CityEvents cityEvent);
    }

    /// <summary>
    /// own city, abstract base class
    /// </summary>
    abstract unsafe class ACity : ICitySpyReport
    {
        protected readonly Empire TheEmpire;
        public CityId Id { get; private set; }
        private CityData* Data;
        private readonly PersistentCityId CachedPersistentId;

        protected ACity(Empire empire, CityId id)
        {
            TheEmpire = empire;
            Id = id;
            Data = empire.Data->CitiesData[id];
            CachedPersistentId = PersistentId;
            TheEmpire.OnStartOfTurnOrResume += () => _maintenance = -1;
        }

        public CitySpyReport ToSpyReport()
        {
            CitySpyData spyData = new CitySpyData(TheEmpire.Id, *Data);
            return new CitySpyReport(TheEmpire, spyData, Report);
        }

        public override string ToString() => $"{PersistentId.Founder}.{PersistentId.SerialNumber}@{Data->LocationId}";

        #region ICity Members
        /// <summary>
        /// true - city still exists, false - city has been destroyed or captured
        /// </summary>
        public bool Exists => PersistentId == CachedPersistentId && Data->LocationId.IsValid;
        public PersistentCityId PersistentId => Data->PersistentId;
        public Location Location => new Location(TheEmpire, Data->LocationId);
        public Nation Nation => TheEmpire.Us;
        public Nation Founder => new Nation(TheEmpire, PersistentId.Founder);

        /// <summary>
        /// number of cities the founding nation founded before this one
        /// </summary>
        public int SerialNo => PersistentId.SerialNumber;

        public int Size => Data->Size;

        /// <summary>
        /// Whether the city has a specific building or wonder.
        /// </summary>
        /// <param name="building">the building</param>
        /// <returns>whether building exists in this city</returns>
        public bool Has(Building building) => Data->BuiltImprovements.IsBuilt(building);
        #endregion

        private int _maintenance = -1;
        public int Maintenance
        {
            get
            {
                if (_maintenance >= 0) return _maintenance;
                _maintenance = 0;
                for (Building building = Cevo.StartOfMaintenanceRange;
                    building <= Cevo.EndOfMaintenanceRange;
                    building++)
                    if (Has(building))
                        _maintenance += Cevo.Pedia(building).Maintenance;
                return _maintenance;
            }
        }

        /// <summary>
        /// City area, i.e. the locations of all tiles that might potentially be exploited by the city, including the city location.
        /// Usually the array has 21 elements, but it's less if the city is close to the upper or lower end of the map.
        /// </summary>
        public IReadOnlyList<OtherLocation> Area => Location.Distance5AreaAndOffsets;

        /// <summary>
        /// Whether a location is in the area of the city, i.e. might potentially be exploited by it.
        /// </summary>
        /// <param name="otherLocation">the location</param>
        /// <returns>true if in area, false if not</returns>
        public bool AreaSpans(Location otherLocation) => otherLocation.IsValid && (otherLocation - Location).Distance <= 5;

        /// <summary>
        /// whether the city had a specific event in this turn
        /// </summary>
        /// <param name="cityEvent">the event</param>
        /// <returns>true if event occurred, false if not</returns>
        public bool HadEvent__Turn(CityEvents cityEvent) => (Data->Events & cityEvent) != 0;

        /// <summary>
        /// If city was captured, turns until the takeover is complete and the city can be managed. Always 0 for cities that were not captured.
        /// </summary>
        public int TurnsTillTakeoverComplete => Data->TurnsTillTakeoverComplete;

        /// <summary>
        /// food collected by the city
        /// </summary>
        public int FoodPile => Data->FoodPile;

        /// <summary>
        /// material collected by the city
        /// </summary>
        public int MaterialPile => Data->MaterialPile;

        /// <summary>
        /// pollution accumulated in the city
        /// </summary>
        public int PollutionPile => Data->PollutionPile;

        /// <summary>
        /// size of food storage
        /// </summary>
        public int StorageSize => Cevo.StorageSize[(int) TheEmpire.DifficultyLevel];

        /// <summary>
        /// number of units that might have their home in this city without requiring material support
        /// </summary>
        public int FreeSupport => Size * Cevo.Pedia(TheEmpire.Government).FreeSupport / 2;

        #region report
        public int Morale => Report.Morale;
        public int Control => Report.Control;
        public int Wealth => Report.Wealth;
        public int Unrest => 2 * Report.NumUnitsCausingUnrest;
        public int HappinessBalance => Report.HappinessBalance;
        public int FoodSupport => Report.FoodSupport;
        public int MaterialSupport => Report.MaterialSupport;
        public int CurrentProjectCost => Report.CurrentProjectCost;
        public BaseResourceSet TotalResourcesFromArea => Report.TotalResourcesFromArea;
        public int FoodSurplus => Report.FoodSurplus;
        public int MaterialSurplus => Report.MaterialSurplus;
        public int PollutionPlus => Report.PollutionPlus;
        public int Corruption => Report.Corruption;
        public int TaxOutput => Report.TaxOutput;
        public int ScienceOutput => Report.ScienceOutput;
        public int NumberOfExploitedLocations => Report.NumberOfExploitedLocations;
        #endregion

        public int IncomeAvailableForMaintenance =>
            TaxOutput
            + IncomeFromFood
            + Math.Max(MaterialPile - CurrentProjectCost, 0)
            + (Data->Project.IsTradeGoods ? MaterialSurplus : 0);

        /// <summary>
        /// This does not include proceeds from the automatic sale of a building that is being replaced or relocated.
        /// </summary>
        public int Income =>
            TaxOutput
            + IncomeFromFood
            + (Data->Project.IsBuilding ? Math.Max(MaterialPile + MaterialSurplus - CurrentProjectCost, 0) : 0)
            + (Data->Project.IsTradeGoods ? MaterialPile + MaterialSurplus : 0);
        
        private int IncomeFromFood =>
            (Size >= Cevo.MaxCitySizeBasic && FoodSurplus == 1)
                || (TheEmpire.Government == Government.FutureSociety && FoodSurplus > 0)
            ? FoodSurplus
            : 0;

        private Location[] _exploitedLocations = null;

        public Location[] ExploitedLocations =>
            _exploitedLocations ?? (_exploitedLocations = Data->ExploitedTiles.GetLocations(Location));

        ModelBase ICitySpyReport.UnitInProduction => UnitInProduction;

        /// <summary>
        /// model of unit currently in production, null if production project is not a unit
        /// </summary>
        public Model UnitInProduction
        {
            get
            {
                ModelId id = Data->Project.UnitInProduction;
                return id.IsValid ? TheEmpire.Models[id] : null;
            }
        }

        /// <summary>
        /// building currently in production, Building.None if production project is not a building
        /// </summary>
        public Building BuildingInProduction => Data->Project.BuildingInProduction;

        public bool CanSetBuildingInProduction__Turn(Building building) =>
            TheEmpire.TestPlay(Protocol.sSetCityProject, Id, ((int) building & Protocol.cpIndex) | Protocol.cpImp).OK;

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
        /// Change selection of tiles to exploit by the city.
        /// Does not touch the tile selection of other cities.
        /// </summary>
        /// <param name="resourceWeights">selection strategy: how to weight the different resource types</param>
        /// <returns>result of operation</returns>
        public PlayResult OptimizeExploitedLocations__Turn(ResourceWeights resourceWeights)
        {
            PlayResult result;
            fixed (uint* cityTileAdvice = new uint[20])
            {
                cityTileAdvice[0] = resourceWeights.Code;
                result = TheEmpire.Play(Protocol.sGetCityTileAdvice, Id, cityTileAdvice);
                if (result.OK)
                    result = TheEmpire.Play(Protocol.sSetCityTiles, Id, (int) cityTileAdvice[1]);
            }
            if (result.Effective)
                InvalidateReport();
            return result;
        }

        /// <summary>
        /// Do no longer exploit any tile except the tile of the city itself. Combined with OptimizeExploitedLocations,
        /// this can be used to set priorities for tile exploitation between cities with overlapping area. Typical
        /// sequence:
        /// (1) LowPriorityCity.StopExploitation
        /// (2) HighPriorityCity.OptimizeExploitedLocations
        /// (3) LowPriorityCity.OptimizeExploitedLocations
        /// Usually calling this should be followed by an OptimizeExploitedLocations for the same city within the same
        /// turn. Otherwise the city will remain in the non-exploiting state and start to decline.
        /// </summary>
        /// <returns>result of operation</returns>
        public PlayResult StopExploitation__Turn()
        {
            PlayResult result = TheEmpire.Play(Protocol.sSetCityTiles, Id, 1<<13);
            if (result.Effective)
                InvalidateReport();
            return result;
        }

        /// <summary>
        /// Change production project to a unit.
        /// </summary>
        /// <param name="model">model of the unit to produce</param>
        /// <param name="options">options</param>
        /// <returns>result of operation</returns>
        public PlayResult SetUnitInProduction__Turn(Model model, UnitProductionOptions options)
        {
            PlayResult result = TheEmpire.Play(Protocol.sSetCityProject, Id, ((IId) model.Id).Index | (int) options);
            if (result.Effective)
                InvalidateReport();
            return result;
        }

        /// <summary>
        /// Change production project to a unit.
        /// </summary>
        /// <param name="model">model of the unit to produce</param>
        /// <returns>result of operation</returns>
        public PlayResult SetUnitInProduction__Turn(Model model) =>
            SetUnitInProduction__Turn(model, UnitProductionOptions.None);

        /// <summary>
        /// Change production project to a building or wonder.
        /// </summary>
        /// <param name="building">the building to produce</param>
        /// <returns>result of operation</returns>
        public PlayResult SetBuildingInProduction__Turn(Building building)
        {
            PlayResult result =
                TheEmpire.Play(Protocol.sSetCityProject, Id, ((int) building & Protocol.cpIndex) | Protocol.cpImp);
            if (result.Effective)
                InvalidateReport();
            return result;
        }

        /// <summary>
        /// stop production and set production to trade goods
        /// </summary>
        /// <returns>result of operation</returns>
        public PlayResult StopProduction__Turn() => SetBuildingInProduction__Turn(Building.None);

        /// <summary>
        /// buy material to complete the production in the next turn
        /// </summary>
        /// <returns>result of operation</returns>
        public PlayResult BuyMaterial__Turn() => TheEmpire.Play(Protocol.sBuyCityProject, Id);

        /// <summary>
        /// sell the progress on the current project
        /// </summary>
        /// <returns>result of operation</returns>
        public PlayResult SellProject__Turn() => TheEmpire.Play(Protocol.sSellCityProject, Id);

        /// <summary>
        /// sell an existing building
        /// </summary>
        /// <param name="building">the building to sell</param>
        /// <returns>result of operation</returns>
        public PlayResult SellBuilding__Turn(Building building)
        {
            PlayResult result = TheEmpire.Play(Protocol.sSellCityImprovement, Id, (int) building);
            if (result.Effective)
            {
                _maintenance = -1;
                if (building == Building.Palace || building == Building.StockExchange || building == Building.SpacePort)
                    TheEmpire.InvalidateAllCityReports();
                else
                    InvalidateReport();
            }
            return result;
        }

        /// <summary>
        /// rebuild an existing building
        /// </summary>
        /// <param name="building">the building to rebuild</param>
        /// <returns>result of operation</returns>
        public PlayResult RebuildBuilding__Turn(Building building)
        {
            PlayResult result = TheEmpire.Play(Protocol.sRebuildCityImprovement, Id, (int) building);
            if (result.Effective)
            {
                _maintenance = -1;
                if (building == Building.Palace || building == Building.StockExchange || building == Building.SpacePort)
                    TheEmpire.InvalidateAllCityReports();
                else
                    InvalidateReport();
            }
            return result;
        }
        #endregion

        #region template internal stuff
        private CityReportData _report;
        private bool IsReportValid = false;

        private CityReportData Report
        {
            get
            {
                if (IsReportValid) return _report;
                _report.HypotheticalExploitedTiles = -1;
                _report.HypotheticalTaxRate = -1;
                _report.HypotheticalWealthRate = -1;
                fixed (CityReportData* data = &_report)
                {
                    TheEmpire.Play(Protocol.sGetCityReportNew, Id, data);
                }
                IsReportValid = true;
                return _report;
            }
        }

        /// <summary>
        /// INTERNAL - only call from CevoAILib classes!
        /// </summary>
        public void InvalidateReport() { IsReportValid = false; _exploitedLocations = null; }

        /// <summary>
        /// INTERNAL - only call from CevoAILib classes!
        /// </summary>
        public void UpdateId(CityId newId)
        {
            Id = newId;
            Data = TheEmpire.Data->CitiesData[newId];
            InvalidateReport();
            Debug.Assert(CachedPersistentId == Data->PersistentId);
        }
        #endregion
    }

    unsafe class ForeignCityList : IReadableIdIndexedArray<ForeignCityId, ForeignCity>
    {
        private readonly EmpireData* EmpirePtr;
        private readonly ForeignCityData.Ptr CitiesPtr;
        private readonly AEmpire TheEmpire;
        private readonly IdIndexedList<ForeignCityId, ForeignCity> CityObjects =
            new IdIndexedList<ForeignCityId, ForeignCity>();
        private readonly Dictionary<PersistentCityId, ForeignCity> IdLookup =
            new Dictionary<PersistentCityId, ForeignCity>();
        private readonly Dictionary<LocationId, ForeignCity> LocationLookup = new Dictionary<LocationId, ForeignCity>();
        private readonly Dictionary<PersistentCityId, ForeignCity> ObjectsCache
            = new Dictionary<PersistentCityId, ForeignCity>();

        public ForeignCityList(AEmpire theEmpire)
        {
            TheEmpire = theEmpire;
            EmpirePtr = theEmpire.Data;
            CitiesPtr = EmpirePtr->ForeignCitiesData;
            theEmpire.OnStartOfTurnOrResume += Refresh;
        }

        /// <summary>
        /// This value will always be correct at the start of your turn, and will increase immediately when you find a
        /// new city, but will not reflect cities you capture or destroy until your next turn. If you need a guaranteed
        /// accurate count even after capturing or destroying cities during your turn, iterate the list and count the
        /// iterations.
        /// </summary>
        public int Count => EmpirePtr->NumForeignCities;

        public ForeignCity this[ForeignCityId id]
        {
            get
            {
                if (CityObjects.Count < Count)
                    Update();
                return CityObjects[id];
            }
        }

        public ForeignCity this[PersistentCityId persistentId]
        {
            get
            {
                if (CityObjects.Count < Count)
                    Update();
                ForeignCity city = IdLookup[persistentId];
                return city.Exists ? city : throw new KeyNotFoundException();
            }
        }

        public bool TryGetValue(PersistentCityId persistentId, out ForeignCity city)
        {
            if (CityObjects.Count < Count)
                Update();
            if (IdLookup.TryGetValue(persistentId, out city) && city.Exists)
                return true;
            city = null;
            return false;
        }

        public ForeignCity this[LocationId locationId]
        {
            get
            {
                if (CityObjects.Count < Count)
                    Update();
                ForeignCity city = LocationLookup[locationId];
                return city.Exists ? city : throw new KeyNotFoundException();
            }
        }

        public bool TryGetValue(LocationId locationId, out ForeignCity city)
        {
            if (CityObjects.Count < Count)
                Update();
            if (LocationLookup.TryGetValue(locationId, out city) && city.Exists)
                return true;
            city = null;
            return false;
        }

        public ForeignCity this[Location location] => this[location.Id];
        public bool TryGetValue(Location location, out ForeignCity city) => TryGetValue(location.Id, out city);

        private void Refresh()
        {
            CityObjects.Clear();
            IdLookup.Clear();
            LocationLookup.Clear();

            foreach (ForeignCityId cityId in ForeignCityId.Range(Count))
                RecordForeignCityWithId(cityId);
        }

        private void Update()
        {
            var firstNewId = new ForeignCityId((short) CityObjects.Count);
            var lastNewId = new ForeignCityId((short) (Count - 1));
            foreach (ForeignCityId cityId in ForeignCityId.Range(firstNewId, lastNewId))
                RecordForeignCityWithId(cityId);
        }

        private void RecordForeignCityWithId(ForeignCityId cityId)
        {
            if (ObjectsCache.TryGetValue(CitiesPtr[cityId]->PersistentId, out ForeignCity city))
            {
                city.UpdateId(cityId);
            }
            else
            {
                city = new ForeignCity((Empire) TheEmpire, cityId);
                ObjectsCache[city.PersistentId] = city;
            }
            CityObjects.Add(city);
            IdLookup[city.PersistentId] = city;
            LocationLookup[city.Location.Id] = city;
        }

        public IEnumerator<ForeignCity> GetEnumerator()
        {
            short i = 0;
            do
            {
                while (i < CityObjects.Count)
                {
                    ForeignCity city = CityObjects[new ForeignCityId(i)];
                    if (city.Exists)
                        yield return city;
                    i++;
                }
                if (CityObjects.Count < Count)
                    Update();
            } while (i < CityObjects.Count);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    
    /// <summary>
    /// foreign city, abstract base class
    /// </summary>
    abstract unsafe class AForeignCity : ICity
    {
        protected readonly Empire TheEmpire;
        public ForeignCityId Id { get; private set; }
        private ForeignCityData* Data;
        private readonly PersistentCityId CachedPersistentId;

        protected AForeignCity(Empire empire, ForeignCityId id)
        {
            TheEmpire = empire;
            Id = id;
            Data = empire.Data->ForeignCitiesData[id];
            CachedPersistentId = PersistentId;
        }

        public override string ToString() => $"{PersistentId.Founder}.{PersistentId.SerialNumber}@{Data->LocationId}";

        #region ICity Members
        /// <summary>
        /// true - city still exists, false - city has been captured by you or destroyed
        /// </summary>
        public bool Exists => PersistentId == CachedPersistentId && Data->LocationId.IsValid;
        public PersistentCityId PersistentId => Data->PersistentId;
        public Location Location => new Location(TheEmpire, Data->LocationId);
        public Nation Nation => new Nation(TheEmpire, Data->OwnerId);
        public Nation Founder => new Nation(TheEmpire, Data->PersistentId.Founder);

        /// <summary>
        /// number of cities the founding nation founded before this one
        /// </summary>
        public int SerialNo => Data->PersistentId.SerialNumber;

        public int Size => Data->Size;

        /// <summary>
        /// Whether the city has a specific building or wonder.
        /// Only works for buildings which are known if built in a foreign city.
        /// These are: wonders, palace, space port and all defense facilities.
        /// For all others, the return value is always false.
        /// </summary>
        /// <param name="building">the building</param>
        /// <returns>whether building exists in this city</returns>
        public bool Has(Building building)
        {
            switch (building)
            {
                case Building.Palace:
                    return (Data->ObviousBuildings & ObviousBuildings.Capital) != 0;
                case Building.SpacePort:
                    return (Data->ObviousBuildings & ObviousBuildings.Spaceport) != 0;
                case Building.CommandBunker:
                    return (Data->ObviousBuildings & ObviousBuildings.CommandBunker) != 0;
                case Building.CityWalls:
                    return (Data->ObviousBuildings & ObviousBuildings.CityWalls) != 0;
                case Building.CoastalFortress:
                    return (Data->ObviousBuildings & ObviousBuildings.CoastalFortress) != 0;
                case Building.SAM:
                    return (Data->ObviousBuildings & ObviousBuildings.MissileBattery) != 0;
                default:
                    return building < Building.WonderRange && TheEmpire.Wonder_IsInCity(building, this);
            }
        }
        #endregion

        /// <summary>
        /// city size and building information dates back to this turn
        /// </summary>
        public int TurnOfInformation => Location.TurnObservedLast;

        /// <summary>
        /// persistent custom value
        /// </summary>
        public int Status
        {
            get => Data->Status;
            set => Data->Status = value;
        }

        public bool IsSpiedOut => Location.IsSpiedOut;

        public PlayResult GetSpyReport(out CitySpyReport report)
        {
            CitySpyData data;
            PlayResult result = TheEmpire.Play(Protocol.sGetCity, Location.Id, &data);
            report = result.OK ? new CitySpyReport(TheEmpire, this, data) : null;
            return result;
        }

        #region template internal stuff
        /// <summary>
        /// INTERNAL - only call from CevoAILib classes!
        /// </summary>
        public void UpdateId(ForeignCityId newId)
        {
            Id = newId;
            Data = TheEmpire.Data->ForeignCitiesData[newId];
            Debug.Assert(CachedPersistentId == Data->PersistentId);
        }
        #endregion
    }

    /// <summary>
    /// Information about a foreign city gathered by spying.
    /// </summary>
    unsafe class CitySpyReport : ICitySpyReport
    {
        protected readonly AEmpire TheEmpire;
        private SerializedForm Serialized;
        public int TurnOfInformation => Serialized.TurnOfInformation;
        private CitySpyData Data => Serialized.Data;
        private CityReportData Report => Serialized.Report;

        public CitySpyReport(AEmpire empire, AForeignCity city, CitySpyData data)
        {
            TheEmpire = empire;
            Serialized = new SerializedForm
            {
                TurnOfInformation = city.TurnOfInformation,
                Data = data,
            };
            Maintenance = 0;
            for (Building building = Cevo.StartOfMaintenanceRange;
                building <= Cevo.EndOfMaintenanceRange;
                building++)
                if (Has(building))
                    Maintenance += Cevo.Pedia(building).Maintenance;
            fixed (CityReportData* reportData = &Serialized.Report)
            {
                PlayResult result = TheEmpire.Play(Protocol.sGetEnemyCityReportNew, Location.Id, reportData);
                Debug.Assert(result.OK);
            }
        }

        public CitySpyReport(AEmpire empire, CitySpyData data, CityReportData report)
        {
            TheEmpire = empire;
            Serialized = new SerializedForm
            {
                TurnOfInformation = empire.Turn,
                Data = data,
                Report = report
            };
            Maintenance = 0;
            for (Building building = Cevo.StartOfMaintenanceRange;
                building <= Cevo.EndOfMaintenanceRange;
                building++)
                if (Has(building))
                    Maintenance += Cevo.Pedia(building).Maintenance;
        }

        public CitySpyReport(AEmpire empire, int[] serialized)
        {
            TheEmpire = empire;
            fixed (SerializedForm* ptr = &Serialized)
            {
                Marshal.Copy(serialized, 0, (IntPtr) ptr, serialized.Length);
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct SerializedForm
        {
            public int TurnOfInformation;
            public CitySpyData Data;
            public CityReportData Report;
        }

        public int[] Serialize()
        {
            int[] array = new int[sizeof(SerializedForm) / sizeof(int)];
            fixed (SerializedForm* ptr = &Serialized)
            {
                Marshal.Copy((IntPtr) ptr, array, 0, array.Length);
            }
            return array;
        }

        public override string ToString() => $"{PersistentId.Founder}.{PersistentId.SerialNumber}@{Data.Data.LocationId}";

        #region ICity Members
        /// <summary>
        /// true - city still exists, false - city has been destroyed or captured
        /// </summary>
        public bool Exists => true;
        public PersistentCityId PersistentId => Data.Data.PersistentId;
        public Location Location => new Location(TheEmpire, Data.Data.LocationId);
        public Nation Nation => new Nation(TheEmpire, Data.Owner);
        public Nation Founder => new Nation(TheEmpire, PersistentId.Founder);

        /// <summary>
        /// number of cities the founding nation founded before this one
        /// </summary>
        public int SerialNo => PersistentId.SerialNumber;

        public int Size => Data.Data.Size;

        /// <summary>
        /// Whether the city has a specific building or wonder.
        /// </summary>
        /// <param name="building">the building</param>
        /// <returns>whether building exists in this city</returns>
        public bool Has(Building building) => Data.Data.BuiltImprovements.IsBuilt(building);
        #endregion

        public int Maintenance { get; }

        /// <summary>
        /// City area, i.e. the locations of all tiles that might potentially be exploited by the city, including the city location.
        /// Usually the array has 21 elements, but it's less if the city is close to the upper or lower end of the map.
        /// </summary>
        public IReadOnlyList<OtherLocation> Area => Location.Distance5AreaAndOffsets;

        /// <summary>
        /// Whether a location is in the area of the city, i.e. might potentially be exploited by it.
        /// </summary>
        /// <param name="otherLocation">the location</param>
        /// <returns>true if in area, false if not</returns>
        public bool AreaSpans(Location otherLocation) => otherLocation.IsValid && (otherLocation - Location).Distance <= 5;

        /// <summary>
        /// whether the city had a specific event in this turn
        /// </summary>
        /// <param name="cityEvent">the event</param>
        /// <returns>true if event occurred, false if not</returns>
        public bool HadEvent__Turn(CityEvents cityEvent) => (Data.Data.Events & cityEvent) != 0;

        /// <summary>
        /// If city was captured, turns until the takeover is complete and the city can be managed. Always 0 for cities that were not captured.
        /// </summary>
        public int TurnsTillTakeoverComplete => Data.Data.TurnsTillTakeoverComplete;

        /// <summary>
        /// food collected by the city
        /// </summary>
        public int FoodPile => Data.Data.FoodPile;

        /// <summary>
        /// material collected by the city
        /// </summary>
        public int MaterialPile => Data.Data.MaterialPile;

        /// <summary>
        /// pollution accumulated in the city
        /// </summary>
        public int PollutionPile => Data.Data.PollutionPile;

        /// <summary>
        /// size of food storage
        /// </summary>
        public int StorageSize => Cevo.StorageSize[(int) Nation.DifficultyLevel];

        /// <summary>
        /// number of units that might have their home in this city without requiring material support
        /// </summary>
        public int FreeSupport => Size * Cevo.Pedia(Nation.Government).FreeSupport / 2;

        #region report
        public int Morale => Report.Morale;
        public int Control => Report.Control;
        public int Wealth => Report.Wealth;
        public int Unrest => 2 * Report.NumUnitsCausingUnrest;
        public int HappinessBalance => Report.HappinessBalance;
        public int FoodSupport => Report.FoodSupport;
        public int MaterialSupport => Report.MaterialSupport;
        public int CurrentProjectCost => Report.CurrentProjectCost;
        public BaseResourceSet TotalResourcesFromArea => Report.TotalResourcesFromArea;
        public int FoodSurplus => Report.FoodSurplus;
        public int MaterialSurplus => Report.MaterialSurplus;
        public int PollutionPlus => Report.PollutionPlus;
        public int Corruption => Report.Corruption;
        public int TaxOutput => Report.TaxOutput;
        public int ScienceOutput => Report.ScienceOutput;
        public int NumberOfExploitedLocations => Report.NumberOfExploitedLocations;
        #endregion

        private Location[] _exploitedLocations = null;

        public Location[] ExploitedLocations =>
            _exploitedLocations ?? (_exploitedLocations = Data.Data.ExploitedTiles.GetLocations(Location));

        private ModelBase _unitInProduction = null;
        /// <summary>
        /// model of unit currently in production, null if production project is not a unit
        /// </summary>
        public ModelBase UnitInProduction
        {
            get
            {
                if (_unitInProduction != null) return _unitInProduction;
                if (!Data.Data.Project.IsUnit) return null;
                if (Nation == TheEmpire.Us)
                {
                    _unitInProduction = TheEmpire.Models[Data.Data.Project.UnitInProduction];
                    return _unitInProduction;
                }
                foreach (ForeignModel model in TheEmpire.ForeignModels)
                {
                    if (model.OwnersModelId == Data.Data.Project.ForeignUnitInProduction)
                    {
                        _unitInProduction = model;
                        return _unitInProduction;
                    }
                }
                throw new Exception("Error in city spy report: Foreign model not found!");
            }
        }

        /// <summary>
        /// building currently in production, Building.None if production project is not a building
        /// </summary>
        public Building BuildingInProduction => Data.Data.Project.BuildingInProduction;
    }
}
